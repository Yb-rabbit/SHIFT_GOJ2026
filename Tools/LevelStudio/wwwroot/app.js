/* BitFlow 关卡编辑器前端。
   没有任何构建步骤：这个文件由本地 http 服务直接发给浏览器。
   校验一律走后端（/api/check），跑的是 Unity 里同一份 BitFlowSimulation ——
   前端只负责画格子、改参数、拼 JSON。 */

/* 种类表必须和 Core 的 CellKind 一一对应（7 种）。
   少写一种的后果不是「画不出来」，而是**读进来的那一格被整格丢掉** ——
   所以新增 CellKind 时，这里、SUPPLY_KINDS、Api.cs 的兜底目录要一起加。 */
const KINDS = [
  { kind: 'Emitter',   glyph: 'E', label: '发射器', params: ['dir', 'sequence', 'count'] },
  { kind: 'Receiver',  glyph: 'R', label: '接收器', params: ['dir', 'sequence', 'count', 'face'] },
  { kind: 'Splitter',  glyph: 'S', label: '分流器', params: ['dir', 'dir2'] },
  { kind: 'NotGate',   glyph: 'N', label: '非门',   params: ['dir'] },
  { kind: 'Filter',    glyph: 'F', label: '过滤器', params: ['dir', 'pass'] },
  { kind: 'DelayGate', glyph: 'D', label: '延迟器', params: ['delay'] },
  { kind: 'Wall',      glyph: '#', label: '墙',     params: [] },
];

/* 手牌能发哪些种类：和 Core 的 SupplyKinds.IsSupplyKind 一字不差。
   发射器/接收器不外发 —— 那等于把出题权交给玩家。

   这只是「门槛」：具体发哪一张牌由类型目录决定（Filter 有 F0/F1、DelayGate 有 D1~D4），
   下拉选的是类型资产 key，不是种类。 */
const SUPPLY_KINDS = ['Wall', 'Splitter', 'NotGate', 'Filter', 'DelayGate'];

/* 墙类型缺省值：和 LevelDocument.wallType 的默认一致（空串 = 用目录里第一份墙类型）。 */
const DEFAULT_WALL_TYPE = 'Type_Wall';

/* 尺寸档位必须和游戏给玩家的四档一字不差。
   来源：Assets/Scripts/Gameplay/BitFlow/Runtime/View/BitFlowWorkshop.cs 的 SizePresets
   （9x8 是主线的标准板）+ LevelSelectPanel.cs 的 NEW LEVEL 第二屏「PICK A GRID」——
   玩家建关只有这四个按钮，没有输入框。作者在这里做出第 5 种尺寸，游戏里选不出来。 */
const SIZE_PRESETS = [
  { width: 9, height: 8, standard: true },
  { width: 11, height: 8 },
  { width: 7, height: 6 },
  { width: 13, height: 9 },
];

const DIRS = ['Up', 'Right', 'Down', 'Left'];
const DIR_MARK = { Up: '↑', Right: '→', Down: '↓', Left: '←' };
const DIR_CN = { Up: '上', Right: '右', Down: '下', Left: '左' };

const state = {
  doc: newDoc(SIZE_PRESETS[0].width, SIZE_PRESETS[0].height),
  brush: 'Emitter',
  selectedKey: null,
  catalog: [],
  where: 'incoming',

  /* 当前这份文档对应的磁盘文件名（不含 .json）；null = 还没存过盘（或文档是凭空粘进来的）。
     保存时优先用它 —— 否则「打开 A.json、改完保存」会按 displayName 写出 B.json，
     磁盘上凭空多一份，之后改哪一份都可能改错。 */
  fileName: null,

  /* 目录列表的最近一次结果（loadFiles 回填），只用来给新文档挑一个不撞名的默认标题。 */
  files: [],

  notes: [],
};

/* 撤销栈：快照式（整份文档 JSON 压栈）。
   文档极小（9x8、几十个元件 = 几 KB），60 步也就几百 KB；
   换成「按操作 diff」要覆盖涂刷/擦除/旋转/6 个参数/手牌/元信息/尺寸/清空，
   漏一类就是「Ctrl+Z 行为诡异」的 bug 源 —— 快照不可能漏。 */
const HISTORY = {
  undo: [],
  redo: [],
  limit: 60,
  lastLabel: '',   // 与 lastAt 一起做 700ms 合并窗：一次拖拽 / 连续打字算一步
  lastAt: 0,
};

/* 只用于把「来自 JSON 内容」的文本塞进 innerHTML —— displayName 是作者随便写的，可能带 < >。 */
function escapeHtml(text) {
  return String(text == null ? '' : text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

/* ------------------------------------------------------------------ 关卡名规则

   规则本体在 Assets/Scripts/Gameplay/BitFlow/Runtime/Core/LevelNaming.cs —— 那个文件被
   游戏和这个编辑器一起编译（ShiftGoLevelStudio.csproj 直接 Include 它），所以判定只有一份。
   下面是它的逐字符镜像，唯一用途是让作者在打字时就看到「这个名字会存成哪个文件」；
   真正的判定在 /api/save 里，前端说什么都不算数。

   改了 Core/LevelNaming.cs 就同步这里，否则预览会和落盘结果不一致。
   注意 char.ToUpperInvariant 对某些字符（ß）不做映射，JS 的 toUpperCase 却会展开成 'SS' ——
   所以展开不是单字符的一律丢弃，跟 C# 保持一样的结果。 */
const LEVEL_NAMING = {
  maxLength: 20,
  fallback: 'CUSTOM LEVEL',
};

/** 只留 A-Z 0-9 - _ 与空格，大写、折叠空格、截断 20 字。空名字返回 ''。 */
function sanitizeName(raw) {
  let out = '';
  let lastWasSpace = false;

  for (const ch of String(raw == null ? '' : raw)) {
    const upper = ch.toUpperCase();
    if (upper.length !== 1) continue;          // ß → 'SS' 这种，C# 不会保留
    const c = upper;

    const allowed = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c === '-' || c === '_' || c === ' ';
    if (!allowed) continue;

    if (c === ' ') {
      if (lastWasSpace || out.length === 0) continue;
      lastWasSpace = true;
    } else {
      lastWasSpace = false;
    }

    out += c;
    if (out.length >= LEVEL_NAMING.maxLength) break;
  }

  return out.trim();
}

/** 名字 → 文件名（不含扩展名）：'MY LEVEL 07' → 'MY_LEVEL_07'。 */
function baseNameOf(displayName) {
  const safe = sanitizeName(displayName);
  return (safe || LEVEL_NAMING.fallback).replace(/ /g, '_');
}

/** 名字 → 文件名：'MY LEVEL 07' → 'MY_LEVEL_07.json'。 */
function fileNameOf(displayName) {
  return baseNameOf(displayName) + '.json';
}

/* 「会存成哪个文件」始终可见，但不可编辑 —— 它是名字的派生品，不是第二个名字。
   以前这里是个输入框，作者于是以为文件名可以随便填；填得和名字不一致，就是
   「文件叫 MY_LEVEL_02_01、游戏里这关叫 111」那种同一关两个名字。 */
function renderSavePreview() {
  const typed = String(el.displayName.value || '');
  const display = sanitizeName(typed);

  if (!display) {
    el.saveAsPreview.textContent = typed.trim()
      ? '这些字游戏里显示不出来，换个名字'
      : '还没起名字';
    el.saveAsPreview.className = 'bad';
  } else {
    el.saveAsPreview.textContent = '存为 ' + fileNameOf(display);
    el.saveAsPreview.className = 'dim';
  }

  // 框里的字和最终名字不一致时（大小写、非法字符、超长），把结果直接写出来
  if (el.nameRule) {
    el.nameRule.textContent = display && display !== typed ? '（会存成「' + display + '」）' : '';
  }
}

const el = {
  displayName: document.getElementById('displayName'),
  size: document.getElementById('size'),
  tickInterval: document.getElementById('tickInterval'),
  allowBuild: document.getElementById('allowBuild'),
  palette: document.getElementById('palette'),
  grid: document.getElementById('grid'),
  coordLabel: document.getElementById('coordLabel'),
  problems: document.getElementById('problems'),
  inspector: document.getElementById('inspector'),
  checkResult: document.getElementById('checkResult'),
  where: document.getElementById('where'),
  fileList: document.getElementById('fileList'),
  pathInfo: document.getElementById('pathInfo'),
  saveAsPreview: document.getElementById('saveAsPreview'),
  nameRule: document.getElementById('nameRule'),
  jsonBox: document.getElementById('jsonBox'),
  status: document.getElementById('status'),
  allowBuildNote: document.getElementById('allowBuildNote'),
  supplyKind: document.getElementById('supplyKind'),
  supplyCount: document.getElementById('supplyCount'),
  supplyList: document.getElementById('supplyList'),
  btnUndo: document.getElementById('btnUndo'),
  btnRedo: document.getElementById('btnRedo'),
  filePicker: document.getElementById('filePicker'),
  dropHint: document.getElementById('dropHint'),
};

// ------------------------------------------------------------------ 数据

function newDoc(width, height) {
  return {
    schema: 2,
    displayName: 'CUSTOM LEVEL',
    width: width,
    height: height,
    tickInterval: 0.35,
    wallType: DEFAULT_WALL_TYPE,

    /* 墙一律走 wallRows（一行一个 y，第一行 y=0）。wallMap 只是旧文件的读入口，编辑器不再产出它。 */
    wallRows: [],
    elements: [],
    supplies: [],
    allowBuild: false,
  };
}

function kindInfo(kind) {
  return KINDS.find((k) => k.kind === kind) || KINDS[0];
}

/* 新文档的默认关卡名。
   目录里已经有一堆 CUSTOM LEVEL 时别再添一个 —— 选关列表里十几个同名关卡，
   作者（和玩家）根本分不清谁是谁。挑第一个没被占用的编号即可。

   「占着」按名字算：displayName 直接比，文件名也换算回名字比 ——
   存在 CUSTOM_LEVEL_01.json 时，CUSTOM LEVEL 01 也算被占了 —— 它存下去正是落在
   这个文件上，后端会拒。 */
function nextDocName() {
  const taken = new Set();
  for (const file of state.files) {
    if (file.displayName) taken.add(String(file.displayName));
    if (file.name) {
      taken.add(String(file.name));
      taken.add(String(file.name).replace(/_/g, ' '));
    }
  }

  for (let i = 1; i <= 99; i++) {
    const candidate = 'CUSTOM LEVEL ' + String(i).padStart(2, '0');
    if (!taken.has(candidate)) return candidate;
  }

  return 'CUSTOM LEVEL';
}

function coordKey(x, y) {
  return x + ',' + y;
}

function cellIndex() {
  const map = new Map();
  for (const cell of state.doc.elements) map.set(coordKey(cell.x, cell.y), cell);
  return map;
}

function cellAt(x, y) {
  return state.doc.elements.find((c) => c.x === x && c.y === y) || null;
}

function isWall(x, y) {
  const rows = state.doc.wallRows;
  if (!rows || y < 0 || y >= rows.length) return false;
  const row = rows[y] || '';
  return x >= 0 && x < row.length && row[x] === '#';
}

function ensureWallRows() {
  const width = state.doc.width;
  const height = state.doc.height;
  if (!state.doc.wallRows) state.doc.wallRows = [];

  for (let y = 0; y < height; y++) {
    let row = state.doc.wallRows[y] || '';
    if (row.length < width) row = row.padEnd(width, '.');
    state.doc.wallRows[y] = row;
  }

  state.doc.wallRows.length = height;
}

function setWall(x, y, on) {
  ensureWallRows();
  const row = state.doc.wallRows[y] || '';
  const before = row[x] || '.';
  const next = on ? '#' : '.';
  if (before === next) return;

  state.doc.wallRows[y] = row.substring(0, x) + next + row.substring(x + 1);
}

/** 延迟器 tick 数：和 Core 一样夹在 1~4（LevelDocumentBuilder.ApplyCellOverrides 同一套规则）。 */
function clampDelay(value) {
  const ticks = parseInt(value, 10);
  if (!Number.isFinite(ticks)) return 1;
  return Math.min(4, Math.max(1, ticks));
}

/** 某个种类新建一格时的默认参数：优先用类型目录里同类第一份，和 Unity 导入器的口味一致。 */
function defaultsFor(kind) {
  const spec = (state.catalog || []).find((t) => (t.kind || '').toLowerCase() === kind.toLowerCase());
  if (spec) {
    return {
      dir: spec.dir || 'Right',
      dir2: spec.dir2 || 'Down',
      sequence: spec.sequence || '1',
      count: typeof spec.count === 'number' ? spec.count : 1,
      face: spec.acceptAnyFace === false ? 'in' : 'any',
      pass: spec.pass === 0 ? 0 : 1,
      delay: clampDelay(spec.delay),
    };
  }

  switch (kind) {
    case 'Receiver': return { dir: 'Down', dir2: 'Down', sequence: '1', count: 1, face: 'any', pass: 1, delay: 1 };
    case 'Splitter': return { dir: 'Up', dir2: 'Right', sequence: '1', count: 1, face: 'any', pass: 1, delay: 1 };
    default: return { dir: 'Right', dir2: 'Down', sequence: '1', count: 1, face: 'any', pass: 1, delay: 1 };
  }
}

function newCell(kind, x, y) {
  const cell = { kind: kind, x: x, y: y, type: '', rot: 0, ...defaultsFor(kind), playerEditable: false };
  return cell;
}

// ------------------------------------------------------------------ 左栏：元件

function renderPalette() {
  el.palette.innerHTML = '';
  for (const info of KINDS) {
    const item = document.createElement('div');
    item.className = 'palette-item' + (state.brush === info.kind ? ' active' : '');
    item.innerHTML = '<span class="glyph">' + info.glyph + '</span>'
      + '<span class="name">' + info.label + '</span>'
      + '<span class="sub">' + info.kind + '</span>';
    item.onclick = () => {
      state.brush = info.kind;
      renderPalette();
    };
    el.palette.appendChild(item);
  }
}

// ------------------------------------------------------------------ 网格

function elementMarkup(cell) {
  const info = kindInfo(cell.kind);
  let html = '<span class="glyph">' + info.glyph + '</span>';

  const arrows = [];
  if (info.params.indexOf('dir') >= 0) arrows.push(cell.dir || 'Right');
  if (info.params.indexOf('dir2') >= 0) arrows.push(cell.dir2 || 'Down');

  for (const dir of arrows) {
    html += '<span class="arrow ' + String(dir).toLowerCase() + '">' + (DIR_MARK[dir] || '?') + '</span>';
  }

  if (cell.kind === 'Emitter') html += '<span class="badge">' + (cell.count || 0) + '</span>';
  if (cell.kind === 'Receiver') html += '<span class="badge">' + (cell.count || 0) + '</span>';
  if (cell.kind === 'DelayGate') html += '<span class="badge">+' + clampDelay(cell.delay) + '</span>';

  return html;
}

function renderGrid() {
  const width = state.doc.width;
  const height = state.doc.height;
  const index = cellIndex();

  el.grid.style.gridTemplateColumns = 'repeat(' + width + ', 1fr)';
  el.grid.innerHTML = '';

  // 从 y=height-1 画到 y=0：屏幕上方是最后一个 y（和游戏里的观感一致）
  for (let y = height - 1; y >= 0; y--) {
    for (let x = 0; x < width; x++) {
      const cell = index.get(coordKey(x, y)) || null;
      const node = document.createElement('div');
      node.className = 'cell';
      node.dataset.x = x;
      node.dataset.y = y;

      if (cell) {
        const info = kindInfo(cell.kind);
        if (cell.kind === 'Wall') node.classList.add('wall');
        if (cell.playerEditable) node.classList.add('playerEditable');
        if (state.selectedKey === coordKey(x, y)) node.classList.add('selected');
        node.innerHTML = cell.kind === 'Wall'
          ? '<span class="glyph">#</span>'
          : elementMarkup(cell);
        node.title = describeCell(cell);
      } else if (isWall(x, y)) {
        node.classList.add('wall');
        node.innerHTML = '<span class="glyph">#</span>';
        node.title = '(' + x + ',' + y + ') 墙（wallRows）';
      } else {
        node.title = '(' + x + ',' + y + ')';
      }

      node.oncontextmenu = (event) => {
        event.preventDefault();
        eraseAt(x, y);
      };
      node.onclick = () => paintAt(x, y);

      el.grid.appendChild(node);
    }
  }
}

function describeCell(cell) {
  const info = kindInfo(cell.kind);
  const parts = ['(' + cell.x + ',' + cell.y + ')', info.label];
  if (info.params.indexOf('dir') >= 0) parts.push('朝' + (DIR_CN[cell.dir] || cell.dir));
  if (info.params.indexOf('dir2') >= 0) parts.push('零臂' + (DIR_CN[cell.dir2] || cell.dir2));
  if (info.params.indexOf('sequence') >= 0) parts.push('序列 ' + cell.sequence + ' × ' + cell.count);
  if (info.params.indexOf('face') >= 0) parts.push(cell.face === 'in' ? '只认进料面' : '四面可收');
  if (info.params.indexOf('pass') >= 0) parts.push('放行 ' + cell.pass);
  if (info.params.indexOf('delay') >= 0) parts.push('延迟 ' + clampDelay(cell.delay) + ' tick');
  if (cell.type) parts.push('type=' + cell.type + (cell.rot ? ' rot=' + cell.rot : ''));
  return parts.join(' · ');
}

// ------------------------------------------------------------------ 撤销 / 重做

/**
 * 记一步「改之前」的状态。
 * label 只用于按钮 title 和合并判断；700ms 内连续同 label 的操作（拖拽涂刷、序列框打字）
 * 合成一步，否则一次拖 10 格就要按 10 次 Ctrl+Z。
 */
function beginHistory(label) {
  const now = Date.now();
  const merge = label === HISTORY.lastLabel && now - HISTORY.lastAt < 700;
  HISTORY.lastLabel = label;
  HISTORY.lastAt = now;
  if (merge) return;

  const snapshot = JSON.stringify(state.doc);
  const top = HISTORY.undo[HISTORY.undo.length - 1];
  if (top && top.doc === snapshot) return;   // 没真改就别占格子

  HISTORY.undo.push({ label: label, doc: snapshot });
  if (HISTORY.undo.length > HISTORY.limit) HISTORY.undo.shift();
  HISTORY.redo.length = 0;
  updateHistoryButtons();
}

/** 换文档（打开文件 / 导入文本框 / 新建）时历史应当跟着换，否则 Ctrl+Z 会跳回上一关。 */
function resetHistory() {
  HISTORY.undo.length = 0;
  HISTORY.redo.length = 0;
  HISTORY.lastLabel = '';
  HISTORY.lastAt = 0;
  updateHistoryButtons();
}

function applySnapshot(snapshot) {
  state.doc = JSON.parse(snapshot);

  // 撤销后选中的格子可能已经不存在了
  if (state.selectedKey) {
    const parts = state.selectedKey.split(',');
    if (!cellAt(Number(parts[0]), Number(parts[1]))) state.selectedKey = null;
  }

  syncMetaInputs();   // 输入框显示的是旧尺寸/旧关卡名就不对了
  afterEdit();

  HISTORY.lastLabel = '';   // 撤销后紧接着的新操作不该并进上一步
  HISTORY.lastAt = 0;
  updateHistoryButtons();
}

function undo() {
  if (!HISTORY.undo.length) return;
  const step = HISTORY.undo.pop();
  HISTORY.redo.push({ label: step.label, doc: JSON.stringify(state.doc) });
  applySnapshot(step.doc);
  setStatus('已撤销：' + step.label, true);
}

function redo() {
  if (!HISTORY.redo.length) return;
  const step = HISTORY.redo.pop();
  HISTORY.undo.push({ label: step.label, doc: JSON.stringify(state.doc) });
  applySnapshot(step.doc);
  setStatus('已重做：' + step.label, true);
}

function updateHistoryButtons() {
  if (!el.btnUndo || !el.btnRedo) return;

  const undoStep = HISTORY.undo[HISTORY.undo.length - 1];
  const redoStep = HISTORY.redo[HISTORY.redo.length - 1];

  el.btnUndo.disabled = !undoStep;
  el.btnRedo.disabled = !redoStep;
  el.btnUndo.title = undoStep ? '撤销：' + undoStep.label + '（Ctrl+Z）' : '没有可撤销的操作';
  el.btnRedo.title = redoStep ? '重做：' + redoStep.label + '（Ctrl+Y）' : '没有可重做的操作';
}

// ------------------------------------------------------------------ 绘制操作

/* 只选中，不碰文档、不压撤销 ——「点一下看看这格是什么」不该算一次编辑。 */
function selectCell(x, y) {
  state.selectedKey = coordKey(x, y);
  renderGrid();
  renderInspector();
}

function paintAt(x, y) {
  const existing = cellAt(x, y);

  /* 左键永不删已有的元件。
     以前这一格是 removeCell(existing)：想点开看看参数，元件连同参数一起被手上这件顶掉，
     而且画笔放完还武装着，下一击又是一次覆盖。现在只选中 ——
     要换种类就右键擦掉再放，这两个动作界面上本来就有，不用多记一个「模式」。 */
  if (existing) {
    selectCell(x, y);
    return;
  }

  if (state.brush === 'Wall') {
    // 墙不是元件、没有参数可丢，所以仍然允许来回涂（涂上 / 取消）
    beginHistory('涂刷');
    setWall(x, y, !isWall(x, y));
    state.selectedKey = coordKey(x, y);
    afterEdit();
    return;
  }

  // beginHistory 要在真改之前记快照，但必须放在「确定要改」之后：以前它排在函数第一行，
  // 于是点一下已经放好的同类格子也会积一步空撤销（Ctrl+Z 按下去画面没变）。
  beginHistory('涂刷');
  setWall(x, y, false);
  state.doc.elements.push(newCell(state.brush, x, y));
  state.selectedKey = coordKey(x, y);
  afterEdit();
}

function eraseAt(x, y) {
  const existing = cellAt(x, y);
  const hasWall = isWall(x, y);

  // 空格上右键什么都不该发生，更不该占一步撤销
  if (!existing && !hasWall) {
    selectCell(x, y);
    return;
  }

  beginHistory('涂刷');
  if (existing) removeCell(existing);
  else setWall(x, y, false);

  state.selectedKey = coordKey(x, y);
  afterEdit();
}

function removeCell(cell) {
  const index = state.doc.elements.indexOf(cell);
  if (index >= 0) state.doc.elements.splice(index, 1);
}

/** type 模式下这一格的基准朝向（= 类型资产上写的朝向，尚未旋转）。 */
function baseDirsFor(cell) {
  const spec = cell.type ? (state.catalog || []).find((t) => t.key === cell.type) : null;
  if (!spec) return null;
  return { dir: spec.dir || 'Right', dir2: spec.dir2 || 'Down' };
}

/** 改 dir / pass / delay 之前先脱开 type：这三样在 type 模式下被忽略，留着就是「改了没反应」。 */
function detachTypeForEdit(cell, what) {
  if (!cell.type) return;
  cell.type = '';
  cell.rot = 0;
  setStatus('已解除 type 绑定（' + what + ' 在 type 模式下不生效），这一格改成 kind 模式', false);
}

function rotateCell(cell) {
  const info = kindInfo(cell.kind);
  if (info.params.indexOf('dir') < 0) return;

  // type 模式：朝向 = 资产基准朝向 + rot，所以旋转动的是 rot，不是 dir
  if (cell.type) {
    const base = baseDirsFor(cell);
    if (!base) return;              // 目录里没这份类型：什么都不改，也就不该占一步撤销
    beginHistory('旋转');
    cell.rot = ((Number(cell.rot) || 0) + 1) % 4;
    cell.dir = rotateBy(base.dir, cell.rot);
    cell.dir2 = rotateBy(base.dir2, cell.rot);
    return;
  }

  beginHistory('旋转');
  const next = DIRS[(DIRS.indexOf(cell.dir || 'Right') + 1) % 4];
  cell.dir = next;

  // 分流器两臂必须互相垂直：转主臂时把零臂一起转，省得作者转出个「平行分流器」
  if (cell.kind === 'Splitter') {
    const zeroIndex = DIRS.indexOf(cell.dir2 || 'Down');
    cell.dir2 = DIRS[(zeroIndex + 1) % 4];
  }
}

function afterEdit() {
  renderGrid();
  renderInspector();
  renderSupplies();
  syncAllowBuildLock();
}

function selectedCell() {
  if (!state.selectedKey) return null;
  const parts = state.selectedKey.split(',');
  return cellAt(Number(parts[0]), Number(parts[1]));
}

// ------------------------------------------------------------------ 右栏：选中格子的参数

function fieldRow(labelText, node) {
  const label = document.createElement('label');
  const name = document.createElement('span');
  name.textContent = labelText;
  label.appendChild(name);
  label.appendChild(node);
  return label;
}

function dirPicker(current, onChange, label) {
  const wrap = document.createElement('div');
  wrap.className = 'dir-row';

  for (const dir of DIRS) {
    const button = document.createElement('button');
    button.textContent = DIR_MARK[dir] + ' ' + DIR_CN[dir];
    if (dir === current) button.classList.add('active');
    button.onclick = () => {
      beginHistory(label);
      onChange(dir);
      afterEdit();
    };
    wrap.appendChild(button);
  }

  return wrap;
}

function renderInspector() {
  el.coordLabel.textContent = state.selectedKey ? '(' + state.selectedKey + ')' : '—';

  const cell = selectedCell();
  if (!cell) {
    el.inspector.innerHTML = '<p class="dim">先点一下网格里的格子。</p>';
    return;
  }

  const info = kindInfo(cell.kind);
  el.inspector.innerHTML = '';

  const title = document.createElement('div');
  title.className = 'row';
  title.innerHTML = '<strong>' + cell.kind + '</strong>' +
    (cell.type
      ? '<span class="dim">type=' + cell.type + ' · rot=' + ((Number(cell.rot) || 0) % 4) + '</span>'
      : '<span class="dim">kind 模式（参数全写进 JSON）</span>');
  el.inspector.appendChild(title);

  if (cell.type) {
    const note = document.createElement('p');
    note.className = 'hint';
    note.textContent = 'type 模式下朝向是「资产朝向 + rot」（旋转按钮改 rot）；dir / 放行值 / 延迟数在 type 模式下不生效，改它们会自动解除绑定。';
    el.inspector.appendChild(note);
  }

  if (info.params.indexOf('dir') >= 0) {
    const label = cell.kind === 'Receiver' ? '进料面' : '朝向';
    el.inspector.appendChild(fieldRow(label, dirPicker(cell.dir || 'Right', (dir) => {
      detachTypeForEdit(cell, '朝向');
      cell.dir = dir;
    }, '朝向')));
  }

  if (info.params.indexOf('dir2') >= 0) {
    el.inspector.appendChild(fieldRow('零臂', dirPicker(cell.dir2 || 'Down', (dir) => {
      detachTypeForEdit(cell, '零臂朝向');
      cell.dir2 = dir;
    }, '零臂朝向')));
  }

  if (info.params.indexOf('sequence') >= 0) {
    const input = document.createElement('input');
    input.type = 'text';
    input.value = cell.sequence || '1';
    input.spellcheck = false;
    input.oninput = () => {
      beginHistory('序列');
      cell.sequence = input.value.trim();
      renderGrid();
    };
    el.inspector.appendChild(fieldRow('序列', input));
  }

  if (info.params.indexOf('count') >= 0) {
    const input = document.createElement('input');
    input.type = 'number';
    input.min = '0';
    input.value = Number(cell.count || 0);
    input.oninput = () => {
      beginHistory('数量');
      cell.count = Math.max(0, parseInt(input.value, 10) || 0);
      renderGrid();
    };
    el.inspector.appendChild(fieldRow('数量', input));
  }

  if (info.params.indexOf('face') >= 0) {
    const select = document.createElement('select');
    select.innerHTML = '<option value="any">四面可收</option><option value="in">只认进料面</option>';
    select.value = cell.face === 'in' ? 'in' : 'any';
    select.onchange = () => {
      beginHistory('收位面');
      cell.face = select.value;
      renderGrid();
    };
    el.inspector.appendChild(fieldRow('收位面', select));
  }

  if (info.params.indexOf('pass') >= 0) {
    const select = document.createElement('select');
    select.innerHTML = '<option value="1">放行 1</option><option value="0">放行 0</option>';
    select.value = String(cell.pass === 0 ? 0 : 1);
    select.onchange = () => {
      beginHistory('放行值');
      detachTypeForEdit(cell, '放行值');
      cell.pass = select.value === '0' ? 0 : 1;
      renderGrid();
    };
    el.inspector.appendChild(fieldRow('放行值', select));
  }

  if (info.params.indexOf('delay') >= 0) {
    const select = document.createElement('select');
    for (let tick = 1; tick <= 4; tick++) {
      select.innerHTML += '<option value="' + tick + '">延迟 ' + tick + ' tick</option>';
    }
    select.value = String(clampDelay(cell.delay));
    select.onchange = () => {
      beginHistory('延迟');
      detachTypeForEdit(cell, '延迟数');
      cell.delay = clampDelay(select.value);
      renderGrid();
    };
    el.inspector.appendChild(fieldRow('延迟', select));
  }

  const editable = document.createElement('input');
  editable.type = 'checkbox';
  editable.checked = !!cell.playerEditable;
  editable.onchange = () => {
    beginHistory('玩家可动');
    cell.playerEditable = editable.checked;
    renderGrid();
  };
  el.inspector.appendChild(fieldRow('玩家可动', editable));

  if (cell.type) {
    const detach = document.createElement('button');
    detach.textContent = '解除 type 绑定（改成 kind 模式）';
    detach.onclick = () => {
      beginHistory('解除 type');
      cell.type = '';
      cell.rot = 0;
      afterEdit();
    };
    el.inspector.appendChild(detach);
  }
}

// ------------------------------------------------------------------ 文档 → JSON / JSON → 文档

function rotateBy(dir, steps) {
  const index = DIRS.indexOf(dir);
  if (index < 0) return dir;
  const shift = ((steps % 4) + 4) % 4;
  return DIRS[(index + shift) % 4];
}

/**
 * 文档 → JSON。
 *
 * 两条规矩：
 * 1. 作者在编辑器里摆的格子是 kind 模式（参数全写进 JSON，不依赖类型资产）；
 * 2. 从 Unity 导出的 type 模式关卡里读到的格子**保留 type / rot** ——
 *    否则「打开官方关卡 → 存盘」会把元件的资产绑定一股脑抹掉（label、美术、提示全退回种类名）。
 */
function serializeDoc() {
  const doc = state.doc;

  const out = {
    schema: 2,
    // 名字就是身份，不再拿文件名兜底 —— 文件名是它的派生品（见 fileNameOf），不是另一个真名。
    displayName: doc.displayName || LEVEL_NAMING.fallback,
    width: doc.width,
    height: doc.height,
    // 四舍五入到 3 位：float 存回来的 0.3499999940395355 这种残渣不该写进 JSON
    tickInterval: Math.round((Number(doc.tickInterval) || 0.35) * 1000) / 1000,
    // 墙类型原样保留（空串 = 交给 Core 用目录里第一份墙类型，别替作者改主意）
    wallType: typeof doc.wallType === 'string' ? doc.wallType : DEFAULT_WALL_TYPE,
  };

  // 有墙就写满 height 行（和 LevelDocumentWriter 一致：第 1 行是 y=0，短行按空处理）
  const rows = (doc.wallRows || []).slice(0, doc.height);
  const anyWall = rows.some((row) => (row || '').indexOf('#') >= 0);

  if (anyWall) {
    out.wallRows = [];
    for (let y = 0; y < doc.height; y++) {
      out.wallRows.push((rows[y] || '').padEnd(doc.width, '.').slice(0, doc.width));
    }
  }

  const cells = doc.elements.slice().sort((a, b) => (a.y - b.y) || (a.x - b.x));
  out.elements = cells.map((cell) => {
    const info = kindInfo(cell.kind);
    const one = { kind: cell.kind, x: cell.x, y: cell.y };

    // type 模式：写回 type + rot（引擎按「资产朝向 + rot」定位），dir 只作人类可读的参照
    if (cell.type) {
      one.type = cell.type;
      one.rot = ((Number(cell.rot) || 0) % 4 + 4) % 4;
    }

    if (info.params.indexOf('dir') >= 0) one.dir = cell.dir || 'Right';
    if (info.params.indexOf('dir2') >= 0) one.dir2 = cell.dir2 || 'Down';
    if (info.params.indexOf('sequence') >= 0) one.sequence = cell.sequence || '1';
    if (info.params.indexOf('count') >= 0) one.count = Math.max(0, Number(cell.count) || 0);
    if (info.params.indexOf('face') >= 0) one.face = cell.face === 'in' ? 'in' : 'any';
    if (info.params.indexOf('pass') >= 0) one.pass = cell.pass === 0 ? 0 : 1;
    if (info.params.indexOf('delay') >= 0) one.delay = clampDelay(cell.delay);

    // 总是写：和 LevelDocumentWriter 一样，false 也要落盘（缺省值靠人记是件危险的事）
    one.playerEditable = !!cell.playerEditable;

    return one;
  });

  const supplies = (doc.supplies || []).filter((item) => item && item.count >= 1);
  if (supplies.length) {
    out.supplies = supplies.map((item) => {
      const one = { kind: item.kind };
      if (item.type) one.type = item.type;
      one.count = Math.max(1, Number(item.count) || 1);
      return one;
    });
  }

  // Core 的硬规则：有手牌就必须允许玩家动手（发得出牌就得放得下）
  out.allowBuild = supplies.length > 0 ? true : !!doc.allowBuild;

  return out;
}

/**
 * 读进来的关卡统一展开成「每格参数都在手里」的形态。
 *
 * - type 模式（Unity 导出）**保留 type + rot**：存盘时原样写回，资产绑定不会丢；
 *   朝向按「类型目录里的基准朝向 + rot」算出来给人看。
 * - 认不出的种类 / 发不出手牌的种类不静默吞掉，记进 state.notes 报到界面上。
 */
function fromDocument(input) {
  const doc = newDoc(Number(input.width) || 9, Number(input.height) || 8);
  // 没写 displayName 的老关卡不硬塞一个 'CUSTOM LEVEL'：调用方（openLevel）会在知道文件名后补上它。
  doc.displayName = input.displayName || '';
  doc.tickInterval = Number(input.tickInterval) || 0.35;
  doc.allowBuild = !!input.allowBuild;
  doc.wallType = typeof input.wallType === 'string' ? input.wallType : DEFAULT_WALL_TYPE;

  // wallRows 优先；只有 wallMap 的老关卡（docs/LEVEL_JSON.md 的兼容写法）按 \n 拆行
  const rows = Array.isArray(input.wallRows) ? input.wallRows : null;
  if (rows && rows.length) {
    doc.wallRows = rows.slice(0, doc.height);
  } else if (typeof input.wallMap === 'string' && input.wallMap.length) {
    doc.wallRows = input.wallMap.split(/\r?\n/).slice(0, doc.height);
  }

  doc.supplies = normalizeSupplies(input.supplies);

  for (const raw of (input.elements || [])) {
    if (!raw) continue;

    const kind = raw.kind || kindOfType(raw.type);
    if (!kind || !KINDS.some((k) => k.kind === kind)) {
      state.notes.push('(' + raw.x + ',' + raw.y + ') 种类认不出'
        + '（kind=' + (raw.kind || '') + ' type=' + (raw.type || '') + '），这一格已跳过');
      continue;
    }

    if (kind === 'Wall') {
      setWallIn(doc, raw.x, raw.y, true);
      continue;
    }

    const spec = raw.type ? (state.catalog || []).find((t) => t.key === raw.type) : null;
    const base = spec || defaultsFor(kind);
    const rot = ((Number(raw.rot) || 0) % 4 + 4) % 4;

    const cell = {
      kind: kind,
      x: raw.x,
      y: raw.y,
      type: raw.type || '',
      rot: rot,
      dir: raw.dir || rotateBy(base.dir || 'Right', rot),
      dir2: raw.dir2 || rotateBy(base.dir2 || 'Down', rot),
      sequence: raw.sequence || base.sequence || '1',
      count: Number(raw.count) >= 0 ? Number(raw.count) : (base.count >= 0 ? base.count : 1),
      face: raw.face ? (raw.face === 'in' ? 'in' : 'any') : (base.acceptAnyFace === false ? 'in' : 'any'),
      pass: Number(raw.pass) >= 0 ? Number(raw.pass) : (base.pass === 0 ? 0 : 1),
      delay: Number(raw.delay) >= 0 ? clampDelay(raw.delay) : clampDelay(base.delay),
      playerEditable: !!raw.playerEditable,
    };

    doc.elements.push(cell);
  }

  return doc;
}

/**
 * 手牌归一化：只认白名单里的种类（发射器/接收器是出题权，不发），
 * 张数缺省 1、0 张直接丢掉（和 Core 的 LevelJsonSerializer.ReadSupplies 同义）。
 * 认不出的条目报进 state.notes，不静默吞。
 */
function normalizeSupplies(raw) {
  const out = [];

  for (const item of (raw || [])) {
    if (!item) continue;

    const kind = item.kind || kindOfType(item.type);
    if (!kind || SUPPLY_KINDS.indexOf(kind) < 0) {
      state.notes.push('手牌 ' + JSON.stringify(item) + ' 不是可发牌的种类，已跳过');
      continue;
    }

    const count = Number(item.count) >= 0 ? Number(item.count) : 1;
    if (count <= 0) {
      state.notes.push('手牌 ' + kind + ' 的张数是 0，已跳过');
      continue;
    }

    const one = { kind: kind, count: count };
    if (item.type) one.type = item.type;
    out.push(one);
  }

  return out;
}

/** 按 type 取类型目录里的一条；目录还没导出（或这个 type 不认识）时返回 null。 */
function specOf(type) {
  if (!type) return null;
  return (state.catalog || []).find((t) => t.key === type) || null;
}

function kindOfType(type) {
  const spec = specOf(type);
  return spec ? spec.kind : null;
}

function setWallIn(doc, x, y, on) {
  if (!doc.wallRows) doc.wallRows = [];
  for (let row = 0; row <= y; row++) {
    if (!doc.wallRows[row]) doc.wallRows[row] = '';
    if (doc.wallRows[row].length < doc.width) doc.wallRows[row] = doc.wallRows[row].padEnd(doc.width, '.');
  }
  const line = doc.wallRows[y];
  doc.wallRows[y] = line.substring(0, x) + (on ? '#' : '.') + line.substring(x + 1);
}

// ------------------------------------------------------------------ 手牌

/* 目录里「能发牌」的类型资产（Filter 的 F0/F1、DelayGate 的 D1~D4 各算一条）。 */
function supplySpecs() {
  return (state.catalog || []).filter((spec) => spec && SUPPLY_KINDS.indexOf(spec.kind) >= 0);
}

/* 变体说明：光看 F0/F1 分不出谁放行 0，光看 D3 也想不起来要等几拍。 */
function supplySpecDetail(spec) {
  if (spec.kind === 'Filter') return '（PASS ' + (spec.pass === 0 ? 0 : 1) + '）';
  if (spec.kind === 'DelayGate') return '（HOLD ' + clampDelay(spec.delay) + ' TICK）';
  return '';
}

/* 下拉里的一行：带种类字形，省地方。 */
function supplySpecText(spec) {
  return kindInfo(spec.kind).glyph + ' ' + spec.label + supplySpecDetail(spec);
}

/* 手牌列表里的一行：写全「过滤器 F1（PASS 1）」，一眼能对上玩家看到的牌面。 */
function supplySpecName(spec) {
  return kindInfo(spec.kind).label + ' ' + spec.label + supplySpecDetail(spec);
}

/**
 * 手牌下拉：列**具体类型资产**，不是一个种类一行。
 *
 * 为什么不能只发种类：手牌存的是 type（LevelSupply.type 只有一个 type + count 通道），
 * Unity 侧按这个资产 CreateEditable。只写 kind 的话 F1 / D2~D4 永远发不出来 ——
 * LevelTypeRegistryResolver.MatchByKind 只比 PrimaryDir，同 kind 的变体永远落到第一个资产
 * （Filter→F0、DelayGate→D1）。
 *
 * 类型目录是 Unity 菜单「导出类型目录」拍的快照；还没导出时退回按种类发牌，行为不比原来差。
 */
function renderSupplyOptions() {
  if (!el.supplyKind) return;

  el.supplyKind.innerHTML = '';
  const specs = supplySpecs();

  if (!specs.length) {
    for (const kind of SUPPLY_KINDS) {
      const info = kindInfo(kind);
      const option = document.createElement('option');
      option.value = 'kind:' + kind;
      option.textContent = info.glyph + ' ' + info.label + '（未导出类型目录）';
      el.supplyKind.appendChild(option);
    }
    return;
  }

  for (const kind of SUPPLY_KINDS) {
    const group = specs.filter((spec) => spec.kind === kind);
    if (!group.length) continue;

    const box = document.createElement('optgroup');
    box.label = kindInfo(kind).label;

    for (const spec of group) {
      const option = document.createElement('option');
      option.value = spec.key;
      option.textContent = supplySpecText(spec);
      box.appendChild(option);
    }

    el.supplyKind.appendChild(box);
  }
}

/**
 * 有手牌就必须允许玩家动手。
 *
 * Core 侧（LevelDocumentBuilder）见到非空 supplies 会直接把 AllowBuild 顶成 true，
 * 编辑器这边跟着加锁：不然界面上勾着「不允许」，存盘却变成允许，作者一脸问号。
 */
function syncAllowBuildLock() {
  const has = (state.doc.supplies || []).length > 0;

  if (has) state.doc.allowBuild = true;
  el.allowBuild.checked = !!state.doc.allowBuild;
  el.allowBuild.disabled = has;
  if (el.allowBuildNote) el.allowBuildNote.textContent = has ? '（有手牌 ⇒ 强制开启）' : '';
}

function renderSupplies() {
  if (!el.supplyList) return;

  el.supplyList.innerHTML = '';
  const supplies = state.doc.supplies || [];

  if (!supplies.length) {
    el.supplyList.innerHTML = '<li class="dim">没有手牌：玩家只能玩你摆好的样子。</li>';
    return;
  }

  supplies.forEach((item, index) => {
    const li = document.createElement('li');

    const name = document.createElement('span');
    name.className = 'name';
    const spec = specOf(item.type);
    name.textContent = spec
      ? supplySpecName(spec)
      : kindInfo(item.kind).label + (item.type ? ' · ' + item.type : '');
    name.title = item.type || item.kind;

    const count = document.createElement('input');
    count.type = 'number';
    count.min = '1';
    count.max = '99';
    count.value = String(item.count);
    // 只改数值不重绘：重绘会把正在输入的框换掉，光标就飞了
    count.oninput = () => {
      beginHistory('手牌');
      item.count = Math.max(1, parseInt(count.value, 10) || 1);
    };
    count.onchange = () => afterEdit();

    const remove = document.createElement('button');
    remove.className = 'danger';
    remove.textContent = '×';
    remove.title = '拿走这张牌';
    remove.onclick = () => {
      beginHistory('手牌');
      state.doc.supplies.splice(index, 1);
      afterEdit();
    };

    li.appendChild(name);
    li.appendChild(count);
    li.appendChild(remove);
    el.supplyList.appendChild(li);
  });
}

function addSupply() {
  const value = el.supplyKind.value;
  const count = Math.max(1, parseInt(el.supplyCount.value, 10) || 1);

  // 下拉的值是类型 key（F1 / D3…）；没导出目录时会退成 'kind:Wall' 这种写法
  const spec = specOf(value);
  const kind = spec ? spec.kind : (value.indexOf('kind:') === 0 ? value.slice(5) : value);
  if (!kind || SUPPLY_KINDS.indexOf(kind) < 0) return;

  beginHistory('手牌');
  if (!state.doc.supplies) state.doc.supplies = [];

  // 同一条手牌直接累加：列表里出现两行「墙」，看着像 bug。
  // 绑了类型的按 type 判等（F0 和 F1 是两条不同的牌），没绑的仍按种类。
  const same = state.doc.supplies.find((item) => (spec ? item.type === spec.key : (item.kind === kind && !item.type)));
  if (same) same.count += count;
  else state.doc.supplies.push(spec ? { type: spec.key, kind: spec.kind, count: count } : { kind: kind, count: count });

  afterEdit();
}

// ------------------------------------------------------------------ 跟本地服务说话

async function apiGet(path) {
  const response = await fetch(path);
  return response.json();
}

async function apiPost(path, body) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  return response.json();
}

function setStatus(text, ok) {
  el.status.textContent = text || '';
  el.status.className = 'status' + (ok === true ? ' ok' : ok === false ? ' bad' : '');
}

async function loadCatalog() {
  try {
    const catalog = await apiGet('/api/catalog');
    state.catalog = (catalog && catalog.types) || [];
  } catch (error) {
    state.catalog = [];
  }
}

// ------------------------------------------------------------------ 文件

async function loadFiles() {
  el.fileList.innerHTML = '<li class="dim">读取中…</li>';

  try {
    const listing = await apiGet('/api/levels?where=' + encodeURIComponent(state.where));
    el.fileList.innerHTML = '';
    state.files = listing.files || [];

    if (listing.missing) {
      el.fileList.innerHTML = '<li class="dim">这个目录还不存在（点「打开目录」会顺手建出来）</li>';
      return;
    }

    if (!listing.files.length) {
      el.fileList.innerHTML = '<li class="dim">这个目录里没有 .json</li>';
      return;
    }

    for (const file of listing.files) {
      const item = document.createElement('li');

      // 名字是身份，文件名是它的派生。主行写名字，副行写真实文件名 ——
      // 对不上时（手工放进来 / 旧编辑器存的）副行变黄，这是唯一能看见「同一关两个名字」的地方。
      const shown = sanitizeName(file.displayName);
      const drifted = !!shown && baseNameOf(shown) !== file.name;

      const sub = drifted
        ? '<span class="sub warn">与文件名不符：' + escapeHtml(file.file) + '</span>'
        : '<span class="sub">' + escapeHtml(file.file) + ' · ' + (file.size / 1024).toFixed(1) + ' KB</span>';

      item.innerHTML = '<span class="name"><span class="file">' + escapeHtml(shown || file.name) + '</span>' + sub + '</span>'
        + '<span class="when">' + file.modified + '</span>';
      item.onclick = () => openLevel(file.name);
      el.fileList.appendChild(item);
    }
  } catch (error) {
    el.fileList.innerHTML = '<li class="bad">列目录失败：' + error.message + '</li>';
  }
}

async function openLevel(name) {
  setStatus('读 ' + name + ' …');
  try {
    const raw = await apiGet('/api/level?where=' + encodeURIComponent(state.where) + '&name=' + encodeURIComponent(name));
    if (raw && raw.error) {
      setStatus(raw.error, false);
      return;
    }

    state.notes = [];
    state.doc = fromDocument(raw);
    state.fileName = name;
    // 老文件没写 displayName：用文件名当标题（Unity 侧也是这么兜底的），别留一个空标题给玩家看。
    if (!state.doc.displayName) state.doc.displayName = String(name).replace(/_/g, ' ');
    state.selectedKey = null;
    resetHistory();
    syncMetaInputs();
    afterEdit();
    el.jsonBox.value = JSON.stringify(serializeDoc(), null, 2);

    const suffix = showNotes();

    // 名字是身份，文件名是派生。手工命名或旧编辑器存出来的文件两者常常对不上 ——
    // 打开时不偷偷改名（改名是保存那一刻的事），但必须现在就说，否则作者会以为它已经叫这个了。
    const expected = baseNameOf(state.doc.displayName);
    const nameNote = expected === name
      ? ''
      : '；这份文件叫 ' + name + '.json，按名字「' + state.doc.displayName + '」该叫 '
        + expected + '.json（保存时会改名）';

    setStatus('已读入 ' + name + '（' + state.doc.elements.length + ' 个元件）' + suffix + nameNote, suffix === '');
  } catch (error) {
    setStatus('读失败：' + error.message, false);
  }
}

// ------------------------------------------------------------------ 自测

/* 检查面板的常驻脚注。
   「玩家拿着手牌到底能不能解出来」需要求解器或者人试玩，这个面板答不了。
   与其让作者从一个框的颜色里猜，不如每次都把这句话明说出来。 */
const CHECK_CAVEAT = '能不能真的过，只能进游戏、按玩家身份试 —— 这里只查必要条件，不解题。';

/**
 * 检查 = 只陈述引擎真能证明的三件事，不下「这关能不能过」的结论：
 *
 *   1. 引擎收不收这份 JSON       —— 构建问题 / 接口报错，红
 *   2. 当前布局当固定关卡跑一遍   —— 事实陈述，无褒贬
 *   3. 存档前置条件（六条）       —— 必要条件，不是充分条件，黄
 *
 * 「有手牌的关卡，预设布局跑不通」是设计意图，不是缺陷；反过来
 * 「有手牌、预设却自己就通了」才说明手牌是摆设 —— 那由 RunForSave 判定，
 * 面板只负责如实转述，不替作者下结论。
 */
async function runCheck() {
  el.checkResult.className = 'result dim';
  el.checkResult.textContent = '跑引擎…';
  el.problems.textContent = '';

  try {
    const result = await apiPost('/api/check', serializeDoc());

    if (result.error) {
      el.checkResult.className = 'result fail';
      el.checkResult.textContent = result.error;
      return;
    }

    const buildProblems = result.problems || [];
    const saveProblems = result.saveProblems || [];
    // 有没有手牌以后端为准（definition.HasSupplies）：前端自己数 doc.supplies
    // 会把 count 为 0 的残留条目也算进去，那句「有手牌，跑不通才正常」就会说错。
    const hasSupplies = !!result.hasSupplies;

    // report 首行是「✅ 通关 12 ticks」这种引擎结论，直接接在「预设布局」后面；
    // 剩下的行（每个接收器收了几位、第一处不匹配在哪）原样留着，作者要靠它定位。
    const reportLines = String(result.report || '').split('\n');
    const headline = reportLines.shift() || '';
    const detail = reportLines.join('\n').trim();

    const verdict = '预设布局（当成固定关卡跑一遍）：' + headline
      + (hasSupplies && !result.pass ? '  —— 有手牌，跑不通才是正常' : '');

    const saveLine = saveProblems.length === 0
      ? '存档前置检查：没有发现硬伤'
      : '存档前置检查：' + saveProblems.length + ' 条没过（清单在网格下方）';

    el.checkResult.textContent = [verdict, detail, saveLine, CHECK_CAVEAT]
      .filter((line) => line !== '')
      .join('\n');

    // 红只留给「引擎读不了 / 读不全这份 JSON」。
    // 存档前置没过用黄：那也可能是作者还没改完的草稿，不该一票否决。
    el.checkResult.className = 'result '
      + (buildProblems.length ? 'fail' : (saveProblems.length ? 'warn' : 'dim'));

    const notes = [];
    if (buildProblems.length) notes.push('构建问题：\n' + buildProblems.map((p) => '· ' + p).join('\n'));
    if (saveProblems.length) notes.push('存档前置检查：\n' + saveProblems.map((p) => '· ' + p).join('\n'));
    el.problems.textContent = notes.join('\n\n');
  } catch (error) {
    el.checkResult.className = 'result fail';
    el.checkResult.textContent = '连不上本地服务：' + error.message;
  }
}

// ------------------------------------------------------------------ 顶部信息栏

/**
 * 尺寸下拉的内容。
 * 正常只有游戏那四档；读进来的文件若不是四档之一（手工改过的 json、或游戏以后加了档位），
 * 临时补一项「导入的自定义尺寸」并选中 —— 不静默改人的数据，也不让作者能手填第 5 种。
 */
function renderSizeOptions() {
  const current = state.doc.width + 'x' + state.doc.height;

  el.size.innerHTML = SIZE_PRESETS.map((preset) => {
    const value = preset.width + 'x' + preset.height;
    const text = preset.width + ' x ' + preset.height + (preset.standard ? '（主线标准板）' : '');
    return '<option value="' + value + '">' + text + '</option>';
  }).join('');

  const known = SIZE_PRESETS.some((p) => p.width + 'x' + p.height === current);
  if (!known) {
    const option = document.createElement('option');
    option.value = current;
    option.textContent = state.doc.width + ' x ' + state.doc.height + '（导入的自定义尺寸）';
    el.size.appendChild(option);
  }

  // 永远对齐到文档本身的尺寸：撤销 / 导入之后下拉不能还停在旧档位
  el.size.value = current;
}

function syncMetaInputs() {
  el.displayName.value = state.doc.displayName || '';
  renderSizeOptions();
  el.tickInterval.value = state.doc.tickInterval;
  el.allowBuild.checked = !!state.doc.allowBuild;

  renderSupplies();
  syncAllowBuildLock();
  renderSavePreview();
}

/** 读入时攒下来的问题（认不出的种类、被跳过的怪手牌）要摆到台面上，不能安静吞。 */
function showNotes() {
  el.problems.textContent = state.notes.join('\n');
  return state.notes.length ? '，有 ' + state.notes.length + ' 处需要看一下（见网格下方）' : '';
}

/**
 * 把一份 json 文本读进编辑器。文本框粘贴、选文件、拖文件三条路都汇到这里 ——
 * 读入路径只有一份，不会出现「粘进来能过、拖进来过不了」的漂移。
 * 读入后不继承上一份的身份（fileName=null），保存时按新名字落盘，不会把刚打开的那一关按新名字搬走。
 */
function adoptJsonText(text, source) {
  // 记事本存的 utf-8 带 BOM，JSON.parse 会在第一个字符上直接抛错
  const clean = String(text == null ? '' : text).replace(/^\uFEFF/, '');
  el.jsonBox.value = clean;

  try {
    const parsed = JSON.parse(clean);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
      throw new Error('最外面必须是一层 { }');
    }

    state.notes = [];
    state.doc = fromDocument(parsed);
    state.fileName = null;
    state.selectedKey = null;
    resetHistory();
    syncMetaInputs();
    afterEdit();

    const suffix = showNotes();
    const hint = state.doc.displayName
      ? '；按「保存」会存成 ' + fileNameOf(state.doc.displayName)
      : '；这份没写 displayName，保存前先起个名';
    setStatus('已读入 ' + source + '（' + state.doc.elements.length + ' 个元件）' + suffix + hint, suffix === '');
  } catch (error) {
    setStatus('JSON 读不出来：' + error.message, false);
  }
}

/** 磁盘上的一个文件 → 文本 → adoptJsonText。错在门口拦住，别让它掉进解析里报一句看不懂的话。 */
function readJsonFile(file) {
  if (!file) return;

  if (!/\.json$/i.test(file.name)) {
    setStatus('只认 .json 文件：' + file.name, false);
    return;
  }

  if (file.size > 2 * 1024 * 1024) {
    setStatus('文件太大了（' + Math.round(file.size / 1024) + ' KB），关卡 json 通常只有几 KB', false);
    return;
  }

  const reader = new FileReader();
  reader.onerror = () => setStatus('读不了这个文件：' + file.name, false);
  reader.onload = () => adoptJsonText(String(reader.result || ''), file.name);
  reader.readAsText(file, 'utf-8');
}

/** 拖过来的是不是文件。（拖选一段文字也会触发 dragover，不能一概 preventDefault。） */
function dragHasFiles(event) {
  const types = event.dataTransfer && event.dataTransfer.types;
  return !!types && Array.prototype.indexOf.call(types, 'Files') >= 0;
}

/** 从下拉的 "9x8" 里拆出宽高；读不懂就保持原尺寸。 */
function parseSize(value) {
  const parts = String(value || '').split('x');
  const width = parseInt(parts[0], 10);
  const height = parseInt(parts[1], 10);
  if (!Number.isFinite(width) || !Number.isFinite(height) || width < 1 || height < 1) return null;
  return { width: width, height: height };
}

/**
 * 改元信息（关卡名 / tick / 尺寸 / allowBuild）。
 * 尺寸只能从 SIZE_PRESETS 里选（游戏里玩家只有这四档），所以这里不再夹数，只认下拉的值。
 * 变小时越界的格子会被丢掉 —— 但不留「看不见但还在」的元件，且 Ctrl+Z 能整体撤回。
 */
function applyMeta() {
  // 预览跟着框里实际打的字走，所以放在早退检查之前 ——
  // 打了一串中文（名字其实没变）也得当场说这些字会被丢掉。
  renderSavePreview();

  // 清空名字框不再拿文件名顶替 —— 名字是身份，空名字该被拦住（保存会拒），
  // 而不是悄悄变成文件名里的那串字符。
  const displayName = el.displayName.value.trim() || LEVEL_NAMING.fallback;
  const tickInterval = Math.min(5, Math.max(0.05, Number(el.tickInterval.value) || 0.35));
  const allowBuild = el.allowBuild.checked;
  const size = parseSize(el.size.value) || { width: state.doc.width, height: state.doc.height };

  // 什么都没动就别记一步：否则「点一下下拉、选的还是原来那档」会留一格空撤销，
  // 按 Ctrl+Z 看着像没反应。
  const sameMeta = state.doc.displayName === displayName
    && Number(state.doc.tickInterval) === tickInterval
    && !!state.doc.allowBuild === allowBuild;
  const sameSize = state.doc.width === size.width && state.doc.height === size.height;
  if (sameMeta && sameSize) return;

  beginHistory('关卡信息');

  state.doc.displayName = displayName;
  state.doc.tickInterval = tickInterval;
  state.doc.allowBuild = allowBuild;

  const width = size.width;
  const height = size.height;

  if (width !== state.doc.width || height !== state.doc.height) {
    const before = state.doc.elements.length;

    state.doc.width = width;
    state.doc.height = height;
    state.doc.elements = state.doc.elements.filter((cell) => cell.x < width && cell.y < height);

    if (state.doc.wallRows && state.doc.wallRows.length > height) {
      state.doc.wallRows.length = height;
    }

    const dropped = before - state.doc.elements.length;
    if (dropped > 0) setStatus('缩小网格后丢掉了 ' + dropped + ' 个越界元件（Ctrl+Z 可撤回）', false);
    else setStatus('尺寸改成 ' + width + ' x ' + height, true);

    if (state.selectedKey) {
      const parts = state.selectedKey.split(',');
      if (Number(parts[0]) >= width || Number(parts[1]) >= height) state.selectedKey = null;
    }

    renderGrid();
    renderInspector();
  }
}

function interactionTargetIsField(target) {
  if (!target) return false;
  const tag = target.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
}

// ------------------------------------------------------------------ 事件绑定

function bindUi() {
  el.displayName.oninput = applyMeta;
  el.tickInterval.oninput = applyMeta;
  el.allowBuild.onchange = applyMeta;
  el.size.onchange = applyMeta;

  el.btnUndo.onclick = undo;
  el.btnRedo.onclick = redo;
  updateHistoryButtons();

  document.getElementById('btnAddSupply').onclick = addSupply;
  el.supplyCount.onkeydown = (event) => {
    if (event.key === 'Enter') {
      addSupply();
      event.preventDefault();
    }
  };

  el.where.onchange = () => {
    state.where = el.where.value;
    loadFiles();
  };

  document.getElementById('btnRotate').onclick = () => {
    const cell = selectedCell();
    if (!cell) return;
    rotateCell(cell);
    afterEdit();
  };

  document.getElementById('btnErase').onclick = () => {
    if (!state.selectedKey) return;
    const parts = state.selectedKey.split(',');
    eraseAt(Number(parts[0]), Number(parts[1]));
  };

  document.getElementById('btnClear').onclick = () => {
    if (!window.confirm('清空所有元件和墙？')) return;
    beginHistory('清空');
    state.doc.elements = [];
    state.doc.wallRows = [];
    state.selectedKey = null;
    afterEdit();
  };

  document.getElementById('btnCheck').onclick = runCheck;

  document.getElementById('btnExport').onclick = () => {
    el.jsonBox.value = JSON.stringify(serializeDoc(), null, 2);
    setStatus('已导出到下面的文本框', true);
  };

  document.getElementById('btnCopy').onclick = async () => {
    if (!el.jsonBox.value.trim()) el.jsonBox.value = JSON.stringify(serializeDoc(), null, 2);

    try {
      await navigator.clipboard.writeText(el.jsonBox.value);
      setStatus('已复制到剪贴板', true);
    } catch (error) {
      el.jsonBox.select();
      document.execCommand('copy');
      setStatus('已复制（兼容模式）', true);
    }
  };

  document.getElementById('btnDownload').onclick = () => {
    if (!el.jsonBox.value.trim()) el.jsonBox.value = JSON.stringify(serializeDoc(), null, 2);

    const blob = new Blob([el.jsonBox.value], { type: 'application/json' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    // 导出名也走名字派生：它必须和落盘的文件名一模一样，
    // 否则「下载 → 再导入」就变成了另一关。
    link.download = fileNameOf(state.doc.displayName);
    link.click();
    URL.revokeObjectURL(link.href);
  };

  document.getElementById('btnImport').onclick = () => adoptJsonText(el.jsonBox.value, '文本框');

  document.getElementById('btnPickFile').onclick = () => el.filePicker.click();

  el.filePicker.onchange = () => {
    const file = el.filePicker.files && el.filePicker.files[0];
    // 不清空的话，连着选同一个文件第二次不会再触发 change
    el.filePicker.value = '';
    readJsonFile(file);
  };

  /* 整页都能接文件。顺带堵掉一个隐性事故：现在把 json 拖到页面上，浏览器会直接导航过去，
     没保存的编辑全没了 —— 所以「拖的是文件」时必须 preventDefault。 */
  let dragDepth = 0;
  window.addEventListener('dragenter', (event) => {
    if (!dragHasFiles(event)) return;
    dragDepth += 1;
    el.dropHint.classList.add('on');
  });
  window.addEventListener('dragover', (event) => {
    if (!dragHasFiles(event)) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
  });
  window.addEventListener('dragleave', (event) => {
    if (!dragHasFiles(event)) return;
    dragDepth = Math.max(0, dragDepth - 1);
    if (dragDepth === 0) el.dropHint.classList.remove('on');
  });
  window.addEventListener('drop', (event) => {
    if (!dragHasFiles(event)) return;
    event.preventDefault();
    dragDepth = 0;
    el.dropHint.classList.remove('on');
    readJsonFile(event.dataTransfer.files && event.dataTransfer.files[0]);
  });

  document.getElementById('btnSave').onclick = saveLevel;
  document.getElementById('btnRefresh').onclick = loadFiles;

  document.getElementById('btnOpenFolder').onclick = async () => {
    const result = await apiPost('/api/open-folder', { where: state.where });
    setStatus(result.ok ? '已打开：' + result.directory : String(result.error || '打不开这个目录'), result.ok);
  };

  window.addEventListener('keydown', (event) => {
    // 文本框里的 Ctrl+Z 交给浏览器撤销文字，不该动关卡
    if (interactionTargetIsField(event.target)) return;

    if ((event.ctrlKey || event.metaKey) && (event.key === 'z' || event.key === 'Z')) {
      if (event.shiftKey) redo();
      else undo();
      event.preventDefault();
      return;
    }

    if ((event.ctrlKey || event.metaKey) && (event.key === 'y' || event.key === 'Y')) {
      redo();
      event.preventDefault();
      return;
    }

    if (event.key === 'r' || event.key === 'R') {
      const cell = selectedCell();
      if (cell) {
        rotateCell(cell);
        afterEdit();
      }
      event.preventDefault();
      return;
    }

    if (event.key === 'Delete' || event.key === 'Backspace') {
      if (!state.selectedKey) return;
      const parts = state.selectedKey.split(',');
      eraseAt(Number(parts[0]), Number(parts[1]));
      event.preventDefault();
      return;
    }

  });
}

async function saveLevel() {
  // 名字是唯一身份，文件名由它派生（Core/LevelNaming.cs）。客户端不再能指定文件名 ——
  // 能指定，就会长出「文件叫 MY_LEVEL_02_01、游戏里这关叫 111」那种同一关两个名字。
  const displayName = sanitizeName(state.doc.displayName);
  if (!displayName) {
    setStatus('关卡名不能为空：只能用 A-Z、0-9、- _ 和空格，最多 '
      + LEVEL_NAMING.maxLength + ' 个字（游戏里的像素字体没有中文字形）', false);
    el.displayName.focus();
    return;
  }

  const json = JSON.stringify(serializeDoc(), null, 2);

  // oldName 只用来「改名」：打开着某个文件时改了名字，就把它搬过去，而不是再写一份新的。
  // 没有打开文件（新文档 / 从文本框导入）就是空串，后端据此刻意不碰任何已有文件。
  const oldName = state.fileName || '';

  setStatus('保存中…');
  try {
    const result = await apiPost('/api/save', { where: state.where, oldName: oldName, json: json });

    if (!result.ok) {
      setStatus(String(result.error || '保存失败'), false);
      return;
    }

    state.fileName = result.name;
    el.jsonBox.value = json;

    // 「已改名 / 已覆盖 / 新写入」是三件不同的事，别糊成一句「已保存」。
    // 改名尤其要说 —— 磁盘上少了一个文件，作者得知道那是这里做掉的。
    const note = result.renamedFrom
      ? '已改名：' + result.renamedFrom + '.json → ' + result.name + '.json'
      : (result.existed ? '已覆盖：' : '新写入：') + result.file;

    // 存得下 ≠ 做完了。文件确实写进去了，所以不是失败（中性色，不是红）——
    // 但「这关还差几条」不能因为作者没点「跑一遍真引擎」就消失，否则清单只活在
    // 那个要主动点的按钮后面。
    const saveProblems = result.saveProblems || [];
    const buildProblems = result.problems || [];

    // 面板整个重铺，和 runCheck 一个形状：只叠加不清空的话，上一次留下的
    // 「这一关没有接收器」会活到这一次已经补上之后，变成一句骗人的黄字。
    const notes = [];
    if (buildProblems.length) notes.push('构建问题：\n' + buildProblems.map((p) => '· ' + p).join('\n'));
    if (saveProblems.length) notes.push('存档前置检查：\n' + saveProblems.map((p) => '· ' + p).join('\n'));
    el.problems.textContent = notes.join('\n\n');

    if (saveProblems.length) {
      setStatus(note + '　/　这关还差 ' + saveProblems.length + ' 条，清单在网格下方');
    } else {
      setStatus(note, true);
    }

    await loadFiles();
    renderSavePreview();
  } catch (error) {
    setStatus('保存失败：' + error.message, false);
  }
}

// ------------------------------------------------------------------ 启动

async function boot() {
  await loadCatalog();

  // 先取一次文件列表：新文档的默认标题要看目录里已经有哪些名字，别再添一个 CUSTOM LEVEL。
  // loadFiles 只写 el.fileList 和 state.files，不依赖 bindUi 里的绑定，提前调用是安全的。
  await loadFiles();

  state.doc = newDoc(SIZE_PRESETS[0].width, SIZE_PRESETS[0].height);
  state.doc.displayName = nextDocName();
  renderSupplyOptions();
  syncMetaInputs();
  renderPalette();
  renderGrid();
  renderInspector();
  bindUi();
  resetHistory();

  try {
    const info = await apiGet('/api/state');
    el.pathInfo.textContent = '工程：' + info.projectRoot
      + '\n玩家关卡目录：' + (info.playerDir || '未知')
      + (info.hasCatalog ? '' : '\n（还没导出类型目录：Unity 菜单 SHIFT GO/导出类型目录 之后，打开 Unity 导出的关才认得 type）');
  } catch (error) {
    el.pathInfo.textContent = '连不上本地服务：' + error.message;
  }

  setStatus('左键放元件，右键擦。点格子后右边改参数。');
}

boot();





