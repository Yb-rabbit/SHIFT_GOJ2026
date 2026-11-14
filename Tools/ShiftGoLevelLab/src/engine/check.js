/* LevelCheck.cs 的移植：把一关丢进模拟器跑到底，返回一句人话报告 + 存档前自检。
 *
 * 这里的措辞（✅ / 📝 / ❌、超收/少收、第 N 位不匹配）是作者会直接看到的文案，
 * 与游戏内自检保持一字不差，免得同一个关卡在两个地方给出不同说法。
 */
(function (root) {
  'use strict';

  var Doc = root.BitFlowDocument;
  var Sim = root.BitFlowSimulation;
  if (!Doc && typeof require === 'function') Doc = require('./document.js');
  if (!Sim && typeof require === 'function') Sim = require('./simulation.js');

  var KIND = Doc.KIND;

  function safetyTicks(definition) {
    if (!definition) return 128;
    return Math.max(128, definition.width * definition.height * 8);
  }

  function bitsText(bits, maxShow) {
    var limit = typeof maxShow === 'number' ? maxShow : 32;
    if (!bits || bits.length === 0) return '（空）';

    var show = Math.min(bits.length, limit);
    var text = '';
    for (var i = 0; i < show; i++) text += bits[i] === 0 ? '0' : '1';
    if (bits.length > show) text += '…（共 ' + bits.length + ' 位）';
    return text;
  }

  function findReceiver(definition, receiverId) {
    for (var i = 0; i < definition.elements.length; i++) {
      var element = definition.elements[i];
      if (element.kind === KIND.Receiver && element.id === receiverId) return element;
    }
    return null;
  }

  function describe(definition, hasSupplies, result, ticks, safety) {
    /* 有手牌的关，预设布局跑不通才是对的：措辞必须能一眼区分「题还没解」和「数据错了」。 */
    var puzzle = hasSupplies && !result.isSuccess;
    var text = '';

    if (result.isSuccess) text += '✅ 通关 ';
    else if (puzzle) text += '📝 预设布局未通关 ';
    else text += '❌ 未通过 ';

    text += result.tickCount + ' ticks';
    if (puzzle) text += '（有手牌的关，这是预期：玩家要自己用牌解开）';

    /* 「跑到上限还没结算」不等于关卡数据错了，措辞要分开。 */
    if (!result.isSuccess && ticks >= safety) {
      text += '（跑到 ' + safety + ' tick 上限还没结算，可能有个位卡住了；这是自检上限，不是关卡数据错误）';
    }

    if (result.receivers.length === 0) {
      text += '\n这一关没有接收器，没法判定通关';
      return text;
    }

    for (var i = 0; i < result.receivers.length; i++) {
      var receiver = result.receivers[i];
      text += '\n  R' + receiver.receiverId + '   ';
      text += receiver.receivedCount + '/' + receiver.targetCount;

      if (receiver.receivedCount > receiver.targetCount) text += ' 超收';
      else if (receiver.receivedCount < receiver.targetCount) text += ' 少收';

      text += receiver.isSuccess ? '  ✓' : '  ✗';
      text += '   实收 ' + bitsText(receiver.received, 24);

      if (receiver.isSuccess) continue;

      /* 指出第一个不匹配的位置：作者一眼能定位是哪一段节奏错了 */
      var source = findReceiver(definition, receiver.receiverId);
      for (var m = 0; m < receiver.matched.length; m++) {
        if (receiver.matched[m]) continue;
        text += '\n      第 ' + (m + 1) + ' 位不匹配：期望 ';
        text += source ? Sim.expectedValueAt(source, m) : 1;
        text += ' 实收 ' + receiver.received[m];
        break;
      }
    }

    return text;
  }

  /* 跑一遍。返回的 sim 已经是终局状态，时间轴可以直接用它回放。 */
  function run(definition, hasSupplies) {
    var outcome = {
      success: false,
      report: '关卡定义是空的',
      ticks: 0,
      safety: 128,
      result: null,
      simulation: null,
      finished: false
    };
    if (!definition) return outcome;

    var simulation = new Sim.Simulation(definition);
    var supplies = typeof hasSupplies === 'boolean' ? hasSupplies : !!definition.hasSupplies;
    /* 上限必须与模拟器一致才有意义（模拟器自己会在 safetyTickLimit 停下）。 */
    var safety = Math.max(safetyTicks(definition), simulation.safetyTickLimit);

    var ticks = 0;
    while (!simulation.finished && ticks < safety) {
      simulation.step();
      ticks++;
    }

    var result = simulation.buildResult();
    outcome.success = result.isSuccess;
    outcome.result = result;
    outcome.simulation = simulation;
    outcome.ticks = ticks;
    outcome.safety = safety;
    outcome.finished = simulation.finished;
    outcome.report = describe(definition, supplies, result, ticks, safety);
    return outcome;
  }


  /* ---------------- 存档前自检 ----------------
   * 只查那些「无论玩家怎么动都救不回来」的错；「手牌是摆设」是 Advisory，不拦保存。 */
  function runForSaveDetailed(definition) {
    var issues = [];
    if (!definition) {
      issues.push({ severity: 'Blocking', message: '关卡定义是空的' });
      return issues;
    }

    var receivers = 0;
    var emitters = 0;
    for (var i = 0; i < definition.elements.length; i++) {
      var element = definition.elements[i];
      if (element.kind === KIND.Receiver) receivers++;
      else if (element.kind === KIND.Emitter) emitters++;
    }

    if (receivers === 0) issues.push({ severity: 'Blocking', message: '这一关没有接收器，没法判定通关' });
    if (emitters === 0) issues.push({ severity: 'Blocking', message: '这一关没有发射器，场上永远不会有位' });

    var outcome = run(definition, !!definition.hasSupplies);

    if (!definition.hasSupplies) {
      if (receivers > 0 && emitters > 0 && !outcome.success) {
        issues.push({
          severity: 'Blocking',
          message: '没有手牌，但预设布局跑不通 —— 要么补件，要么发牌：' + firstLine(outcome.report)
        });
      }
    } else {
      if (outcome.success) {
        issues.push({
          severity: 'Advisory',
          message: '预设布局自己就能通关 —— 玩家一张牌都不用出，手牌是摆设'
        });
      }

      for (var s = 0; s < definition.supplies.length; s++) {
        var supply = definition.supplies[s];
        if (!supply) continue;
        /* SupplyKinds：发射器 / 接收器是这一关的题，不能发给玩家。 */
        if (supply.kind === KIND.Emitter || supply.kind === KIND.Receiver) {
          issues.push({
            severity: 'Blocking',
            message: '手牌里有' + Doc.kindName(supply.kind) + '：' +
              '发射器 / 接收器定的是这一关的题，只能由作者给定，不能发给玩家'
          });
        }
      }

      var occupied = definition.elements.length;
      var empty = definition.width * definition.height - occupied;
      var total = 0;
      for (var c = 0; c < definition.supplies.length; c++) {
        if (definition.supplies[c]) total += Math.max(0, definition.supplies[c].count);
      }

      if (empty < total) {
        issues.push({
          severity: 'Blocking',
          message: '空位只有 ' + empty + ' 格，却发了 ' + total + ' 张牌 —— 一定有几张放不下'
        });
      }
    }

    return issues;
  }

  function runForSave(definition) {
    var problems = [];
    var issues = runForSaveDetailed(definition);
    for (var i = 0; i < issues.length; i++) {
      if (issues[i].severity === 'Blocking') problems.push(issues[i].message);
    }
    return problems;
  }

  function firstLine(text) {
    if (typeof text !== 'string' || text === '') return '';
    var index = text.indexOf('\n');
    return index <= 0 ? text : text.substring(0, index);
  }

  var api = {
    safetyTicks: safetyTicks,
    bitsText: bitsText,
    run: run,
    describe: describe,
    runForSave: runForSave,
    runForSaveDetailed: runForSaveDetailed
  };

  root.BitFlowCheck = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
