/* 编辑器主逻辑。
 *
 * 数据模型刻意贴住关卡 JSON 本身（wallRows 是墙层，elements 是元件格），
 * 这样导入 → 编辑 → 导出不会因为"内部模型更漂亮"而丢掉字段。
 */
(function (root) {
  'use strict';

  var Doc = root.BitFlowDocument;
  var D = root.BitFlowDir;
  var Check = root.BitFlowCheck;
  var Catalog = root.BitFlowCatalog;
  var Samples = root.BitFlowSamples;
  var Docs = root.BitFlowDocs;
  var Timeline = root.BitFlowTimeline;
  var GridLib = root.BitFlowGrid;
  var SelfTest = root.BitFlowSelfTest;
  var HandLib = root.BitFlowHand;

  var KIND = Doc.KIND;
  var STORAGE_KEY = 'shiftgo.levelLab.v1';
  var BUILD_STAMP = '__BUILD_STAMP__';

  var PALETTE = [
    { kind: KIND.Wall, label: '墙', en: 'Wall', short: '#', key: '1', color: '#5b6673' },
    { kind: KIND.Emitter, label: '发射器', en: 'Emitter', short: 'E', key: '2', color: '#b48ead' },
    { kind: KIND.Receiver, label: '接收器', en: 'Receiver', short: 'R', key: '3', color: '#5aa9e6' },
    { kind: KIND.Splitter, label: '分流器', en: 'Splitter', short: 'L', key: '4', color: '#e0a458' },
    { kind: KIND.NotGate, label: '非门', en: 'NotGate', short: 'N', key: '5', color: '#7ee787' },
    { kind: KIND.Filter, label: '过滤器', en: 'Filter', short: 'F', key: '6', color: '#d98cb3' },
    { kind: KIND.DelayGate, label: '延迟器', en: 'DelayGate', short: 'D', key: '7', color: '#8fbbbc' }
  ];

  /* 选择工具不是画笔：它只改 selection，不碰文档。 */
  var TOOL_SELECT = { tool: 'select', label: '选择', en: 'Select', short: '▣', key: 'V', color: '#c9d1d9' };

  var state = {
    document: null,
    brush: null,
    tool: 'paint',
    selection: null,
    hover: null,
    timeline: null,
    player: null,
    view: null,
    lastCheck: null,
    storageOk: true,
    saveTimer: null,
    solve: null
  };

  var dom = {};

  function $(id) { return document.getElementById(id); }

  function el(tag, attrs, children) {
    var node = document.createElement(tag);
    if (attrs) {
      for (var key in attrs) {
        if (key === 'class') node.className = attrs[key];
        else if (key === 'text') node.textContent = attrs[key];
        else if (key.indexOf('on') === 0) node.addEventListener(key.substring(2), attrs[key]);
        else node.setAttribute(key, attrs[key]);
      }
    }
    if (children) {
      for (var i = 0; i < children.length; i++) {
        if (children[i]) node.appendChild(children[i]);
      }
    }
    return node;
  }

  function clampInt(value, fallback, min, max) {
    var parsed = typeof value === 'number' ? Math.trunc(value) : parseInt(value, 10);
    if (!isFinite(parsed)) parsed = fallback;
    return Math.max(min, Math.min(max, parsed));
  }

  function str(value) { return typeof value === 'string' ? value : ''; }

  function kindDefaults(kind) { return Doc.kindDefaults(kind); }


  /* 一行算空：一个 '#' 都没有（'.' 只是占位符）。
   * 注意不能反过来「删掉 '#' 再判长度」——那样「整行都是墙」会被当成空行丢掉，
   * 舞台与导出会同时少一堵墙。 */
  function isEmptyRow(row) { return !row || row.indexOf('#') < 0; }

  /* 把任意 JSON 收敛成编辑器能安全操作的结构（不丢信息、不猜错类型）。 */
  function normalizeDocument(raw) {
    var source = raw && typeof raw === 'object' ? raw : {};
    var width = clampInt(source.width, 9, 1, 40);
    var height = clampInt(source.height, 8, 1, 40);

    var result = {
      schema: Doc.CURRENT_SCHEMA,
      displayName: str(source.displayName) || 'NEW LEVEL',
      width: width,
      height: height,
      tickInterval: typeof source.tickInterval === 'number' && isFinite(source.tickInterval)
        ? Math.max(0.05, Math.min(5, source.tickInterval)) : 0.35,
      wallRows: [],
      elements: [],
      supplies: [],
      allowBuild: !!source.allowBuild
    };

    var rows = [];
    if (Array.isArray(source.wallRows)) {
      for (var i = 0; i < source.wallRows.length; i++) rows.push(str(source.wallRows[i]));
    } else if (typeof source.wallMap === 'string' && source.wallMap !== '') {
      rows = source.wallMap.replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
    }
    while (rows.length < height) rows.push('');
    result.wallRows = rows.slice(0, height);

    var cells = Array.isArray(source.elements) ? source.elements : [];
    for (var c = 0; c < cells.length; c++) {
      var cell = cells[c];
      if (!cell || typeof cell !== 'object') continue;
      var kind = Doc.parseKind(cell.kind);
      if (kind === null) kind = KIND.Wall;
      var defaults = kindDefaults(kind);

      result.elements.push({
        type: str(cell.type),
        kind: Doc.kindName(kind),
        x: clampInt(cell.x, 0, -1, 999),
        y: clampInt(cell.y, 0, -1, 999),
        rot: ((clampInt(cell.rot, 0, -99, 99) % 4) + 4) % 4,
        dir: str(cell.dir) || D.name(defaults.dir),
        dir2: str(cell.dir2) || D.name(defaults.dir2),
        sequence: str(cell.sequence) || defaults.sequence,
        count: typeof cell.count === 'number' ? Math.trunc(cell.count) : defaults.count,
        face: str(cell.face) || (kind === KIND.Receiver ? (defaults.acceptAnyFace ? 'any' : 'in') : ''),
        pass: typeof cell.pass === 'number' ? Math.trunc(cell.pass) : (kind === KIND.Filter ? defaults.pass : -1),
        delay: typeof cell.delay === 'number' ? Math.trunc(cell.delay) : (kind === KIND.DelayGate ? defaults.delay : -1),
        playerEditable: !!cell.playerEditable
      });
    }

    var supplies = Array.isArray(source.supplies) ? source.supplies : [];
    for (var s = 0; s < supplies.length; s++) {
      var supply = supplies[s];
      if (!supply || typeof supply !== 'object') continue;
      var supplyKind = Doc.parseKind(supply.kind);
      result.supplies.push({
        type: str(supply.type),
        kind: supplyKind === null ? '' : Doc.kindName(supplyKind),
        count: typeof supply.count === 'number' ? Math.trunc(supply.count) : -1
      });
    }

    result.allowBuild = !!source.allowBuild || result.supplies.length > 0;
    return result;
  }


  /* 导出用的文档：只写 schema 2 的字段，顺序固定，方便 diff。 */
  function buildDocument() {
    var source = state.document;
    var elements = [];

    for (var i = 0; i < source.elements.length; i++) {
      var cell = source.elements[i];
      elements.push({
        type: cell.type,
        kind: cell.kind,
        x: cell.x,
        y: cell.y,
        rot: cell.rot,
        dir: cell.dir,
        dir2: cell.dir2,
        sequence: cell.sequence,
        count: cell.count,
        face: cell.face,
        pass: cell.pass,
        delay: cell.delay,
        playerEditable: !!cell.playerEditable
      });
    }

    var wallRows = source.wallRows.slice(0, source.height);
    while (wallRows.length > 0 && isEmptyRow(wallRows[wallRows.length - 1])) {
      wallRows.pop();
    }

    return {
      schema: Doc.CURRENT_SCHEMA,
      displayName: source.displayName,
      width: source.width,
      height: source.height,
      tickInterval: source.tickInterval,
      wallType: '',
      wallRows: wallRows,
      elements: elements,
      supplies: source.supplies.map(function (supply) {
        return { type: supply.type, kind: supply.kind, count: supply.count };
      }),
      allowBuild: !!source.allowBuild
    };
  }

  /* ---------------- 本地存储（失败也不炸） ---------------- */
  function readStorage() {
    try {
      var text = window.localStorage.getItem(STORAGE_KEY);
      if (!text) return null;
      return JSON.parse(text);
    } catch (error) {
      state.storageOk = false;
      return null;
    }
  }

  function writeStorage() {
    if (!state.storageOk) return;
    /* 试玩中盘面是临时解，不能顶替作者自动保存的那一份。 */
    if (state.solve) return;
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify({
        savedAt: new Date().toISOString(),
        document: buildDocument()
      }));
    } catch (error) {
      state.storageOk = false;
      showStorageBanner('本地存储不可用（' + (error && error.message ? error.message : error) +
        '）。改动只在本页有效，请及时「导出 JSON」。');
    }
  }

  function showStorageBanner(message) {
    if (!dom.storageBanner) return;
    dom.storageBanner.textContent = message;
    dom.storageBanner.classList.remove('hidden');
    if (dom.footerStorage) {
      dom.footerStorage.textContent = '本地存储 不可用';
      dom.footerStorage.className = 'bad';
    }
  }

  function scheduleSave() {
    if (state.saveTimer) clearTimeout(state.saveTimer);
    state.saveTimer = setTimeout(function () {
      state.saveTimer = null;
      writeStorage();
      if (dom.metaHint) dom.metaHint.textContent = '已自动保存';
    }, 350);
  }

  /* 改动后统一走这里：重画 + 存盘 + 让上一次自检结论失效（避免看着旧结论改数据）。 */
  function changed(options) {
    var settings = options || {};
    scheduleSave();

    /* 约定：plain changed() 表示文档真的改了；keepCheck 的调用是纯 UI 动作
     * （切画笔、选格子、载入），不该进撤销栈。
     * 试玩中走自己的快照栈（solve.stack），不污染作者编辑历史。 */
    if (!settings.keepCheck && !settings.silentHistory && !state.solve) {
      recordHistory(settings.label || '编辑', { merge: settings.merge !== false });
    }

    if (!settings.keepCheck) {
      state.lastCheck = null;
      if (state.player) state.player.pause();
      state.player = null;
      state.timeline = null;
      if (dom.scrub) { dom.scrub.min = '0'; dom.scrub.max = '0'; dom.scrub.value = '0'; }
      if (dom.tickLabel) dom.tickLabel.textContent = 'tick 0 / 0';
      if (dom.btnPlay) dom.btnPlay.textContent = '▶';
      if (dom.reportText) {
        dom.reportText.textContent = '数据已改动，自检结论已过期，请重新运行。';
        dom.reportText.className = 'report';
      }
      if (dom.issueList) dom.issueList.innerHTML = '';
      if (dom.problemList) dom.problemList.innerHTML = '';
    }
    renderMeta();
    renderSupplies();
    renderInspector();
    renderPalette();
    renderInventory();
    renderHand();
    if (state.view) {
      state.view.setState({
        document: buildDocument(),
        catalog: Catalog.builtin,
        selection: state.selection,
        brush: state.solve ? null : state.brush,
        tool: state.solve ? 'select' : state.tool,
        hover: state.hover,
        frame: state.player ? state.player.frame() : null
      });
    }
    updateHistoryButtons();
  }

  /* ---------------- 撤销 / 重做 ----------------
   * 快照式：每一步存一份 {document, selection} 的 JSON。
   * 不做命令式 diff 的原因：所有编辑最终都收敛到同一份文档，快照实现短、
   * 不会漏字段（万一以后加新参数也不用改历史代码），40×40 一份也就几 KB。
   * 同一标签 700ms 内连改（拖手牌数量、连续打字）并成一步，免得撤销十几次。
   */
  var HISTORY = { stack: [], index: 0, label: '', at: 0, limit: 60 };

  function snapshotText() {
    return JSON.stringify({ document: buildDocument(), selection: state.selection });
  }

  function resetHistory() {
    HISTORY.stack = [snapshotText()];
    HISTORY.index = 0;
    HISTORY.label = '';
    HISTORY.at = 0;
    updateHistoryButtons();
  }

  /* 必须在改动落定之后再调，否则存进去的是改动前的样子。 */
  function recordHistory(label, options) {
    if (HISTORY.stack.length === 0) { resetHistory(); return; }

    var text = snapshotText();
    if (text === HISTORY.stack[HISTORY.index]) return; /* 没真改（比如点了同值） */

    var settings = options || {};
    var now = Date.now();
    var merge = settings.merge !== false && label === HISTORY.label &&
      now - HISTORY.at < 700 && HISTORY.index > 0;

    HISTORY.stack.length = HISTORY.index + 1; /* 有新改动就丢掉 redo 分支 */
    if (merge) {
      HISTORY.stack[HISTORY.index] = text;
    } else {
      HISTORY.stack.push(text);
      HISTORY.index = HISTORY.stack.length - 1;
    }
    if (HISTORY.stack.length > HISTORY.limit) {
      HISTORY.stack.shift();
      HISTORY.index = HISTORY.stack.length - 1;
    }

    HISTORY.label = label;
    HISTORY.at = now;
    updateHistoryButtons();
  }

  function applySnapshot(text) {
    var snapshot = JSON.parse(text);
    state.document = normalizeDocument(snapshot.document);

    var selection = snapshot.selection;
    var inside = !!selection && selection.x >= 0 && selection.y >= 0 &&
      selection.x < state.document.width && selection.y < state.document.height;
    state.selection = inside ? { x: selection.x, y: selection.y } : null;

    HISTORY.label = '';
    HISTORY.at = 0;
    changed({ silentHistory: true });
  }

  function undo() {
    if (state.solve) { solveUndo(); return; }
    if (HISTORY.index <= 0) return;
    HISTORY.index--;
    applySnapshot(HISTORY.stack[HISTORY.index]);
  }

  function redo() {
    if (state.solve) { solveRedo(); return; }
    if (HISTORY.index >= HISTORY.stack.length - 1) return;
    HISTORY.index++;
    applySnapshot(HISTORY.stack[HISTORY.index]);
  }

  function updateHistoryButtons() {
    if (state.solve) {
      var solve = state.solve;
      var canUndoS = solve.index > 0;
      var canRedoS = solve.index < solve.stack.length - 1;
      if (dom.btnUndo) {
        dom.btnUndo.disabled = !canUndoS;
        dom.btnUndo.title = canUndoS ? '撤销上一步试玩（Ctrl+Z）· 还能退 ' + solve.index + ' 步' : '试玩还没有可撤销的改动';
      }
      if (dom.btnRedo) {
        dom.btnRedo.disabled = !canRedoS;
        dom.btnRedo.title = canRedoS ? '重做（Ctrl+Y）' : '没有可重做的试玩改动';
      }
      return;
    }

    var canUndo = HISTORY.index > 0;
    var canRedo = HISTORY.index < HISTORY.stack.length - 1;
    if (dom.btnUndo) {
      dom.btnUndo.disabled = !canUndo;
      dom.btnUndo.title = canUndo
        ? '撤销上一步（Ctrl+Z）· 还能退 ' + HISTORY.index + ' 步'
        : '没有可撤销的改动';
    }
    if (dom.btnRedo) {
      dom.btnRedo.disabled = !canRedo;
      dom.btnRedo.title = canRedo
        ? '重做（Ctrl+Y）· 还有 ' + (HISTORY.stack.length - 1 - HISTORY.index) + ' 步'
        : '没有可重做的改动';
    }
  }

  /* ---------------- 画笔 ---------------- */
  function makeBrush(kind) {
    var defaults = kindDefaults(kind);
    return {
      kind: kind,
      type: '',
      dir: D.name(defaults.dir),
      dir2: D.name(defaults.dir2),
      sequence: defaults.sequence,
      count: defaults.count,
      acceptAnyFace: kind === KIND.Receiver ? true : defaults.acceptAnyFace,
      pass: defaults.pass,
      delay: defaults.delay,
      playerEditable: false
    };
  }

  function setBrush(kind) {
    state.brush = makeBrush(kind);
    state.tool = 'paint';
    state.selection = null;
    changed({ keepCheck: true });
  }

  function setTool(tool) {
    state.tool = tool === 'select' ? 'select' : 'paint';
    changed({ keepCheck: true });
  }

  function renderPalette() {
    if (!dom.palette) return;
    dom.palette.innerHTML = '';

    var entries = [TOOL_SELECT].concat(PALETTE);
    for (var i = 0; i < entries.length; i++) {
      var entry = entries[i];
      var isTool = entry.tool === 'select';
      var active = isTool
        ? state.tool === 'select'
        : (state.tool === 'paint' && state.brush && state.brush.kind === entry.kind);

      var button = el('button', {
        class: 'brush' + (active ? ' active' : '') + (isTool ? ' tool' : ''),
        title: isTool
          ? '选择工具：左键只选不落件（V / 0 / Esc）'
          : entry.label + ' · ' + entry.en + '（按 ' + entry.key + '）',
        onclick: isTool
          ? function () { setTool('select'); }
          : (function (kind) {
              return function () { setBrush(kind); };
            })(entry.kind)
      }, [
        el('span', { class: 'glyph', text: entry.short }),
        el('span', { class: 'name', text: entry.label }),
        el('span', { class: 'key', text: entry.key })
      ]);
      button.firstChild.style.color = entry.color;
      dom.palette.appendChild(button);
    }
  }

  /* ---------------- 格子操作 ---------------- */
  function findCellIndex(x, y) {
    var list = state.document.elements;
    for (var i = 0; i < list.length; i++) {
      if (list[i].x === x && list[i].y === y) return i;
    }
    return -1;
  }

  function setWallCell(x, y, on) {
    var rows = state.document.wallRows;
    while (rows.length <= y) rows.push('');
    var row = rows[y] || '';
    while (row.length <= x) row += '.';
    row = row.substring(0, x) + (on ? '#' : '.') + row.substring(x + 1);
    rows[y] = row;
  }

  function makeCell(kind, x, y) {
    var brush = state.brush || makeBrush(kind);
    var defaults = kindDefaults(kind);
    return {
      type: '',
      kind: Doc.kindName(kind),
      x: x,
      y: y,
      rot: 0,
      dir: brush.kind === kind ? brush.dir : D.name(defaults.dir),
      dir2: brush.kind === kind ? brush.dir2 : D.name(defaults.dir2),
      sequence: brush.kind === kind ? brush.sequence : defaults.sequence,
      count: brush.kind === kind ? brush.count : defaults.count,
      face: kind === KIND.Receiver ? ((brush.kind === kind ? brush.acceptAnyFace : true) ? 'any' : 'in') : '',
      pass: kind === KIND.Filter ? (brush.kind === kind ? brush.pass : defaults.pass) : -1,
      delay: kind === KIND.DelayGate ? (brush.kind === kind ? brush.delay : defaults.delay) : -1,
      playerEditable: !!(brush.kind === kind && brush.playerEditable)
    };
  }

  function wallAt(x, y) {
    return ((state.document.wallRows[y] || '').charAt(x)) === '#';
  }

  function selectCell(x, y) {
    if (!state.selection || state.selection.x !== x || state.selection.y !== y) {
      state.selection = { x: x, y: y };
    }
    changed({ keepCheck: true });
  }

  /* 左键的规则：格子已经被占用就只选中，绝不覆写（这就是旧版覆盖 bug 的根因）。
   * 只有「画笔模式 + 格子是空的」才落件；纯选择工具下永远只选。 */
  function applyBrushAt(x, y) {
    var brush = state.brush;
    var existing = findCellIndex(x, y);

    if (state.tool === 'select' || existing >= 0) {
      selectCell(x, y);
      return;
    }
    if (!brush) return;

    if (brush.kind === KIND.Wall) {
      if (wallAt(x, y)) { selectCell(x, y); return; }
      setWallCell(x, y, true);
      state.selection = { x: x, y: y };
      changed({ label: '画墙', merge: false });
      return;
    }

    setWallCell(x, y, false);
    var cell = makeCell(brush.kind, x, y);
    if (cell.type === '' && brush.type) cell.type = brush.type;
    state.document.elements.push(cell);
    state.selection = { x: x, y: y };
    changed({ label: '落件', merge: false });
  }

  function eraseAt(x, y) {
    var index = findCellIndex(x, y);
    if (index < 0 && !wallAt(x, y)) { selectCell(x, y); return; }

    if (index >= 0) state.document.elements.splice(index, 1);
    setWallCell(x, y, false);
    if (state.selection && state.selection.x === x && state.selection.y === y) state.selection = null;
    changed({ label: '擦除', merge: false });
  }

  function clearAll() {
    var doc = state.document;
    if (doc.elements.length === 0 && doc.wallRows.join('').indexOf('#') < 0) {
      if (dom.metaHint) dom.metaHint.textContent = '棋盘已经是空的';
      return;
    }

    doc.elements = [];
    doc.wallRows = [];
    while (doc.wallRows.length < doc.height) doc.wallRows.push('');
    state.selection = null;
    changed({ label: '清空', merge: false });
  }

  /* 旋转：kind 模式转绝对朝向，type 模式加 rot（游戏里就是这两种语义）。 */
  function rotateCell(cell) {
    if (!cell) return false;
    var kind = Doc.parseKind(cell.kind);
    if (kind === KIND.DelayGate || kind === KIND.Wall) return false;

    if (cell.type !== '' && cell.type) {
      cell.rot = ((cell.rot | 0) + 1) % 4;
      return true;
    }

    var dir = D.parse(cell.dir);
    if (dir === null) dir = D.RIGHT;
    cell.dir = D.name(D.clockwise(dir));

    if (kind === KIND.Splitter) {
      var dir2 = D.parse(cell.dir2);
      cell.dir2 = D.name(D.clockwise(dir2 === null ? D.DOWN : dir2));
    }
    return true;
  }

  function rotateSelection() {
    if (!state.selection) return;
    var index = findCellIndex(state.selection.x, state.selection.y);
    if (index < 0) return;
    if (rotateCell(state.document.elements[index])) changed({ label: '旋转', merge: false });
  }

  function setHover(cell) {
    state.hover = cell;
    if (dom.coordLabel) {
      dom.coordLabel.textContent = cell
        ? GridLib.describeCell(state.document, Catalog.builtin, cell.x, cell.y)
        : '悬停格子看坐标与参数';
    }
    if (state.view) state.view.setState({ hover: cell });
  }

  function bindCanvas() {
    var stage = dom.grid;

    stage.addEventListener('contextmenu', function (event) { event.preventDefault(); });

    stage.addEventListener('pointermove', function (event) {
      setHover(GridLib.cellFromEvent(event));
    });

    stage.addEventListener('pointerleave', function () { setHover(null); });

    stage.addEventListener('pointerdown', function (event) {
      var cell = GridLib.cellFromEvent(event);
      if (!cell) return;

      if (state.solve) {
        solvePointer(cell, event);
        return;
      }

      if (event.button === 2) {
        eraseAt(cell.x, cell.y);
        return;
      }

      if (event.shiftKey) {
        state.selection = { x: cell.x, y: cell.y };
        rotateSelection();
        return;
      }

      applyBrushAt(cell.x, cell.y);
    });
  }

  /* ---------------- 手牌试玩（Solve） ----------------
   * 规则全在 hand.js（tests/hand.js 单测覆盖），这里只做「点哪一格 → 调哪个方法 → 写句人话」。
   * 进入试玩时把 state.document 换成手牌那份副本，网格 / 清单 / 自检 / 时间轴都不用改。
   * 退出或采用解时再把文档换回去，所以作者原稿不会被试玩改动污染。
   */
  var SOLVE_REASONS = {
    NoPile: '先在手牌里选一张牌',
    NotPlaceable: '工具卡不放上棋盘：选中它再点带锁的预设件',
    OutOfBounds: '越界了：这一格不在棋盘上',
    OutOfStock: '这张牌已经用完了',
    Locked: '这一格是预设件：带锁的件不能搬（BUFFER 除外）',
    Occupied: '这一格已经有东西了',
    NotPlayerPiece: '这一格不是你自己放的件，搬不动',
    EmptyCell: '这一格是空的',
    NotLocked: 'BUFFER 只收带锁的预设件，自己放的不需要它',
    NoElement: '这一格没有元件',
    NotBufferable: '发射器 / 接收器是关卡骨架，BUFFER 也收不走'
  };

  function reasonText(reason) {
    return SOLVE_REASONS[reason] || ('这一步不行（' + reason + '）');
  }

  function describePile(pile) {
    if (!pile) return '未知元件';
    if (pile.label) return pile.label + (pile.type ? '（' + pile.type + '）' : '');
    return GridLib.KIND_LABELS[pile.kind] || Doc.kindName(pile.kind);
  }

  function solveMessage(text, bad) {
    if (!dom.handMsg) return;
    dom.handMsg.textContent = text;
    dom.handMsg.className = 'hand-msg' + (bad ? ' bad' : '');
  }

  /* 试玩中检查器只读：手牌是规则，参数得回编辑器改。 */
  function renderSolveInspector(box) {
    var solve = state.solve;
    var hand = solve.hand;
    if (dom.inspectorTitle) dom.inspectorTitle.textContent = '试玩 · 手牌';
    if (dom.inspectorHint) dom.inspectorHint.textContent = '规则与游戏一致 · 只读';

    var pile = hand.pile(solve.selected);
    if (pile) {
      box.appendChild(el('div', { class: 'field' }, [
        el('label', { text: '选中的牌' }),
        el('div', { text: describePile(pile) + ' · 剩 ' + pile.left + '/' + pile.total }),
        el('div', {
          class: 'hint',
          text: pile.tool
            ? '工具卡：点带锁的预设件把它收回手牌（发射器 / 接收器收不走）'
            : '点空格落件；右键或 Shift+左键旋转；点自己放的件拾回'
        })
      ]));
    }

    var status = hand.status();
    box.appendChild(el('div', { class: 'field' }, [
      el('label', { text: '手牌' }),
      el('div', { text: '剩 ' + status.left + '/' + status.total + ' 张 · 盘面自己放 ' + status.placed + ' 件' }),
      el('div', { class: 'hint', text: '锁定件 = 作者预设（playerEditable = false），搬不动也不能覆盖' })
    ]));

    if (hand.warnings.length) {
      box.appendChild(el('div', { class: 'field' }, [
        el('label', { text: '手牌警告' }),
        el('div', { class: 'hint', text: hand.warnings.join(' / ') })
      ]));
    }

    box.appendChild(el('div', {
      class: 'hint',
      text: '试玩期间不写回原稿、也不覆盖本地自动保存：「采用解」才会把布局写回关卡。'
    }));
  }

  function renderHand() {
    var solve = state.solve;
    if (!dom.handPiles) return;
    if (!solve) {
      if (dom.handTray) dom.handTray.classList.add('hidden');
      return;
    }
    if (dom.handTray) dom.handTray.classList.remove('hidden');

    var hand = solve.hand;
    var status = hand.status();
    if (dom.handStatus) {
      dom.handStatus.textContent = '剩 ' + status.left + '/' + status.total + ' 张 · 已放 ' +
        status.placed + ' · 空格 ' + status.empty;
    }

    dom.handPiles.innerHTML = '';
    if (hand.piles.length === 0) {
      dom.handPiles.appendChild(el('div', { class: 'muted', text: '这一关没有手牌（supplies 为空）。' }));
      return;
    }

    for (var i = 0; i < hand.piles.length; i++) {
      (function (pile) {
        var active = pile.key === solve.selected;
        var glyph = pile.label ? pile.label.charAt(0) : (GridLib.KIND_LABELS[pile.kind] || '?').charAt(0);
        var button = el('button', {
          class: 'pile' + (active ? ' active' : '') + (pile.left <= 0 ? ' empty' : '') + (pile.tool ? ' tool' : ''),
          title: describePile(pile) + ' · ' + (pile.tool
            ? '工具卡：选中后点带锁的预设件把它收回手牌'
            : '选中后点空格落件；右键 / Shift+左键旋转'),
          onclick: function () {
            solve.selected = pile.key;
            changed({ keepCheck: true });
            solveMessage('已选中 ' + describePile(pile) + '（剩 ' + pile.left + ' 张）');
          }
        }, [
          el('span', { class: 'pile-glyph', text: glyph }),
          el('span', { class: 'pile-count', text: '×' + pile.left }),
          el('span', { class: 'pile-kind', text: describePile(pile) })
        ]);
        if (pile.tool) button.appendChild(el('span', { class: 'pile-tool', text: '工具' }));
        dom.handPiles.appendChild(button);
      })(hand.piles[i]);
    }
  }

  function stopSolve() {
    state.solve = null;
    document.body.classList.remove('solving');
    if (dom.btnSolve) dom.btnSolve.textContent = '🧩 试玩';
    if (dom.handTray) dom.handTray.classList.add('hidden');
    if (dom.btnHandAdopt) dom.btnHandAdopt.disabled = true;
  }

  function enterSolve() {
    if (state.solve) return;
    var hand = HandLib.create(buildDocument(), Catalog.builtin);
    if (hand.piles.length === 0) {
      if (dom.metaHint) dom.metaHint.textContent = '这一关没有手牌（supplies 为空），没什么可试玩的：先加一摞手牌。';
      solveMessage('这一关没有手牌。', true);
      return;
    }

    state.solve = { hand: hand, stack: [], index: -1, selected: hand.piles[0].key };
    state.document = hand.document;
    state.selection = null;
    state.lastCheck = null;
    state.timeline = null;
    if (state.player) state.player.pause();
    state.player = null;

    document.body.classList.add('solving');
    if (dom.btnSolve) dom.btnSolve.textContent = '🧩 试玩中';
    pushSolve();
    changed({ silentHistory: true });

    var warningText = hand.warnings.length ? ' · 手牌有 ' + hand.warnings.length + ' 条警告（见「自检」）' : '';
    solveMessage('点一张牌，再点棋盘落件；右键旋转；点自己的件拾回；BUFFER 点锁定件收回。' + warningText);
  }

  function exitSolve() {
    if (!state.solve) return;
    var original = state.solve.hand.original;
    stopSolve();
    state.document = normalizeDocument(original);
    state.selection = null;
    state.lastCheck = null;
    state.timeline = null;
    changed({ silentHistory: true });
    if (dom.metaHint) dom.metaHint.textContent = '已结束试玩，回到预设布局（试玩时的改动没有写回）';
  }

  /* 采用解：这套布局转成预设件（玩家进关后搬不动），手牌原样保留。 */
  function adoptSolve() {
    if (!state.solve) return;
    var adopted = state.solve.hand.adopt();
    stopSolve();
    state.document = normalizeDocument(adopted);
    state.selection = null;
    state.lastCheck = null;
    state.timeline = null;
    changed({ label: '采用解', merge: false });
    if (dom.metaHint) dom.metaHint.textContent = '已采用试玩布局：这些件变成预设件（不可搬），手牌保留';
  }

  function pushSolve() {
    var solve = state.solve;
    if (!solve) return;
    solve.stack.length = solve.index + 1;
    solve.stack.push(solve.hand.serialize());
    if (solve.stack.length > 40) solve.stack.shift();
    solve.index = solve.stack.length - 1;
    updateHistoryButtons();
  }

  function applySolve(snapshot) {
    var hand = HandLib.restore(snapshot, Catalog.builtin);
    state.solve.hand = hand;
    state.document = hand.document;
    state.selection = null;
    changed({ silentHistory: true });
    updateHistoryButtons();
  }

  function solveUndo() {
    var solve = state.solve;
    if (!solve || solve.index <= 0) return;
    solve.index--;
    applySolve(solve.stack[solve.index]);
    solveMessage('已撤销一步试玩改动');
  }

  function solveRedo() {
    var solve = state.solve;
    if (!solve || solve.index >= solve.stack.length - 1) return;
    solve.index++;
    applySolve(solve.stack[solve.index]);
    solveMessage('已重做');
  }

  /* 左键：落件 / 拾回 / BUFFER 收回；右键或 Shift+左键：旋转自己的件。 */
  function solvePointer(cell, event) {
    var solve = state.solve;
    if (!solve) return;
    var hand = solve.hand;
    var x = cell.x;
    var y = cell.y;

    if (event.button === 2 || event.shiftKey) {
      if (hand.rotate(x, y)) {
        pushSolve();
        changed({ silentHistory: true });
        solveMessage('旋转了 (' + x + ',' + y + ')');
      } else {
        solveMessage('这里没有可以旋转的件（墙 / 延迟器 / 预设件不转）', true);
      }
      return;
    }

    var pile = hand.pile(solve.selected);
    if (pile && pile.tool) {
      var retracted = hand.retract(x, y, pile.key);
      if (!retracted.ok) { solveMessage(reasonText(retracted.reason), true); return; }
      pushSolve();
      changed({ silentHistory: true });
      solveMessage('BUFFER 收回一枚 ' + describePile(retracted.pile) + '，已进手牌');
      return;
    }

    if (hand.isPlaced(x, y)) {
      var picked = hand.pick(x, y);
      if (!picked.ok) { solveMessage(reasonText(picked.reason), true); return; }
      pushSolve();
      changed({ silentHistory: true });
      solveMessage('拾回一枚 ' + describePile(picked.pile));
      return;
    }

    if (!pile) { solveMessage(reasonText('NoPile'), true); return; }

    var placed = hand.place(pile.key, x, y);
    if (!placed.ok) { solveMessage(reasonText(placed.reason), true); return; }
    pushSolve();
    changed({ silentHistory: true });
    solveMessage('落件 ' + describePile(pile) + ' @ (' + x + ',' + y + ')');
  }

  /* ---------------- 检查器 ---------------- */
  /* ---------------- 检查器 ---------------- */
  function field(label, control, hint) {
    var wrap = el('div', { class: 'field' }, [
      el('label', { text: label }),
      control
    ]);
    if (hint) wrap.appendChild(el('div', { class: 'hint', text: hint }));
    return wrap;
  }

  function dirSelect(value, onChange) {
    var select = el('select');
    for (var dir = 0; dir < 4; dir++) {
      var option = el('option', { value: D.name(dir), text: D.mark(dir) + ' ' + D.chineseName(dir) });
      if (D.parse(value) === dir) option.selected = true;
      select.appendChild(option);
    }
    select.addEventListener('change', function () { onChange(select.value); });
    return select;
  }

  function textInput(value, onChange, placeholder) {
    var input = el('input', { type: 'text', value: value });
    if (placeholder) input.placeholder = placeholder;
    input.addEventListener('change', function () { onChange(input.value); });
    return input;
  }

  function numberInput(value, onChange, min, max) {
    var input = el('input', { type: 'number', value: String(value) });
    if (typeof min === 'number') input.min = String(min);
    if (typeof max === 'number') input.max = String(max);
    input.addEventListener('change', function () {
      onChange(clampInt(input.value, value, typeof min === 'number' ? min : -999, typeof max === 'number' ? max : 999));
    });
    return input;
  }

  function checkbox(checked, onChange, labelText) {
    var input = el('input', { type: 'checkbox' });
    input.checked = !!checked;
    input.addEventListener('change', function () { onChange(input.checked); });
    return el('label', { class: 'row' }, [input, el('span', { text: labelText })]);
  }

  function renderInspector() {
    var box = dom.inspector;
    if (!box) return;
    box.innerHTML = '';

    if (state.solve) { renderSolveInspector(box); return; }

    var cellIndex = state.selection ? findCellIndex(state.selection.x, state.selection.y) : -1;
    var cell = cellIndex >= 0 ? state.document.elements[cellIndex] : null;
    var target = cell || state.brush;

    dom.inspectorTitle.textContent = cell ? '选中的格子' : '画笔参数';
    dom.inspectorHint.textContent = cell
      ? cell.kind + ' @ ' + cell.x + ',' + cell.y
      : '下一次落件用的默认值';

    if (!target) return;

    var kind = cell ? Doc.parseKind(cell.kind) : state.brush.kind;

    if (cell) {
      box.appendChild(field('类型 type', textInput(cell.type, function (value) {
        cell.type = value.trim();
        changed();
      }, '空 = 用 kind 默认值'), '写了 type 就按类型快照 + rot 读，pass 被忽略'));
      box.appendChild(field('旋转 rot', numberInput(cell.rot, function (value) {
        cell.rot = ((value % 4) + 4) % 4;
        changed();
      }, 0, 3), '只对 type 模式生效（顺时针 90°/档）'));
    }

    if (kind === KIND.Emitter || kind === KIND.NotGate || kind === KIND.Filter) {
      box.appendChild(field('朝向 dir', dirSelect(target.dir, function (value) {
        target.dir = value;
        changed();
      })));
    }

    if (kind === KIND.Splitter) {
      box.appendChild(field('1-臂 dir', dirSelect(target.dir, function (value) {
        target.dir = value;
        changed();
      })));
      box.appendChild(field('0-臂 dir2', dirSelect(target.dir2, function (value) {
        target.dir2 = value;
        changed();
      })));
    }

    if (kind === KIND.Receiver) {
      box.appendChild(field('进料面 dir', dirSelect(target.dir, function (value) {
        target.dir = value;
        changed();
      }), cell ? 'type 模式下由类型 + rot 决定，这里的值不生效' : '四面可收时只作显示朝向'));
    }

    if (kind === KIND.Emitter || kind === KIND.Receiver) {
      box.appendChild(field('序列 sequence', textInput(target.sequence, function (value) {
        target.sequence = value.trim();
        changed();
      }, '只认 0/1，其它字符忽略')));
      box.appendChild(field('数量 count', numberInput(target.count, function (value) {
        target.count = Math.max(0, value);
        changed();
      }, 0, 999), kind === KIND.Emitter ? '总共吐几位（循环填满）' : '总共要收几位'));
    }

    if (kind === KIND.Receiver) {
      if (!cell) target.face = target.acceptAnyFace === false ? 'in' : 'any';
      var faceSelect = el('select');
      [['any', 'any · 四面可收'], ['in', 'in · 只认进料面']].forEach(function (pair) {
        var option = el('option', { value: pair[0], text: pair[1] });
        if ((target.face || 'any') === pair[0]) option.selected = true;
        faceSelect.appendChild(option);
      });
      faceSelect.addEventListener('change', function () {
        target.face = faceSelect.value;
        if (cell) {
          changed();
        } else {
          target.acceptAnyFace = faceSelect.value === 'any';
          renderInspector();
        }
      });
      box.appendChild(field('进料面 face', faceSelect));
    }

    if (kind === KIND.Filter) {
      var passSelect = el('select');
      [[0, '放行 0'], [1, '放行 1']].forEach(function (pair) {
        var option = el('option', { value: String(pair[0]), text: pair[1] });
        if ((target.pass | 0) === pair[0]) option.selected = true;
        passSelect.appendChild(option);
      });
      passSelect.addEventListener('change', function () {
        target.pass = parseInt(passSelect.value, 10);
        changed();
      });
      box.appendChild(field('放行 pass', passSelect));
    }

    if (kind === KIND.DelayGate) {
      box.appendChild(field('延迟 delay', numberInput(target.delay, function (value) {
        target.delay = Math.max(1, Math.min(4, value));
        changed();
      }, 1, 4), '比直通多停 N 拍，1~4'));
    }

    if (cell) {
      box.appendChild(field('玩家可搬', checkbox(cell.playerEditable, function (value) {
        cell.playerEditable = value;
        changed();
      }, 'playerEditable')));
      box.appendChild(field('', el('button', {
        class: 'btn small',
        text: '删除这一格（Delete）',
        onclick: function () { eraseAt(cell.x, cell.y); }
      })));
    } else {
      box.appendChild(field('玩家可搬', checkbox(!!state.brush.playerEditable, function (value) {
        state.brush.playerEditable = value;
        renderInspector();
      }, '新落件的 playerEditable')));
      box.appendChild(el('div', {
        class: 'muted',
        text: '提示：Shift + 点格子可以旋转；右键擦除；墙画在独立的墙层里。'
      }));
    }
  }

  /* ---------------- 关卡属性 ---------------- */
  function resizeDocument(width, height) {
    var doc = state.document;
    if (width === doc.width && height === doc.height) return;

    doc.width = width;
    doc.height = height;

    while (doc.wallRows.length < height) doc.wallRows.push('');
    doc.wallRows = doc.wallRows.slice(0, height).map(function (row) { return row.substring(0, width); });

    var kept = [];
    var dropped = 0;
    for (var i = 0; i < doc.elements.length; i++) {
      var cell = doc.elements[i];
      if (cell.x < width && cell.y < height) kept.push(cell);
      else dropped++;
    }
    doc.elements = kept;

    if (state.selection && (state.selection.x >= width || state.selection.y >= height)) state.selection = null;
    if (dom.metaHint) {
      dom.metaHint.textContent = dropped > 0 ? dropped + ' 个越界元件已移除' : '已调整尺寸';
    }
    changed({ label: '尺寸', merge: false });
  }

  function renderMeta() {
    var box = dom.metaForm;
    if (!box) return;
    box.innerHTML = '';

    var doc = state.document;

    box.appendChild(field('关卡名', textInput(doc.displayName, function (value) {
      doc.displayName = value.trim() || 'NEW LEVEL';
      changed();
    })));

    box.appendChild(field('宽 × 高', (function () {
      var row = el('div', { class: 'row' });
      row.appendChild(numberInput(doc.width, function (value) {
        resizeDocument(clampInt(value, doc.width, 1, 40), doc.height);
      }, 1, 40));
      row.appendChild(el('span', { class: 'muted', text: '×' }));
      row.appendChild(numberInput(doc.height, function (value) {
        resizeDocument(doc.width, clampInt(value, doc.height, 1, 40));
      }, 1, 40));
      return row;
    })(), '画布尺寸；缩小会移除越界元件'));

    box.appendChild(field('tick 秒', (function () {
      var input = el('input', { type: 'number', value: String(doc.tickInterval) });
      input.min = '0.05';
      input.max = '5';
      input.step = '0.05';
      input.addEventListener('change', function () {
        var value = parseFloat(input.value);
        if (!isFinite(value)) value = 0.35;
        doc.tickInterval = Math.max(0.05, Math.min(5, value));
        changed();
      });
      return input;
    })(), '每 tick 的墙钟时长，0.05~5 秒'));

    box.appendChild(field('allowBuild', checkbox(doc.allowBuild, function (value) {
      doc.allowBuild = value;
      changed();
    }, '允许玩家进关后自己搬件')));
  }

  /* ---------------- 手牌 ---------------- */
  var SUPPLY_KINDS = [KIND.Wall, KIND.Splitter, KIND.NotGate, KIND.Filter, KIND.DelayGate, KIND.Emitter, KIND.Receiver];

  function renderSupplies() {
    var box = dom.supplyList;
    if (!box) return;
    box.innerHTML = '';

    var supplies = state.document.supplies;
    if (supplies.length === 0) {
      box.appendChild(el('div', { class: 'muted', text: '没有手牌：这一关的预设布局必须自己跑通。' }));
      return;
    }

    for (var i = 0; i < supplies.length; i++) {
      (function (index) {
        var supply = supplies[index];
        var row = el('div', { class: 'supply-row' });

        row.appendChild(textInput(supply.type, function (value) {
          supply.type = value.trim();
          changed();
        }, 'type（可空）'));

        var kindSelect = el('select');
        for (var k = 0; k < SUPPLY_KINDS.length; k++) {
          var kind = SUPPLY_KINDS[k];
          var option = el('option', { value: Doc.kindName(kind), text: Doc.kindName(kind) });
          if (Doc.parseKind(supply.kind) === kind) option.selected = true;
          kindSelect.appendChild(option);
        }
        if (Doc.parseKind(supply.kind) === null) kindSelect.value = '';
        kindSelect.addEventListener('change', function () {
          supply.kind = kindSelect.value;
          changed();
        });
        row.appendChild(kindSelect);

        row.appendChild(numberInput(supply.count, function (value) {
          supply.count = value;
          changed();
        }, -1, 999));

        row.appendChild(el('button', {
          class: 'btn small',
          text: '×',
          title: '删掉这一摞',
          onclick: function () {
            state.document.supplies.splice(index, 1);
            changed();
          }
        }));

        box.appendChild(row);
      })(i);
    }

    box.appendChild(el('div', {
      class: 'muted',
      text: 'count = -1 表示 1 张；0 会被游戏跳过并记一条问题；空位不够放牌时自检会拦下。'
    }));
  }

  /* ---------------- 清单 ----------------
   * 旧编辑器最救命的一块：不用点开每一格就知道场上有什么、参数对不对。
   */
  function describeRawCell(cell) {
    var kind = Doc.parseKind(cell.kind);
    var parts = ['(' + cell.x + ',' + cell.y + ')'];

    if (cell.type) parts.push('type ' + cell.type);
    if (kind === KIND.Emitter) {
      parts.push(D.mark(D.parse(cell.dir)) + ' 序列 ' + (cell.sequence || '') + ' ×' + cell.count);
    } else if (kind === KIND.Receiver) {
      parts.push((cell.face === 'in' ? '只认进料面' : '四面可收') + ' ×' + cell.count);
    } else if (kind === KIND.Splitter) {
      parts.push('1' + D.mark(D.parse(cell.dir)) + ' 0' + D.mark(D.parse(cell.dir2)));
    } else if (kind === KIND.NotGate) {
      parts.push('出口 ' + D.mark(D.parse(cell.dir)));
    } else if (kind === KIND.Filter) {
      parts.push('放行 ' + cell.pass + ' · 出口 ' + D.mark(D.parse(cell.dir)));
    } else if (kind === KIND.DelayGate) {
      parts.push('停 ' + cell.delay + ' 拍');
    }
    if (cell.playerEditable) parts.push('可搬');
    return parts.join(' ');
  }

  function renderInventory() {
    var box = dom.inventory;
    if (!box) return;
    box.innerHTML = '';

    var doc = state.document;
    var cells = doc.elements;
    var wallCount = 0;
    for (var r = 0; r < doc.wallRows.length; r++) {
      var row = doc.wallRows[r] || '';
      for (var c = 0; c < row.length; c++) if (row.charAt(c) === '#') wallCount++;
    }

    var head = el('div', { class: 'inventory-head' });
    head.appendChild(el('strong', { text: '清单' }));
    head.appendChild(el('span', {
      class: 'muted',
      text: doc.width + '×' + doc.height + ' · ' + cells.length + ' 个元件 · ' + wallCount + ' 面墙 · tick ' +
        doc.tickInterval + 's · allowBuild ' + (doc.allowBuild ? '开' : '关')
    }));
    box.appendChild(head);

    if (cells.length === 0 && wallCount === 0) {
      box.appendChild(el('div', { class: 'inv-empty', text: '棋盘是空的：选个画笔，左键落件。' }));
      return;
    }

    var order = [KIND.Emitter, KIND.Receiver, KIND.Splitter, KIND.NotGate, KIND.Filter, KIND.DelayGate, KIND.Wall];
    for (var o = 0; o < order.length; o++) {
      var kind = order[o];
      var group = [];
      for (var i = 0; i < cells.length; i++) {
        if (Doc.parseKind(cells[i].kind) === kind) group.push(cells[i]);
      }
      if (group.length === 0) continue;

      var line = el('div', { class: 'inv-group' });
      var dot = el('span', { class: 'inv-dot' });
      dot.style.background = GridLib.KIND_COLORS[kind] || '#5b6673';
      line.appendChild(dot);
      line.appendChild(el('span', { class: 'inv-name', text: GridLib.KIND_LABELS[kind] + ' ×' + group.length }));

      var body = el('span', { class: 'inv-body' });
      for (var g = 0; g < group.length; g++) {
        if (g > 0) body.appendChild(el('span', { class: 'inv-sep', text: ' | ' }));
        body.appendChild(el('span', { class: 'inv-cell', text: describeRawCell(group[g]) }));
      }
      line.appendChild(body);
      box.appendChild(line);
    }

    if (doc.supplies.length > 0) {
      var supplies = [];
      for (var s = 0; s < doc.supplies.length; s++) {
        var supply = doc.supplies[s];
        supplies.push((supply.kind || '?') + '×' + supply.count + (supply.type ? '(' + supply.type + ')' : ''));
      }
      box.appendChild(el('div', { class: 'inv-supply', text: '手牌 ' + supplies.join(' · ') }));
    }
  }

  /* ---------------- 自检 + 时间轴 ---------------- */
  function severityLabel(severity) {
    return severity === 'Blocking' ? '必须改' : '提醒';
  }

  function renderIssues(issues) {
    var box = dom.issueList;
    if (!box) return;
    box.innerHTML = '';

    if (issues.length === 0) {
      box.appendChild(el('div', { class: 'issue', text: '没有阻塞性意见：这份数据可以存。' }));
      return;
    }

    for (var i = 0; i < issues.length; i++) {
      var issue = issues[i];
      box.appendChild(el('div', {
        class: 'issue ' + (issue.severity === 'Blocking' ? 'blocking' : 'advisory'),
        text: '[' + severityLabel(issue.severity) + '] ' + issue.message
      }));
    }
  }

  function renderProblems(problems) {
    var box = dom.problemList;
    if (!box) return;
    box.innerHTML = '';
    if (!problems || problems.length === 0) return;

    for (var i = 0; i < problems.length; i++) {
      box.appendChild(el('div', { class: 'issue problem', text: '读档问题：' + problems[i] }));
    }
  }

  function countReasons(report) {
    var counts = {};
    for (var i = 0; i < report.destroyed.length; i++) {
      var reason = report.destroyed[i].reason;
      counts[reason] = (counts[reason] || 0) + 1;
    }
    var parts = [];
    for (var key in counts) parts.push(key + '×' + counts[key]);
    return parts.join(' ');
  }

  function describeFrame(frame) {
    if (!frame) return '';
    if (!frame.report) return 'tick 0 · 初始状态（还没有位出场）';

    var report = frame.report;
    var parts = ['tick ' + frame.tick];
    if (report.spawns.length) parts.push('出场 ' + report.spawns.length);
    if (report.deliveries.length) parts.push('收货 ' + report.deliveries.length);
    if (report.destroyed.length) parts.push('销毁 ' + report.destroyed.length + '（' + countReasons(report) + '）');
    if (report.delayAbsorbed.length) parts.push('延迟收纳 ' + report.delayAbsorbed.length);
    if (report.delayReleased.length) parts.push('延迟放行 ' + report.delayReleased.length);
    if (report.delayDropped) parts.push('延迟溢出 ' + report.delayDropped);
    if (report.stalledEmitters) parts.push('发射顺延 ' + report.stalledEmitters);
    if (report.delayStalled) parts.push('延迟堵口 ' + report.delayStalled);
    if (report.isFinished) parts.push('（本 tick 结算）');
    return parts.join(' · ');
  }

  function onFrame(frame, index, total) {
    if (state.view) state.view.setState({ frame: frame });
    if (dom.tickLabel) dom.tickLabel.textContent = 'tick ' + frame.tick + ' / ' + (state.timeline ? state.timeline.totalTicks : 0);
    if (dom.scrub && dom.scrub.max !== String(total - 1)) dom.scrub.max = String(total - 1);
    if (dom.scrub) dom.scrub.value = String(index);
    if (dom.eventLine) dom.eventLine.textContent = describeFrame(frame);
    if (dom.btnPlay) dom.btnPlay.textContent = state.player && state.player.playing ? '⏸' : '▶';
  }

  function runCheck() {
    var document_ = buildDocument();
    var read = Doc.read(document_, Catalog.builtin);
    var outcome = Check.run(read.definition, read.definition.hasSupplies);
    var issues = Check.runForSaveDetailed(read.definition);

    state.lastCheck = { read: read, outcome: outcome, issues: issues };

    if (dom.reportText) {
      dom.reportText.className = 'report';
      dom.reportText.textContent = outcome.report;
    }
    renderIssues(issues);
    renderProblems(read.problems);

    if (state.solve) {
      var solved = !!outcome.success;
      solveMessage(solved
        ? '✅ 这套解跑通了：点「采用解」把它写回关卡'
        : '❌ 还没跑通 —— 看「自检」报告里的第一条', !solved);
      if (dom.btnHandAdopt) dom.btnHandAdopt.disabled = !solved;
    }

    state.timeline = Timeline.build(read.definition, { maxFrames: Timeline.DEFAULT_MAX_FRAMES });
    if (state.player) state.player.pause();
    state.player = new Timeline.Player(state.timeline, onFrame);
    state.player.setTickInterval(read.definition.tickInterval);

    if (dom.scrub) {
      dom.scrub.min = '0';
      dom.scrub.max = String(state.timeline.frames.length - 1);
      dom.scrub.value = '0';
    }

    state.selection = null;
    state.view.setState({ document: document_, selection: null, frame: null });
    state.player.seek(0);

    if (state.timeline.truncated) {
      dom.eventLine.textContent += ' · 回放只到第 ' + state.timeline.frames.length + ' 帧（自检结论仍是完整跑完的）';
    }
  }

  /* ---------------- 载入 / 新建 / 导入 / 导出 ---------------- */
  function loadDocument(raw, options) {
    var settings = options || {};
    /* 换关就退出试玩：否则手牌会指向上一关的文档。 */
    if (state.solve) stopSolve();
    state.document = normalizeDocument(raw);
    state.selection = null;
    state.hover = null;
    state.lastCheck = null;
    state.timeline = null;
    if (state.player) state.player.pause();
    state.player = null;

    if (settings.focusBrush) state.brush = makeBrush(settings.focusBrush);
    else if (!state.brush) state.brush = makeBrush(KIND.Emitter);

    if (dom.reportText) {
      dom.reportText.className = 'report';
      dom.reportText.textContent = settings.reportText || '尚未运行。点「运行自检」用游戏同一份模拟器跑到底。';
    }
    if (dom.issueList) dom.issueList.innerHTML = '';
    if (dom.problemList) dom.problemList.innerHTML = '';
    if (dom.eventLine) dom.eventLine.textContent = '自检后可以逐 tick 回放';
    if (dom.tickLabel) dom.tickLabel.textContent = 'tick 0 / 0';
    if (dom.scrub) { dom.scrub.min = '0'; dom.scrub.max = '0'; dom.scrub.value = '0'; }
    if (dom.metaHint) dom.metaHint.textContent = settings.hint || '';

    state.view.resize();
    resetHistory();
    changed({ keepCheck: true });
  }

  function newLevel() {
    loadDocument(Samples.blank(), { focusBrush: KIND.Emitter, hint: '已新建空白关卡' });
  }

  function sanitizeFileName(name) {
    var base = (name || 'level').trim().replace(/[\\/:*?"<>|]+/g, '_');
    if (base === '') base = 'level';
    return base + '.json';
  }

  function exportJson() {
    /* 试玩中的盘面是临时解，导出会顺手把人骗了：让作者先做选择。 */
    if (state.solve) {
      solveMessage('试玩中不导出：先「采用解」写回，或「退出」回到预设布局', true);
      return;
    }
    var document_ = buildDocument();
    var text = JSON.stringify(document_, null, 2) + '\n';

    try {
      var blob = new Blob([text], { type: 'application/json' });
      var url = URL.createObjectURL(blob);
      var link = el('a', { href: url, download: sanitizeFileName(document_.displayName) });
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
      if (dom.metaHint) dom.metaHint.textContent = '已导出 ' + sanitizeFileName(document_.displayName);
    } catch (error) {
      showStorageBanner('导出失败（' + error + '）。可以打开「导入 JSON」把文本复制出来。');
    }
  }

  function openImport() {
    if (!dom.importModal) return;
    dom.importError.classList.add('hidden');
    dom.importError.textContent = '';
    dom.importText.value = '';
    dom.importModal.classList.remove('hidden');
    dom.importText.focus();
  }

  function closeImport() {
    if (dom.importModal) dom.importModal.classList.add('hidden');
  }

  function doImport() {
    var text = dom.importText.value.trim();
    if (text === '') {
      dom.importError.textContent = '请粘贴 JSON，或先选择文件。';
      dom.importError.classList.remove('hidden');
      return;
    }

    var parsed;
    try {
      parsed = JSON.parse(text);
    } catch (error) {
      dom.importError.textContent = 'JSON 解析失败：' + error.message;
      dom.importError.classList.remove('hidden');
      return;
    }

    var lost = [];
    if (!Array.isArray(parsed.elements)) lost.push('elements');
    if (parsed.wallRows === undefined && parsed.wallMap === undefined) lost.push('wallRows');

    closeImport();
    loadDocument(parsed, {
      hint: lost.length ? '已导入（原文件缺少 ' + lost.join(' / ') + '，按空处理）' : '已导入'
    });
  }

  /* ---------------- 文档抽屉 ---------------- */
  function renderDocs() {
    var body = dom.docsBody;
    if (!body) return;
    body.innerHTML = '';

    for (var i = 0; i < Docs.SECTIONS.length; i++) {
      var section = Docs.SECTIONS[i];
      body.appendChild(el('h3', { text: section.title }));
      var content = el('div');
      content.innerHTML = section.html;
      body.appendChild(content);
    }
  }

  /* ---------------- 页脚 ---------------- */
  function renderFooter() {
    if (dom.footerBuild) dom.footerBuild.textContent = 'build ' + BUILD_STAMP;

    var result;
    try {
      result = SelfTest.run();
    } catch (error) {
      result = {
        ok: false, passed: 0, total: 0, fixtureCount: 0,
        checks: [{ name: '自检本身抛错', ok: false, detail: String(error) }]
      };
    }

    if (dom.footerSelfTest) {
      dom.footerSelfTest.textContent = '自检 ' + result.passed + '/' + result.total + (result.ok ? ' 通过' : ' 失败');
      dom.footerSelfTest.className = result.ok ? 'ok' : 'bad';
      dom.footerSelfTest.title = result.checks.map(function (check) {
        return (check.ok ? '✓ ' : '✗ ') + check.name + (check.detail ? ' — ' + check.detail : '');
      }).join('\n');
    }

    if (dom.footerParity) {
      dom.footerParity.textContent = '对拍 ' + result.fixtureCount + ' 关 · tests/parity.js';
      dom.footerParity.title = 'C# 侧用同一个 Core 源文件跑同一批 fixture，逐字段比对';
    }

    if (dom.footerStorage && state.storageOk) {
      dom.footerStorage.textContent = '本地存储 可用';
      dom.footerStorage.className = 'muted';
    }
  }

  function populateSamples() {
    if (!dom.sampleSelect) return;
    for (var i = 0; i < Samples.SAMPLES.length; i++) {
      var sample = Samples.SAMPLES[i];
      dom.sampleSelect.appendChild(el('option', { value: sample.id, text: sample.title }));
    }
  }

  /* ---------------- 事件绑定 ---------------- */
  function isTypingTarget(target) {
    if (!target) return false;
    var tag = (target.tagName || '').toLowerCase();
    return tag === 'input' || tag === 'textarea' || tag === 'select';
  }

  function bindEvents() {
    dom.btnCheck.addEventListener('click', runCheck);
    dom.btnNew.addEventListener('click', newLevel);
    dom.btnExport.addEventListener('click', exportJson);
    dom.btnImport.addEventListener('click', openImport);
    dom.btnDoImport.addEventListener('click', doImport);
    dom.btnCancelImport.addEventListener('click', closeImport);
    dom.btnDocs.addEventListener('click', function () {
      dom.docsDrawer.classList.remove('hidden');
      dom.docsDrawer.setAttribute('aria-hidden', 'false');
    });
    dom.btnCloseDocs.addEventListener('click', function () {
      dom.docsDrawer.classList.add('hidden');
      dom.docsDrawer.setAttribute('aria-hidden', 'true');
    });

    dom.btnAddSupply.addEventListener('click', function () {
      state.document.supplies.push({ type: '', kind: 'Splitter', count: 1 });
      changed({ label: '手牌', merge: false });
    });

    dom.btnUndo.addEventListener('click', undo);
    dom.btnRedo.addEventListener('click', redo);
    if (dom.btnSolve) {
      dom.btnSolve.addEventListener('click', function () {
        if (state.solve) exitSolve();
        else enterSolve();
      });
    }
    if (dom.btnHandExit) dom.btnHandExit.addEventListener('click', exitSolve);
    if (dom.btnHandAdopt) dom.btnHandAdopt.addEventListener('click', adoptSolve);
    dom.btnRotate.addEventListener('click', rotateSelection);
    dom.btnErase.addEventListener('click', function () {
      if (state.selection) eraseAt(state.selection.x, state.selection.y);
    });
    dom.btnClear.addEventListener('click', clearAll);

    dom.sampleSelect.addEventListener('change', function () {
      var id = dom.sampleSelect.value;
      dom.sampleSelect.value = '';
      if (!id) return;
      var sample = Samples.byId(id);
      if (!sample) return;
      loadDocument(Samples.clone(sample.document), { hint: sample.note });
    });

    dom.importFile.addEventListener('change', function () {
      var file = dom.importFile.files && dom.importFile.files[0];
      if (!file) return;
      var reader = new FileReader();
      reader.onload = function () {
        dom.importText.value = String(reader.result || '');
        dom.importError.classList.add('hidden');
      };
      reader.onerror = function () {
        dom.importError.textContent = '读文件失败：' +
          (reader.error && reader.error.message ? reader.error.message : '未知错误');
        dom.importError.classList.remove('hidden');
      };
      reader.readAsText(file);
    });

    dom.btnRestart.addEventListener('click', function () {
      if (state.player) state.player.restart();
    });
    dom.btnPrev.addEventListener('click', function () {
      if (state.player) state.player.stepBy(-1);
    });
    dom.btnNext.addEventListener('click', function () {
      if (state.player) state.player.stepBy(1);
    });
    dom.btnPlay.addEventListener('click', function () {
      if (!state.player) { runCheck(); return; }
      state.player.toggle();
    });
    dom.speedSelect.addEventListener('change', function () {
      if (state.player) state.player.setSpeed(parseFloat(dom.speedSelect.value));
    });
    dom.scrub.addEventListener('input', function () {
      if (state.player) state.player.seek(parseInt(dom.scrub.value, 10));
    });

    document.addEventListener('keydown', function (event) {
      if (event.ctrlKey && (event.key === 's' || event.key === 'S')) {
        event.preventDefault();
        exportJson();
        return;
      }
      if (isTypingTarget(event.target)) return;

      var key = event.key.toLowerCase();
      var accel = event.ctrlKey || event.metaKey;

      if (accel && key === 'z') {
        event.preventDefault();
        if (event.shiftKey) redo();
        else undo();
        return;
      }
      if (accel && key === 'y') {
        event.preventDefault();
        redo();
        return;
      }

      /* 试玩中画笔 / 旋转 / 删除快捷键没有意义（棋盘是手牌文档），按了就当没按。 */
      if (state.solve && (
        (event.key >= '1' && event.key <= '7') || key === 'v' || event.key === '0' ||
        key === 'r' || event.key === 'Delete' || event.key === 'Backspace')) {
        return;
      }

      if (event.key >= '1' && event.key <= '7') {
        var index = parseInt(event.key, 10) - 1;
        if (PALETTE[index]) setBrush(PALETTE[index].kind);
        return;
      }
      if (key === 'v' || event.key === '0') {
        setTool('select');
        return;
      }
      if (key === 'r') {
        rotateSelection();
        return;
      }
      if (event.key === 'Delete' || event.key === 'Backspace') {
        if (state.selection) {
          event.preventDefault();
          eraseAt(state.selection.x, state.selection.y);
        }
        return;
      }
      if (event.key === 'ArrowLeft' && state.player) {
        event.preventDefault();
        state.player.stepBy(-1);
        return;
      }
      if (event.key === 'ArrowRight' && state.player) {
        event.preventDefault();
        state.player.stepBy(1);
        return;
      }
      if (event.key === ' ') {
        event.preventDefault();
        if (!state.player) runCheck();
        else state.player.toggle();
        return;
      }
      if (event.key === 'Escape') {
        closeImport();
        dom.docsDrawer.classList.add('hidden');
        dom.docsDrawer.setAttribute('aria-hidden', 'true');
        if (state.tool !== 'select') setTool('select');
      }
    });

    window.addEventListener('resize', function () {
      if (state.view) state.view.resize();
    });
  }

  /* ---------------- 启动 ---------------- */
  function boot() {
    var ids = [
      'storageBanner', 'palette', 'grid', 'timelineBar', 'btnRestart', 'btnPrev', 'btnPlay', 'btnNext',
      'speedSelect', 'tickLabel', 'scrub', 'eventLine', 'reportText', 'issueList', 'problemList',
      'btnCheck', 'inspector', 'inspectorTitle', 'inspectorHint', 'metaForm', 'metaHint',
      'supplyList', 'btnAddSupply', 'footerBuild', 'footerSelfTest', 'footerParity', 'footerStorage',
      'docsDrawer', 'docsBody', 'btnDocs', 'btnCloseDocs', 'importModal', 'importText', 'importFile',
      'btnDoImport', 'btnCancelImport', 'importError', 'sampleSelect', 'btnNew', 'btnImport', 'btnExport',
      'btnUndo', 'btnRedo', 'btnRotate', 'btnErase', 'btnClear', 'coordLabel', 'inventory',
      'handTray', 'handPiles', 'handStatus', 'handMsg', 'btnSolve', 'btnHandAdopt', 'btnHandExit'
    ];
    for (var i = 0; i < ids.length; i++) dom[ids[i]] = $(ids[i]);

    state.brush = makeBrush(KIND.Emitter);
    state.tool = 'paint';
    state.view = new GridLib.Grid(dom.grid);

    var saved = readStorage();
    var initial = saved && saved.document
      ? saved.document
      : Samples.clone(Samples.byId('wait111').document);
    var initialHint = saved && saved.document && saved.savedAt
      ? '已恢复上次编辑（' + saved.savedAt.replace('T', ' ').slice(0, 16) + '）'
      : '默认载入官方关卡 WAIT111';

    populateSamples();
    renderDocs();
    bindEvents();
    bindCanvas();

    loadDocument(initial, { hint: initialHint });
    renderFooter();

    requestAnimationFrame(function () {
      state.view.resize();
      if (state.player) state.player.emit();
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})(typeof globalThis !== 'undefined' ? globalThis : this);
