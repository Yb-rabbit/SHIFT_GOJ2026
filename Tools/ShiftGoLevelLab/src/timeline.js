/* 时间轴：把一关跑完并把每个 tick 的快照存下来，支持前进 / 后退 / 播放 / 拖动。
 *
 * 为什么先整关跑完再回放（而不是边走边算）：作者要能反复对比"第 7 拍和第 8 拍"，
 * 后退必须是真的后退。一关几千 tick、每帧只有几十个位，全部存下来也就几 MB。
 */
(function (root) {
  'use strict';

  var Sim = root.BitFlowSimulation;
  var Check = root.BitFlowCheck;
  if (!Sim && typeof require === 'function') Sim = require('./engine/simulation.js');
  if (!Check && typeof require === 'function') Check = require('./engine/check.js');

  var DEFAULT_MAX_FRAMES = 1200;

  function snapshot(simulation, report) {
    return {
      tick: simulation.tickIndex,
      bits: simulation.getBits(),
      report: report,
      emitters: simulation.getEmitterProgress(),
      receivers: simulation.getReceiverProgress(),
      delays: simulation.getDelayGateProgress(),
      finished: simulation.finished
    };
  }

  /* 跑完整关，返回帧序列 + 结算。maxFrames 只影响回放长度，不影响自检结论。 */
  function build(definition, options) {
    var settings = options || {};
    var maxFrames = settings.maxFrames || DEFAULT_MAX_FRAMES;
    var simulation = new Sim.Simulation(definition);
    var safety = Math.max(Check.safetyTicks(definition), simulation.safetyTickLimit);

    var frames = [snapshot(simulation, null)];
    var steps = 0;

    while (!simulation.finished && steps < safety && frames.length < maxFrames) {
      var report = simulation.step();
      steps++;
      frames.push(snapshot(simulation, report));
    }

    return {
      frames: frames,
      result: simulation.buildResult(),
      finished: simulation.finished,
      truncated: !simulation.finished,
      totalTicks: simulation.tickIndex,
      safety: safety
    };
  }

  /* 播放器：只管"第几帧"和播放态，UI 通过 onFrame 重画。 */
  function Player(timeline, onFrame) {
    this.timeline = timeline;
    this.onFrame = onFrame || function () { };
    this.index = 0;
    this.speed = 1;
    this.playing = false;
    this.timer = null;
    this.tickInterval = 0.35;
  }

  Player.prototype.setTickInterval = function (seconds) {
    this.tickInterval = Math.max(0.05, Number(seconds) || 0.35);
  };

  Player.prototype.frame = function () {
    return this.timeline.frames[Math.min(this.index, this.timeline.frames.length - 1)];
  };

  Player.prototype.emit = function () {
    this.onFrame(this.frame(), this.index, this.timeline.frames.length);
  };

  Player.prototype.seek = function (index) {
    var max = this.timeline.frames.length - 1;
    this.index = Math.max(0, Math.min(max, index | 0));
    this.emit();
  };

  Player.prototype.stepBy = function (delta) {
    this.pause();
    this.seek(this.index + delta);
  };

  Player.prototype.restart = function () {
    this.pause();
    this.seek(0);
  };

  Player.prototype.play = function () {
    if (this.playing) return;
    if (this.index >= this.timeline.frames.length - 1) this.index = 0;

    var self = this;
    this.playing = true;
    var delay = Math.max(24, Math.round((this.tickInterval * 1000) / this.speed));

    this.timer = setInterval(function () {
      if (self.index >= self.timeline.frames.length - 1) {
        self.pause();
        self.emit();
        return;
      }
      self.index++;
      self.emit();
    }, delay);

    this.emit();
  };

  Player.prototype.pause = function () {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
    this.playing = false;
  };

  Player.prototype.toggle = function () {
    if (this.playing) this.pause();
    else this.play();
    this.emit();
  };

  Player.prototype.setSpeed = function (speed) {
    this.speed = Math.max(0.25, Math.min(8, Number(speed) || 1));
    if (this.playing) {
      this.pause();
      this.play();
    }
  };

  var api = { build: build, Player: Player, DEFAULT_MAX_FRAMES: DEFAULT_MAX_FRAMES };
  root.BitFlowTimeline = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
