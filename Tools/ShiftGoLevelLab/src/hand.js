/* 手牌试玩（Solve）的规则内核 —— LevelDraftInventory.cs 的浏览器版。
 *
 * 为什么单独一个文件、不写进 ui.js：
 *  - 这些规则（锁定格不能搬、BUFFER 收预设件、按 type 记账、延迟器不能转）
 *    是游戏侧已经在跑的语义，必须能脱离 DOM 跑单测（tests/hand.js）；
 *  - ui.js 只负责「点哪一格 → 调哪个方法 → 把人话写到屏幕上」，
 *    规则与像素分开，改措辞不会碰规则。
 *
 * 与引擎的分工：本文件只做「准入检查 + 改文档」，真正判定仍然由
 * document.js 的 read() 和 check.js 的 run() 完成 —— 所以手牌再多也不会
 * 冒出第二套「能不能通关」的答案。
 */
(function (root) {
  'use strict';

  var Doc = root.BitFlowDocument;
  var D = root.BitFlowDir;
  var Catalog = root.BitFlowCatalog;
  if (!Doc && typeof require === 'function') Doc = require('./engine/document.js');
  if (!D && typeof require === 'function') D = require('./engine/dir.js');
  if (!Catalog && typeof require === 'function') Catalog = require('./engine/catalog.js');

  var KIND = Doc.KIND;

  /* BUFFER 能收回来的件。发射器 / 接收器是关卡骨架，游戏里也不允许被收走。 */
  var RETRACTABLE = [KIND.Wall, KIND.Splitter, KIND.NotGate, KIND.Filter, KIND.DelayGate];

  function key(x, y) { return x + ',' + y; }

  function clone(value) { return JSON.parse(JSON.stringify(value)); }

  function str(value) { return typeof value === 'string' ? value : ''; }

  function cellKeyOf(pile) {
    return pile.type !== '' ? 'type:' + pile.type : 'kind:' + Doc.kindName(pile.kind);
  }

  /* kind 优先信 type 快照：同一摞牌在游戏里是按 type 读的。 */
  function supplyKind(supply, catalog) {
    var typed = catalog && catalog.find ? catalog.find(str(supply.type)) : null;
    if (typed) {
      var fromType = Doc.parseKind(typed.kind);
      if (fromType !== null) return fromType;
    }
    return Doc.parseKind(supply.kind);
  }

  function wallRows(document_) {
    if (!Array.isArray(document_.wallRows)) document_.wallRows = [];
    return document_.wallRows;
  }

  function wallAt(document_, x, y) {
    return (wallRows(document_)[y] || '').charAt(x) === '#';
  }

  function setWall(document_, x, y, on) {
    var rows = wallRows(document_);
    while (rows.length <= y) rows.push('');
    var row = rows[y] || '';
    while (row.length <= x) row += '.';
    rows[y] = row.substring(0, x) + (on ? '#' : '.') + row.substring(x + 1);
  }

  function elementIndex(document_, x, y) {
    var list = Array.isArray(document_.elements) ? document_.elements : [];
    for (var i = 0; i < list.length; i++) {
      if ((list[i].x | 0) === x && (list[i].y | 0) === y) return i;
    }
    return -1;
  }

  /* 一摞牌：type 优先；只有 kind 时按 kind 记账。count -1 = 1 张、0 = 跳过。 */
  function buildPiles(document_, catalog, warnings) {
    var piles = [];
    var byKey = Object.create(null);
    var supplies = Array.isArray(document_.supplies) ? document_.supplies : [];

    for (var i = 0; i < supplies.length; i++) {
      var supply = supplies[i];
      if (!supply || typeof supply !== 'object') continue;

      var type = str(supply.type);
      var kind = supplyKind(supply, catalog);
      if (type === '' && kind === null) {
        warnings.push('手牌第 ' + (i + 1) + ' 摞既没有 type 也认不出 kind，已跳过（游戏里也会跳过）');
        continue;
      }

      var count = typeof supply.count === 'number' ? Math.trunc(supply.count) : -1;
      if (count === 0) {
        warnings.push('手牌第 ' + (i + 1) + ' 摞 count = 0，游戏会跳过并记一条问题');
        continue;
      }
      if (count < 0) count = 1; /* 与 LevelSupply 的 -1 语义一致 */

      var spec = type !== '' && catalog && catalog.find ? catalog.find(type) : null;
      var pile = {
        type: type,
        kind: kind === null ? KIND.Wall : kind,
        label: spec ? str(spec.label) : '',
        tool: !!(spec && spec.tool),
        total: count,
        left: count
      };
      pile.key = cellKeyOf(pile);

      var existing = byKey[pile.key];
      if (existing) {
        existing.total += pile.total;
        existing.left += pile.left;
      } else {
        byKey[pile.key] = pile;
        piles.push(pile);
        if (spec && spec.kind && Doc.parseKind(spec.kind) !== pile.kind) {
          warnings.push('手牌 ' + type + ' 的 kind 与类型快照不一致，按 type 生效');
        }
      }
    }

    return piles;
  }

  /* 从手牌摆出来的一格：参数直接取类型快照（没有快照就取 kind 默认值），
   * 这样「试玩时放的 L1」与「作者手画的 L1」在引擎里读出来是同一个件。 */
  function cellFromPile(pile, catalog, x, y) {
    var defaults = Doc.kindDefaults(pile.kind);
    var spec = pile.type !== '' && catalog && catalog.find ? catalog.find(pile.type) : null;
    var source = spec || defaults;

    function dirOf(value, fallback) {
      var parsed = D.parse(value);
      return D.name(parsed === null ? fallback : parsed);
    }

    return {
      type: pile.type,
      kind: Doc.kindName(pile.kind),
      x: x,
      y: y,
      rot: 0,
      dir: dirOf(source.dir, defaults.dir),
      dir2: dirOf(source.dir2, defaults.dir2),
      sequence: str(source.sequence) || defaults.sequence,
      count: typeof source.count === 'number' ? Math.trunc(source.count) : defaults.count,
      face: pile.kind === KIND.Receiver ? (source.acceptAnyFace === false ? 'in' : 'any') : '',
      pass: pile.kind === KIND.Filter
        ? (typeof source.pass === 'number' ? Math.trunc(source.pass) : defaults.pass) : -1,
      delay: pile.kind === KIND.DelayGate
        ? (typeof source.delay === 'number' ? Math.trunc(source.delay) : defaults.delay) : -1,
      playerEditable: true
    };
  }

  function Hand(document_, catalog) {
    this.document = document_;
    this.catalog = catalog || Catalog.builtin;
    this.original = clone(document_);
    this.warnings = [];
    this.piles = buildPiles(document_, this.catalog, this.warnings);
    this.locked = Object.create(null);
    this.origin = Object.create(null); /* 'x,y' → 牌摞 key（玩家亲手放的） */

    /* 锁定 = 进场那一刻的快照。用实时 playerEditable 会让「试玩时放的件」
     * 在采用解之后反过来锁住自己。 */
    var cells = Array.isArray(document_.elements) ? document_.elements : [];
    for (var i = 0; i < cells.length; i++) {
      if (!cells[i].playerEditable) this.locked[key(cells[i].x | 0, cells[i].y | 0)] = true;
    }
  }

  Hand.prototype.pile = function (pileKey) {
    for (var i = 0; i < this.piles.length; i++) {
      if (this.piles[i].key === pileKey) return this.piles[i];
    }
    return null;
  };

  Hand.prototype.inBounds = function (x, y) {
    return x >= 0 && y >= 0 && x < this.document.width && y < this.document.height;
  };

  Hand.prototype.busy = function (x, y) {
    return elementIndex(this.document, x, y) >= 0 || wallAt(this.document, x, y);
  };

  Hand.prototype.isPlaced = function (x, y) {
    return !!this.origin[key(x, y)];
  };

  Hand.prototype.canPlace = function (pileKey, x, y) {
    var pile = this.pile(pileKey);
    if (!pile) return { ok: false, reason: 'NoPile' };
    if (pile.tool) return { ok: false, reason: 'NotPlaceable' };
    if (!this.inBounds(x, y)) return { ok: false, reason: 'OutOfBounds' };
    if (pile.left <= 0) return { ok: false, reason: 'OutOfStock' };
    if (this.locked[key(x, y)]) return { ok: false, reason: 'Locked' };
    if (this.busy(x, y)) return { ok: false, reason: 'Occupied' };
    return { ok: true, reason: '' };
  };

  Hand.prototype.place = function (pileKey, x, y) {
    var check = this.canPlace(pileKey, x, y);
    if (!check.ok) return check;

    var pile = this.pile(pileKey);
    if (pile.kind === KIND.Wall) setWall(this.document, x, y, true);
    else this.document.elements.push(cellFromPile(pile, this.catalog, x, y));

    pile.left--;
    this.origin[key(x, y)] = pile.key;
    return { ok: true, reason: '', pile: pile };
  };

  Hand.prototype.canPick = function (x, y) {
    if (!this.inBounds(x, y)) return { ok: false, reason: 'OutOfBounds' };
    if (this.locked[key(x, y)]) return { ok: false, reason: 'Locked' };
    if (this.isPlaced(x, y)) return { ok: true, reason: '' };
    if (this.busy(x, y)) return { ok: false, reason: 'NotPlayerPiece' };
    return { ok: false, reason: 'EmptyCell' };
  };

  /* 拾回：返还到「放它的那一摞」，而不是按 kind 猜 —— 两摞同为分流器时不能串味。 */
  Hand.prototype.pick = function (x, y) {
    var check = this.canPick(x, y);
    if (!check.ok) return check;

    var pile = this.pile(this.origin[key(x, y)]);

    var index = elementIndex(this.document, x, y);
    if (index >= 0) this.document.elements.splice(index, 1);
    else setWall(this.document, x, y, false);

    delete this.origin[key(x, y)];
    if (pile) pile.left++;
    return { ok: true, reason: '', pile: pile };
  };

  /* BUFFER：把一枚「锁定件」收回手牌（工具卡本身不进棋盘）。 */
  Hand.prototype.canRetract = function (x, y, pileKey) {
    var pile = this.pile(pileKey);
    if (!pile || !pile.tool) return { ok: false, reason: 'NotPile' };
    if (!this.inBounds(x, y)) return { ok: false, reason: 'OutOfBounds' };
    if (pile.left <= 0) return { ok: false, reason: 'OutOfStock' };
    if (!this.locked[key(x, y)]) return { ok: false, reason: 'NotLocked' };

    var index = elementIndex(this.document, x, y);
    if (index < 0) return { ok: false, reason: 'NoElement' };

    var kind = Doc.parseKind(this.document.elements[index].kind);
    if (kind === null || RETRACTABLE.indexOf(kind) < 0) return { ok: false, reason: 'NotBufferable' };
    return { ok: true, reason: '' };
  };

  /* 收回的件进「同 type / 同 kind」的那一摞；没有就先建一摞（游戏里也是 +1 张）。 */
  Hand.prototype.grant = function (cell) {
    var kind = Doc.parseKind(cell.kind);
    var pile = {
      type: str(cell.type),
      kind: kind === null ? KIND.Wall : kind,
      label: '', tool: false, total: 0, left: 0
    };
    var spec = pile.type !== '' && this.catalog.find ? this.catalog.find(pile.type) : null;
    if (spec) pile.label = str(spec.label);
    pile.key = cellKeyOf(pile);

    var existing = this.pile(pile.key);
    if (existing) {
      existing.left++;
      return existing;
    }
    pile.left = 1;
    this.piles.push(pile);
    return pile;
  };

  Hand.prototype.retract = function (x, y, pileKey) {
    var check = this.canRetract(x, y, pileKey);
    if (!check.ok) return check;

    var pile = this.pile(pileKey);
    var index = elementIndex(this.document, x, y);
    var cell = this.document.elements[index];
    this.document.elements.splice(index, 1);
    delete this.locked[key(x, y)];

    var target = this.grant(cell);
    pile.left--;
    return { ok: true, reason: '', pile: target };
  };

  /* 旋转：只转玩家自己放的；延迟器 / 墙没有朝向。 */
  Hand.prototype.rotate = function (x, y) {
    if (!this.isPlaced(x, y)) return false;
    var index = elementIndex(this.document, x, y);
    if (index < 0) return false;

    var cell = this.document.elements[index];
    var kind = Doc.parseKind(cell.kind);
    if (kind === KIND.DelayGate || kind === KIND.Wall) return false;

    if (str(cell.type) !== '') {
      cell.rot = ((cell.rot | 0) + 1) % 4;
      return true;
    }

    var dir = D.parse(cell.dir);
    cell.dir = D.name(D.clockwise(dir === null ? D.RIGHT : dir));
    if (kind === KIND.Splitter) {
      var dir2 = D.parse(cell.dir2);
      cell.dir2 = D.name(D.clockwise(dir2 === null ? D.DOWN : dir2));
    }
    return true;
  };

  Hand.prototype.remaining = function () {
    var left = 0;
    for (var i = 0; i < this.piles.length; i++) left += Math.max(0, this.piles[i].left);
    return left;
  };

  Hand.prototype.status = function () {
    var doc = this.document;
    var walls = 0;
    var rows = Array.isArray(doc.wallRows) ? doc.wallRows : [];
    for (var i = 0; i < rows.length; i++) {
      for (var c = 0; c < (rows[i] || '').length; c++) if (rows[i].charAt(c) === '#') walls++;
    }

    var total = 0;
    for (var p = 0; p < this.piles.length; p++) total += this.piles[p].total;

    return {
      left: this.remaining(),
      total: total,
      placed: Object.keys(this.origin).length,
      empty: Math.max(0, doc.width * doc.height - doc.elements.length - walls),
      warnings: this.warnings.length
    };
  };

  /* 撤销用：牌库余量 + 锁定表 + 哪一格是哪摞放的一起存。
   * 少了 piles / origin，撤销后数量就会与盘面脱节（最容易被忽略的一处）。 */
  Hand.prototype.serialize = function () {
    var piles = [];
    for (var i = 0; i < this.piles.length; i++) {
      var pile = this.piles[i];
      piles.push({
        key: pile.key, type: pile.type, kind: pile.kind, label: pile.label,
        tool: pile.tool, total: pile.total, left: pile.left
      });
    }
    return {
      document: clone(this.document),
      original: clone(this.original),
      piles: piles,
      locked: clone(this.locked),
      origin: clone(this.origin),
      warnings: this.warnings.slice()
    };
  };

  /* 还原时要深拷贝：快照存在撤销栈里，若共享对象，下一次 place/pick 会就地改掉
   * 栈里那份，撤销回去看到的就是"改过之后"的旧步。 */
  Hand.restore = function (snapshot, catalog) {
    var hand = new Hand(clone(snapshot.document), catalog);
    hand.original = snapshot.original ? clone(snapshot.original) : hand.original;
    hand.piles = snapshot.piles ? clone(snapshot.piles) : hand.piles;
    hand.locked = snapshot.locked ? clone(snapshot.locked) : Object.create(null);
    hand.origin = snapshot.origin ? clone(snapshot.origin) : Object.create(null);
    if (snapshot.warnings) hand.warnings = snapshot.warnings.slice();
    return hand;
  };

  /* 采用这套解：手牌留下的件全部转成预设件，手牌原样保留（作者再调）。 */
  Hand.prototype.adopt = function () {
    var output = clone(this.document);
    var cells = Array.isArray(output.elements) ? output.elements : [];
    for (var i = 0; i < cells.length; i++) cells[i].playerEditable = false;
    return output;
  };

  Hand.prototype.preset = function () { return clone(this.original); };

  var api = {
    Hand: Hand,
    RETRACTABLE: RETRACTABLE,
    create: function (document_, catalog) { return new Hand(document_, catalog); },
    restore: Hand.restore,
    cellFromPile: cellFromPile
  };

  root.BitFlowHand = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
