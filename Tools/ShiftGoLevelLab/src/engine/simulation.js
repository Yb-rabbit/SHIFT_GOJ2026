/* BitFlowSimulation.cs 的零依赖移植 —— 位流路由的唯一真相。
 *
 * 每个 tick 严格四步，顺序不能换（换了就会跟游戏里跑出不同结果）：
 *   0) 延迟器放行：队列里到期且出口空的位先回到自己格子上；
 *   1) 发射：各发射器尝试吐位（前方被占则顺延，多发射器抢同一格 → 一起销毁）；
 *   2) 前进：所有位按朝向走 1 格（链式阻塞允许"跟着前进"；抢格/对穿 → 全销毁）；
 *   3) 裁决：本 tick 刚进入元件格的位按元件规则改向/改值/销毁（延迟器在这里收位）。
 *
 * 事件顺序（spawns / moves / destroyed / deliveries）也照抄 C# 的遍历顺序，
 * 这样时间轴回放和"第一次销毁发生在第几 tick"才能和游戏逐帧对齐。
 */
(function (root) {
  'use strict';

  var D = root.BitFlowDir;
  var Doc = root.BitFlowDocument;
  if (!D && typeof require === 'function') D = require('./dir.js');
  if (!Doc && typeof require === 'function') Doc = require('./document.js');

  var KIND = Doc.KIND;
  var DELAY_CAPACITY = 8;
  var DEFAULT_SAFETY_TICKS = 5000;

  var DESTROY = {
    Wall: 'Wall', OutOfBounds: 'OutOfBounds', EmitterBody: 'EmitterBody',
    ReceiverWrongFace: 'ReceiverWrongFace', Conflict: 'Conflict',
    SplitterBackflow: 'SplitterBackflow', NotGateBackflow: 'NotGateBackflow',
    FilterBackflow: 'FilterBackflow', FilterRejected: 'FilterRejected',
    DelayOverflow: 'DelayOverflow'
  };

  function key(x, y) { return x + ',' + y; }

  function parseSequence(text) {
    if (typeof text !== 'string' || text === '') return [];
    var result = [];
    for (var i = text.length - 1; i >= 0; i--) {
      var c = text[i];
      if (c === '1') result.push(1);
      else if (c === '0') result.push(0);
    }
    return result;
  }

  /* 展开后的实际发射顺序，长度恒等于 emitCount（BuildEmitOrder）。 */
  function buildEmitOrder(element) {
    var order = [];
    var count = element.emitCount | 0;
    if (count <= 0) return order;

    var cycle = parseSequence(element.sequence);
    if (cycle.length === 0) return order;

    for (var i = 0; i < count; i++) order.push(cycle[i % cycle.length]);
    return order;
  }

  function expectedValueAt(receiver, index) {
    var seq = receiver.targetSequence;
    if (typeof seq !== 'string' || seq === '') return 1;
    return seq[index % seq.length] === '0' ? 0 : 1;
  }

  function acceptsEntry(receiver, entryFace) {
    return receiver.acceptAnyFace || entryFace === receiver.feedFace;
  }

  function exitDirsOf(element) {
    switch (element.kind) {
      case KIND.Emitter: return [element.facing];
      case KIND.Splitter: return [element.oneArm, element.zeroArm];
      case KIND.NotGate: return [element.outputDir];
      case KIND.Filter: return [element.outputDir];
      default: return [];
    }
  }


  function emptyReport(tick) {
    return {
      tick: tick,
      isFinished: false,
      spawns: [],
      moves: [],
      destroyed: [],
      deliveries: [],
      delayAbsorbed: [],
      delayReleased: [],
      stalledEmitters: 0,
      delayStalled: 0,
      delayDropped: 0
    };
  }

  function Simulation(definition) {
    if (!definition || !Array.isArray(definition.elements)) throw new Error('BitFlowSimulation 需要一份 LevelDefinition');
    this.level = definition;
    this.lookup = Object.create(null);
    for (var i = 0; i < definition.elements.length; i++) {
      var element = definition.elements[i];
      this.lookup[key(element.x, element.y)] = element;
    }

    this.emitters = [];
    this.receivers = [];
    this.delayGates = [];
    this.emittersById = Object.create(null);
    this.receiversById = Object.create(null);
    this.delayGatesById = Object.create(null);
    this.bits = [];
    this.occupancy = Object.create(null);
    this.tickIndex = 0;
    this.finished = false;
    this.nextBitId = 1;
    this.safetyTickLimit = DEFAULT_SAFETY_TICKS;
    this.reset();
  }

  Simulation.prototype.reset = function () {
    this.tickIndex = 0;
    this.finished = false;
    this.nextBitId = 1;
    this.bits = [];
    this.occupancy = Object.create(null);
    this.emitters = [];
    this.receivers = [];
    this.delayGates = [];
    this.emittersById = Object.create(null);
    this.receiversById = Object.create(null);
    this.delayGatesById = Object.create(null);

    for (var i = 0; i < this.level.elements.length; i++) {
      var element = this.level.elements[i];
      if (element.kind === KIND.Emitter) {
        var emitter = { def: element, order: buildEmitOrder(element), cursor: 0 };
        this.emitters.push(emitter);
        this.emittersById[element.id] = emitter;
      } else if (element.kind === KIND.Receiver) {
        var receiver = { def: element, received: [], matched: [] };
        this.receivers.push(receiver);
        this.receiversById[element.id] = receiver;
      } else if (element.kind === KIND.DelayGate) {
        var gate = { def: element, queue: [] };
        this.delayGates.push(gate);
        this.delayGatesById[element.id] = gate;
      }
    }
  };

  Simulation.prototype.step = function () {
    var report = emptyReport(this.tickIndex + 1);
    if (this.finished) {
      report.isFinished = true;
      return report;
    }

    this.tickIndex++;
    this.delayReleasePhase(report);
    this.emitPhase(report);
    this.advancePhase(report);
    this.resolvePhase(report);

    if ((this.allEmittersDone() && this.bits.length === 0 && !this.hasQueuedBits()) ||
        this.tickIndex >= this.safetyTickLimit) {
      this.finished = true;
      report.isFinished = true;
    }
    return report;
  };

  /* ---------- 阶段 0：延迟器放行 ---------- */
  Simulation.prototype.delayReleasePhase = function (report) {
    if (this.delayGates.length === 0) return;
    this.rebuildOccupancy();

    for (var g = 0; g < this.delayGates.length; g++) {
      var gate = this.delayGates[g];
      if (gate.queue.length === 0) continue;

      var front = gate.queue[0];
      if (front.releaseTick > this.tickIndex) continue;

      if (this.occupancy[key(gate.def.x, gate.def.y)]) {
        /* 上一颗还没走出去（被前方堵住）：本 tick 继续压着，队列不前进 */
        report.delayStalled++;
        continue;
      }

      gate.queue.shift();

      /* 不能走 spawnBit：那个入口会把"元件格里的位"标成待裁决，
         结果刚放出来的位又被延迟器收回去。它是"离开"，不是"进入"。 */
      var bit = {
        id: this.nextBitId++,
        value: front.value,
        x: gate.def.x,
        y: gate.def.y,
        facing: front.exitDir,
        pendingResolve: false
      };
      this.bits.push(bit);
      this.occupancy[key(bit.x, bit.y)] = bit;

      var spawned = {
        bitId: bit.id, value: bit.value, x: bit.x, y: bit.y,
        dir: bit.facing, sourceId: gate.def.id
      };
      report.spawns.push(spawned);
      report.delayReleased.push(spawned);
    }
  };

  /* ---------- 阶段 1：发射 ---------- */
  Simulation.prototype.emitPhase = function (report) {
    this.rebuildOccupancy();
    var intents = [];

    for (var e = 0; e < this.emitters.length; e++) {
      var emitter = this.emitters[e];
      if (emitter.cursor >= emitter.order.length) continue;

      var value = emitter.order[emitter.cursor];
      var offset = D.offset(emitter.def.facing);
      var targetX = emitter.def.x + offset[0];
      var targetY = emitter.def.y + offset[1];

      if (!this.inBounds(targetX, targetY)) {
        /* 出口对着网格外：视为配置错误，本 tick 顺延 */
        report.stalledEmitters++;
        continue;
      }

      if (this.occupancy[key(targetX, targetY)]) {
        report.stalledEmitters++;
        continue;
      }

      var occupant = this.getElement(targetX, targetY);
      if (occupant && (occupant.kind === KIND.Wall || occupant.kind === KIND.Emitter)) {
        /* 正前方是墙 / 发射器机身：同样顺延 */
        report.stalledEmitters++;
        continue;
      }

      intents.push({ emitter: emitter, value: value, x: targetX, y: targetY });
    }

    if (intents.length === 0) return;

    var intentCount = Object.create(null);
    for (var i = 0; i < intents.length; i++) {
      var tk = key(intents[i].x, intents[i].y);
      intentCount[tk] = (intentCount[tk] || 0) + 1;
    }

    for (var j = 0; j < intents.length; j++) {
      var intent = intents[j];
      intent.emitter.cursor++;

      if (intentCount[key(intent.x, intent.y)] > 1) {
        /* 同格争抢：全部销毁（红闪），谁的位也没真正出现 */
        report.destroyed.push({
          bitId: 0, value: intent.value, x: intent.x, y: intent.y, reason: DESTROY.Conflict
        });
        continue;
      }

      var newBit = this.spawnBit(intent.value, intent.x, intent.y, intent.emitter.def.facing);
      report.spawns.push({
        bitId: newBit.id, value: newBit.value, x: newBit.x, y: newBit.y,
        dir: newBit.facing, sourceId: intent.emitter.def.id
      });
    }
  };


  /* ---------- 阶段 2：前进 ---------- */
  Simulation.prototype.advancePhase = function (report) {
    this.rebuildOccupancy();
    var snapshot = this.bits.slice();
    var candidates = [];

    /* 第一轮：先把"绝对进不去"的位处理掉（出界 / 墙 / 发射器机身 / 接收器错误进料面） */
    for (var i = 0; i < snapshot.length; i++) {
      var bit = snapshot[i];

      /* 刚被吐进元件格、还没裁决的位：本 tick 停在原地，等第 3 阶段由元件改向 */
      if (bit.pendingResolve) {
        report.moves.push({
          bitId: bit.id, value: bit.value,
          fromX: bit.x, fromY: bit.y, toX: bit.x, toY: bit.y, moved: false
        });
        continue;
      }

      var offset = D.offset(bit.facing);
      var targetX = bit.x + offset[0];
      var targetY = bit.y + offset[1];

      if (!this.inBounds(targetX, targetY)) {
        this.destroyBit(bit, DESTROY.OutOfBounds, report);
        continue;
      }

      var element = this.getElement(targetX, targetY);
      if (element && element.kind === KIND.Wall) {
        this.destroyBit(bit, DESTROY.Wall, report);
        continue;
      }
      if (element && element.kind === KIND.Emitter) {
        /* 发射器机身不可穿过 */
        this.destroyBit(bit, DESTROY.EmitterBody, report);
        continue;
      }
      if (element && element.kind === KIND.Receiver && !acceptsEntry(element, D.opposite(bit.facing))) {
        /* 严格模式的接收器只有进料面能进，其余面撞上即销毁（默认四面都可收） */
        this.destroyBit(bit, DESTROY.ReceiverWrongFace, report);
        continue;
      }

      candidates.push({ bit: bit, x: targetX, y: targetY });
    }

    if (candidates.length === 0) return;

    /* 第二轮：链式阻塞。前方格里的位如果本 tick 也走不掉，自己就被顶住；
       反之（前方位同 tick 会离开）允许"跟着前进"。 */
    var blocked = this.findBlocked(candidates);
    var intents = [];
    for (var c = 0; c < candidates.length; c++) {
      var move = candidates[c];
      if (blocked[move.bit.id]) {
        report.moves.push({
          bitId: move.bit.id, value: move.bit.value,
          fromX: move.bit.x, fromY: move.bit.y,
          toX: move.bit.x, toY: move.bit.y, moved: false
        });
        continue;
      }
      intents.push(move);
    }

    if (intents.length === 0) return;

    var conflicted = this.findConflicts(intents);

    for (var n = 0; n < intents.length; n++) {
      var pending = intents[n];
      if (conflicted[pending.bit.id]) {
        this.destroyBit(pending.bit, DESTROY.Conflict, report);
        continue;
      }

      var fromX = pending.bit.x;
      var fromY = pending.bit.y;
      pending.bit.x = pending.x;
      pending.bit.y = pending.y;
      report.moves.push({
        bitId: pending.bit.id, value: pending.bit.value,
        fromX: fromX, fromY: fromY, toX: pending.x, toY: pending.y, moved: true
      });

      var arrived = this.getElement(pending.x, pending.y);
      if (arrived && arrived.kind === KIND.Receiver) {
        this.deliver(arrived, pending.bit, report);
      } else if (arrived) {
        pending.bit.pendingResolve = true;
      }
    }
  };

  /* 迭代到收敛地求"被顶住"的位集合：只有当前方格里的位本 tick 也动不了，自己才动不了。 */
  Simulation.prototype.findBlocked = function (candidates) {
    var blocked = Object.create(null);
    var changed = true;

    while (changed) {
      changed = false;
      for (var i = 0; i < candidates.length; i++) {
        var move = candidates[i];
        if (blocked[move.bit.id]) continue;

        var other = this.occupancy[key(move.x, move.y)];
        if (!other || other === move.bit) continue;

        if (blocked[other.id]) {
          blocked[move.bit.id] = true;
          changed = true;
        }
      }
    }

    return blocked;
  };

  /* 找出"抢同一格"与"两两对穿"的位（都会被销毁）。 */
  Simulation.prototype.findConflicts = function (intents) {
    var conflicted = Object.create(null);
    var byTarget = Object.create(null);
    var bySource = Object.create(null);

    for (var i = 0; i < intents.length; i++) {
      var move = intents[i];
      var tk = key(move.x, move.y);
      if (!byTarget[tk]) byTarget[tk] = [];
      byTarget[tk].push(move);
      bySource[key(move.bit.x, move.bit.y)] = move;
    }

    for (var t in byTarget) {
      var list = byTarget[t];
      if (list.length > 1) {
        for (var l = 0; l < list.length; l++) conflicted[list[l].bit.id] = true;
      }
    }

    /* 对穿（A 去 B 的格、B 去 A 的格）：两颗一起销毁 */
    for (var m = 0; m < intents.length; m++) {
      var current = intents[m];
      var oppositeMove = bySource[key(current.x, current.y)];
      if (oppositeMove && oppositeMove.x === current.bit.x && oppositeMove.y === current.bit.y &&
          oppositeMove.bit.id !== current.bit.id) {
        conflicted[current.bit.id] = true;
        conflicted[oppositeMove.bit.id] = true;
      }
    }

    return conflicted;
  };


  /* ---------- 阶段 3：元件裁决 ---------- */
  Simulation.prototype.resolvePhase = function (report) {
    var snapshot = this.bits.slice();

    for (var i = 0; i < snapshot.length; i++) {
      var bit = snapshot[i];
      if (!bit.pendingResolve) continue;
      bit.pendingResolve = false;

      var element = this.getElement(bit.x, bit.y);
      if (!element) continue;

      /* 位"从哪一面进来的" = 当前朝向的反面 */
      var entryFace = D.opposite(bit.facing);
      var exits = exitDirsOf(element);

      switch (element.kind) {
        case KIND.Splitter:
          if (exits.indexOf(entryFace) >= 0) {
            this.destroyBit(bit, DESTROY.SplitterBackflow, report);
            break;
          }
          bit.facing = bit.value === 1 ? element.oneArm : element.zeroArm;
          break;

        case KIND.NotGate:
          if (exits.indexOf(entryFace) >= 0) {
            this.destroyBit(bit, DESTROY.NotGateBackflow, report);
            break;
          }
          bit.value = 1 - bit.value;
          bit.facing = element.outputDir;
          break;

        case KIND.Filter:
          if (exits.indexOf(entryFace) >= 0) {
            this.destroyBit(bit, DESTROY.FilterBackflow, report);
            break;
          }
          if (bit.value !== element.passValue) {
            this.destroyBit(bit, DESTROY.FilterRejected, report);
            break;
          }
          bit.facing = element.outputDir;
          break;

        case KIND.DelayGate:
          /* 进队 = 从场上撤下（不占格）。出口朝向沿用进入时的前进方向。 */
          var gateState = this.delayGatesById[element.id];
          if (!gateState) break;

          if (gateState.queue.length >= DELAY_CAPACITY) {
            report.delayDropped++;
            this.destroyBit(bit, DESTROY.DelayOverflow, report);
            break;
          }

          /* 第 T tick 吸入 → 第 T+1+N 个 tick 放行（"比直通多停 N 拍"） */
          var releaseTick = this.tickIndex + element.delayTicks + 1;
          gateState.queue.push({ value: bit.value, exitDir: bit.facing, releaseTick: releaseTick });

          this.removeBit(bit);
          report.delayAbsorbed.push({
            bitId: bit.id, value: bit.value, gateId: element.id,
            x: bit.x, y: bit.y, releaseTick: releaseTick
          });
          break;
      }
    }
  };

  /* ---------- 内部工具 ---------- */
  Simulation.prototype.spawnBit = function (value, x, y, facing) {
    var aligned = this.getElement(x, y) !== null;
    var bit = {
      id: this.nextBitId++,
      value: value,
      x: x,
      y: y,
      facing: facing,
      pendingResolve: aligned
    };
    this.bits.push(bit);
    this.occupancy[key(x, y)] = bit;
    return bit;
  };

  Simulation.prototype.removeBit = function (bit) {
    var index = this.bits.indexOf(bit);
    if (index >= 0) this.bits.splice(index, 1);
    var slot = key(bit.x, bit.y);
    if (this.occupancy[slot] === bit) delete this.occupancy[slot];
  };

  Simulation.prototype.destroyBit = function (bit, reason, report) {
    this.removeBit(bit);
    report.destroyed.push({ bitId: bit.id, value: bit.value, x: bit.x, y: bit.y, reason: reason });
  };

  Simulation.prototype.deliver = function (receiverDef, bit, report) {
    var state = this.receiversById[receiverDef.id];
    if (!state) return;

    var receivedIndex = state.received.length;
    var overflow = receivedIndex >= receiverDef.targetCount;
    var matched = !overflow && bit.value === expectedValueAt(receiverDef, receivedIndex);

    state.received.push(bit.value);
    state.matched.push(matched);

    this.removeBit(bit);

    report.deliveries.push({
      receiverId: receiverDef.id, value: bit.value, matched: matched,
      index: receivedIndex, overflow: overflow
    });
  };

  Simulation.prototype.rebuildOccupancy = function () {
    this.occupancy = Object.create(null);
    for (var i = 0; i < this.bits.length; i++) {
      var bit = this.bits[i];
      this.occupancy[key(bit.x, bit.y)] = bit;
    }
  };

  Simulation.prototype.getElement = function (x, y) {
    var element = this.lookup[key(x, y)];
    return element === undefined ? null : element;
  };

  Simulation.prototype.inBounds = function (x, y) {
    return x >= 0 && x < this.level.width && y >= 0 && y < this.level.height;
  };

  Simulation.prototype.allEmittersDone = function () {
    for (var i = 0; i < this.emitters.length; i++) {
      var emitter = this.emitters[i];
      if (emitter.cursor < emitter.order.length) return false;
    }
    return true;
  };

  Simulation.prototype.hasQueuedBits = function () {
    for (var i = 0; i < this.delayGates.length; i++) {
      if (this.delayGates[i].queue.length > 0) return true;
    }
    return false;
  };

  /* ---------- 给 View / UI 的只读查询 ---------- */
  Simulation.prototype.getBits = function () {
    var snapshots = [];
    for (var i = 0; i < this.bits.length; i++) {
      var bit = this.bits[i];
      snapshots.push({ bitId: bit.id, value: bit.value, x: bit.x, y: bit.y, facing: bit.facing });
    }
    return snapshots;
  };

  Simulation.prototype.getDelayGateProgress = function () {
    var progress = [];
    for (var i = 0; i < this.delayGates.length; i++) {
      var gate = this.delayGates[i];
      progress.push({
        gateId: gate.def.id,
        queuedCount: gate.queue.length,
        capacity: DELAY_CAPACITY,
        nextReleaseTick: gate.queue.length > 0 ? gate.queue[0].releaseTick : -1
      });
    }
    return progress;
  };

  Simulation.prototype.getEmitterProgress = function () {
    var progress = [];
    for (var i = 0; i < this.emitters.length; i++) {
      var emitter = this.emitters[i];
      progress.push({
        emitterId: emitter.def.id,
        emittedCount: emitter.cursor,
        totalCount: emitter.order.length
      });
    }
    return progress;
  };

  Simulation.prototype.getReceiverProgress = function () {
    var progress = [];
    for (var i = 0; i < this.receivers.length; i++) {
      var state = this.receivers[i];
      var stillValid = true;
      for (var m = 0; m < state.matched.length; m++) {
        if (!state.matched[m]) { stillValid = false; break; }
      }
      progress.push({
        receiverId: state.def.id,
        targetCount: state.def.targetCount,
        receivedCount: state.received.length,
        isStillValid: stillValid
      });
    }
    return progress;
  };

  Simulation.prototype.buildResult = function () {
    var result = {
      displayName: this.level.displayName,
      tickCount: this.tickIndex,
      isSuccess: false,
      receivers: []
    };

    var allSuccess = this.receivers.length > 0;
    for (var i = 0; i < this.receivers.length; i++) {
      var state = this.receivers[i];
      var receiverResult = {
        receiverId: state.def.id,
        targetCount: state.def.targetCount,
        receivedCount: state.received.length,
        received: state.received.slice(),
        matched: state.matched.slice()
      };

      var ok = receiverResult.receivedCount === receiverResult.targetCount;
      if (ok) {
        for (var m = 0; m < receiverResult.matched.length; m++) {
          if (!receiverResult.matched[m]) { ok = false; break; }
        }
      }
      receiverResult.isSuccess = ok;

      result.receivers.push(receiverResult);
      allSuccess = allSuccess && ok;
    }

    result.isSuccess = allSuccess;
    return result;
  };

  /* LevelResult.Summary() 的同一句话，UI 直接显示。 */
  function summarizeResult(result) {
    if (result.isSuccess) return 'ALL RECEIVERS LOCKED IN ' + result.tickCount + ' TICKS';
    var text = 'MISMATCH';
    for (var i = 0; i < result.receivers.length; i++) {
      var receiver = result.receivers[i];
      text += '  |  R' + receiver.receiverId + ' ' + receiver.receivedCount + '/' + receiver.targetCount +
        (receiver.isSuccess ? ' ok' : ' bad');
    }
    return text;
  }

  var api = {
    Simulation: Simulation,
    DELAY_CAPACITY: DELAY_CAPACITY,
    DEFAULT_SAFETY_TICKS: DEFAULT_SAFETY_TICKS,
    DESTROY: DESTROY,
    buildEmitOrder: buildEmitOrder,
    parseSequence: parseSequence,
    expectedValueAt: expectedValueAt,
    acceptsEntry: acceptsEntry,
    summarizeResult: summarizeResult
  };

  root.BitFlowSimulation = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
