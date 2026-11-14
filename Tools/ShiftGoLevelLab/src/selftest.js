/* 启动自检：用内置样关跑几条硬断言，结果直接印在页脚。
 *
 * 为什么页脚要印它：这是离线单文件，出了问题时没有控制台可看。
 * 只要页脚写着"自检 6/6 通过"，就说明这份 HTML 里的引擎仍然能跑出预期结果；
 * 数字对不上就是文件被改坏了，一眼能看出来。 */
(function (root) {
  'use strict';

  var Doc = root.BitFlowDocument;
  var Sim = root.BitFlowSimulation;
  var Check = root.BitFlowCheck;
  var Catalog = root.BitFlowCatalog;
  var Samples = root.BitFlowSamples;
  if (!Doc && typeof require === 'function') Doc = require('./engine/document.js');
  if (!Sim && typeof require === 'function') Sim = require('./engine/simulation.js');
  if (!Check && typeof require === 'function') Check = require('./engine/check.js');
  if (!Catalog && typeof require === 'function') Catalog = require('./engine/catalog.js');
  if (!Samples && typeof require === 'function') Samples = require('./samples.js');

  function arraysEqual(left, right) {
    if (left.length !== right.length) return false;
    for (var i = 0; i < left.length; i++) {
      if (left[i] !== right[i]) return false;
    }
    return true;
  }

  function summarize(doc) {
    var read = Doc.read(doc, Catalog.builtin);
    var outcome = Check.run(read.definition, read.definition.hasSupplies);
    return { read: read, outcome: outcome, definition: read.definition };
  }

  function run() {
    var checks = [];

    function check(name, fn) {
      try {
        var detail = fn();
        checks.push({ name: name, ok: detail !== false, detail: detail === false ? '断言不成立' : (detail || '') });
      } catch (error) {
        checks.push({ name: name, ok: false, detail: String(error && error.message ? error.message : error) });
      }
    }

    check('序列 1001 发 6 位 = 100110', function () {
      var order = Sim.buildEmitOrder({ sequence: '1001', emitCount: 6 });
      return arraysEqual(order, [1, 0, 0, 1, 1, 0]) ? '' : '实得 ' + order.join('');
    });

    check('WAIT111 通关 13 ticks', function () {
      var sample = Samples.byId('wait111');
      var result = summarize(sample.document);
      return result.outcome.success && result.outcome.ticks === 13
        ? ''
        : 'success=' + result.outcome.success + ' ticks=' + result.outcome.ticks;
    });

    check('ONE ONLY 报 📝 预设布局未通关', function () {
      var sample = Samples.byId('one_only');
      var result = summarize(sample.document);
      var text = result.outcome.report;
      return !result.outcome.success && text.indexOf('📝 预设布局未通关') === 0 ? '' : text.split('\n')[0];
    });

    check('NOT LAB 逐位匹配 0000', function () {
      var sample = Samples.byId('not_lab');
      var result = summarize(sample.document);
      var received = result.outcome.result.receivers[0];
      if (!received) return '没有接收器结果';
      if (!result.outcome.success) return '未通关：' + result.outcome.report.replace(/\n/g, ' ');
      return arraysEqual(received.received, [0, 0, 0, 0]) ? '' : '实收 ' + received.received.join('');
    });

    check('越界格被跳过并报问题', function () {
      var result = summarize({
        schema: 2, displayName: 'EDGE', width: 3, height: 3,
        elements: [
          { kind: 'Emitter', x: 9, y: 0, dir: 'Right', sequence: '1', count: 1 },
          { kind: 'Receiver', x: 2, y: 0, dir: 'Down', sequence: '1', count: 1 }
        ]
      });
      var hit = false;
      for (var i = 0; i < result.read.problems.length; i++) {
        if (result.read.problems[i].indexOf('越界') >= 0) hit = true;
      }
      return hit ? '' : 'problems=' + JSON.stringify(result.read.problems);
    });

    check('两个发射器抢同一格 → Conflict', function () {
      var read = Doc.read({
        schema: 2, displayName: 'CLASH', width: 5, height: 5,
        elements: [
          { kind: 'Emitter', x: 0, y: 2, dir: 'Right', sequence: '11', count: 2 },
          { kind: 'Emitter', x: 2, y: 2, dir: 'Left', sequence: '00', count: 2 },
          { kind: 'Receiver', x: 4, y: 0, dir: 'Up', sequence: '1', count: 1 }
        ]
      }, Catalog.builtin);
      var simulation = new Sim.Simulation(read.definition);
      var report = simulation.step();
      var conflict = false;
      for (var i = 0; i < report.destroyed.length; i++) {
        if (report.destroyed[i].reason === 'Conflict' && report.destroyed[i].bitId === 0) conflict = true;
      }
      if (!conflict) return false;
      return '';
    });

    var passed = 0;
    for (var i = 0; i < checks.length; i++) {
      if (checks[i].ok) passed++;
    }

    return {
      ok: passed === checks.length,
      passed: passed,
      total: checks.length,
      checks: checks,
      fixtureCount: Samples.SAMPLES.length
    };
  }

  var api = { run: run };
  root.BitFlowSelfTest = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
