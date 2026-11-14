/* BitFlow 方向盘 —— Dir.cs / DirUtil 的零依赖移植。
 *
 * 顺序固定为 上 → 右 → 下 → 左，旋转就是 +1 mod 4，与 C# 端完全一致。
 * 这个文件既能被浏览器当普通脚本加载（挂到 window.BitFlowDir），
 * 也能被 Node require 进去跑对拍（module.exports）。
 */
(function (root) {
  'use strict';

  var UP = 0;
  var RIGHT = 1;
  var DOWN = 2;
  var LEFT = 3;

  var NAMES = ['Up', 'Right', 'Down', 'Left'];
  var MARKS = ['↑', '→', '↓', '←'];
  var CHINESE = ['上', '右', '下', '左'];

  /* 网格步进：y 向上（与游戏一致）。 */
  var OFFSETS = [[0, 1], [1, 0], [0, -1], [-1, 0]];

  var CHINESE_NAMES = { '上': UP, '右': RIGHT, '下': DOWN, '左': LEFT };
  var MARK_NAMES = { '↑': UP, '→': RIGHT, '↓': DOWN, '←': LEFT };

  /* Up/Right/Down/Left（不分大小写），也认 ↑→↓← 与 上右下左。
   * 解析失败返回 null（对应 C# 的 TryParseDir 返回 false）。 */
  function parse(text) {
    if (typeof text !== 'string') return null;
    var trimmed = text.trim();
    if (trimmed === '') return null;
    if (MARK_NAMES[trimmed] !== undefined) return MARK_NAMES[trimmed];
    if (CHINESE_NAMES[trimmed] !== undefined) return CHINESE_NAMES[trimmed];

    var lower = trimmed.toLowerCase();
    for (var i = 0; i < NAMES.length; i++) {
      if (NAMES[i].toLowerCase() === lower) return i;
    }
    /* Enum.TryParse 也接受数字串；IsDefined 保证只认 0..3 */
    if (/^\d+$/.test(trimmed)) {
      var numeric = parseInt(trimmed, 10);
      return numeric >= 0 && numeric <= 3 ? numeric : null;
    }
    return null;
  }

  function name(dir) { return NAMES[dir] || 'Right'; }

  function mark(dir) { return MARKS[dir] || '→'; }

  function chineseName(dir) { return CHINESE[dir] || '右'; }

  function opposite(dir) { return (dir + 2) & 3; }

  function clockwise(dir) { return (dir + 1) & 3; }

  function counterClockwise(dir) { return (dir + 3) & 3; }

  function offset(dir) { return OFFSETS[dir] || OFFSETS[RIGHT]; }

  function isVertical(dir) { return dir === UP || dir === DOWN; }

  /* 把基准朝向按旋转档（顺时针 90°/档）换算 —— LevelDocumentBuilder.Rotate */
  function rotate(baseDir, rotationSteps) {
    var steps = ((rotationSteps % 4) + 4) % 4;
    var result = baseDir;
    for (var i = 0; i < steps; i++) result = clockwise(result);
    return result;
  }

  var api = {
    UP: UP, RIGHT: RIGHT, DOWN: DOWN, LEFT: LEFT,
    NAMES: NAMES,
    parse: parse,
    name: name,
    mark: mark,
    chineseName: chineseName,
    opposite: opposite,
    clockwise: clockwise,
    counterClockwise: counterClockwise,
    offset: offset,
    isVertical: isVertical,
    rotate: rotate
  };

  root.BitFlowDir = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
