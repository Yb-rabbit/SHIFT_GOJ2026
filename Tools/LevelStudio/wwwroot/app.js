/* BitFlow 关卡编辑器前端。
   没有任何构建步骤：这个文件由本地 http 服务直接发给浏览器。
   校验一律走后端（/api/check），跑的是 Unity 里同一份 BitFlowSimulation ——
   前端只负责画格子、改参数、拼 JSON。 */

const KINDS = [
  { kind: 'Emitter',  glyph: 'E', label: '发射器', params: ['dir', 'sequence', 'count'] },
  { kind: 'Receiver', glyph: 'R', label: '接收器', params: ['dir', 'sequence', 'count', 'face'] },
  { kind: 'Splitter', glyph: 'L', label: '分流器', params: ['dir', 'dir2'] },
  { kind: 'NotGate',  glyph: 'N', label: '非门',   params: ['dir'] },
  { kind: 'Filter',   glyph: 'F', label: '过滤器', params: ['dir', 'pass'] },
  { kind: 'Wall',     glyph: '#', label: '墙',     params: [] },
];

const DIRS = ['Up', 'Right', 'Down', 'Left'];
const DIR_MARK = { Up: '↑', Right: '→', Down: '↓', Left: '←' };
const DIR_CN = { Up: '上', Right: '右', Down: '下', Left: '左' };

const state = {
  doc: newDoc(12, 9),
  brush: 'Emitter',
  selectedKey: null,
  catalog: [],
  where: 'incoming',
};

const el = {
  displayName: document.getElementById('displayName'),
  width: document.getElementById('width'),
  height: document.getElementById('height'),
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
  saveName: document.getElementById('saveName'),
  jsonBox: document.getElementById('jsonBox'),
  status: document.getElementById('status'),
};

// ------------------------------------------------------------------ 数据

function newDoc(width, height) {
  return {
    schema: 2,
    displayName: 'CUSTOM LEVEL',
    width: width,
    height: height,
    tickInterval: 0.35,
    wallRows: [],
    elements: [],
    supplies: [],
    allowBuild: false,
  };
}

function kindInfo(kind) {
  return KINDS.find((k) => k.kind === kind) || KINDS[0];
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
    };
  }

  switch (kind) {
    case 'Receiver': return { dir: 'Down', dir2: 'Down', sequence: '1', count: 1, face: 'any', pass: 1 };
    case 'Splitter': return { dir: 'Up', dir2: 'Right', sequence: '1', count: 1, face: 'any', pass: 1 };
    default: return { dir: 'Right', dir2: 'Down', sequence: '1', count: 1, face: 'any', pass: 1 };
  }
}

function newCell(kind, x, y) {
  const cell = { kind: kind, x: x, y: y, ...defaultsFor(kind), playerEditable: false };
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
  return parts.join(' · ');
}

// ------------------------------------------------------------------ 绘制操作

function paintAt(x, y) {
  state.selectedKey = coordKey(x, y);

  if (state.brush === 'Wall') {
    const existing = cellAt(x, y);
    if (existing) removeCell(existing);
    setWall(x, y, !isWall(x, y));
    afterEdit();
    return;
  }

  const existing = cellAt(x, y);
  if (existing && existing.kind === state.brush) {
    afterEdit();
    return;
  }

  if (existing) removeCell(existing);
  setWall(x, y, false);

  state.doc.elements.push(newCell(state.brush, x, y));
  afterEdit();
}

function eraseAt(x, y) {
  state.selectedKey = coordKey(x, y);

  const existing = cellAt(x, y);
  if (existing) removeCell(existing);
  else setWall(x, y, false);

  afterEdit();
}

function removeCell(cell) {
  const index = state.doc.elements.indexOf(cell);
  if (index >= 0) state.doc.elements.splice(index, 1);
}

function rotateCell(cell) {
  const info = kindInfo(cell.kind);
  if (info.params.indexOf('dir') < 0) return;

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

function dirPicker(current, onChange) {
  const wrap = document.createElement('div');
  wrap.className = 'dir-row';

  for (const dir of DIRS) {
    const button = document.createElement('button');
    button.textContent = DIR_MARK[dir] + ' ' + DIR_CN[dir];
    if (dir === current) button.classList.add('active');
    button.onclick = () => {
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
    (cell.type ? '<span class="dim">type=' + cell.type + '</span>' : '<span class="dim">kind 模式（不绑定资产）</span>');
  el.inspector.appendChild(title);

  if (info.params.indexOf('dir') >= 0) {
    const label = cell.kind === 'Receiver' ? '进料面' : '朝向';
    el.inspector.appendChild(fieldRow(label, dirPicker(cell.dir || 'Right', (dir) => {
      cell.dir = dir;
    })));
  }

  if (info.params.indexOf('dir2') >= 0) {
    el.inspector.appendChild(fieldRow('零臂', dirPicker(cell.dir2 || 'Down', (dir) => {
      cell.dir2 = dir;
    })));
  }

  if (info.params.indexOf('sequence') >= 0) {
    const input = document.createElement('input');
    input.type = 'text';
    input.value = cell.sequence || '1';
    input.spellcheck = false;
    input.oninput = () => {
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
      cell.pass = select.value === '0' ? 0 : 1;
      renderGrid();
    };
    el.inspector.appendChild(fieldRow('放行值', select));
  }

  const editable = document.createElement('input');
  editable.type = 'checkbox';
  editable.checked = !!cell.playerEditable;
  editable.onchange = () => {
    cell.playerEditable = editable.checked;
    renderGrid();
  };
  el.inspector.appendChild(fieldRow('玩家可动', editable));

  if (cell.type) {
    const detach = document.createElement('button');
    detach.textContent = '解除 type 绑定（改成 kind 模式）';
    detach.onclick = () => {
      cell.type = '';
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

/** 编辑器内部一律用 kind 模式：每格参数都写全，不依赖类型资产。 */
function serializeDoc() {
  const doc = state.doc;

  const out = {
    schema: 2,
    displayName: doc.displayName || 'CUSTOM LEVEL',
    width: doc.width,
    height: doc.height,
    tickInterval: Number(doc.tickInterval) || 0.35,
    allowBuild: !!doc.allowBuild,
  };

  const rows = (doc.wallRows || []).slice(0, doc.height);
  let lastWall = -1;
  for (let y = 0; y < rows.length; y++) {
    if ((rows[y] || '').indexOf('#') >= 0) lastWall = y;
  }

  if (lastWall >= 0) {
    out.wallRows = [];
    for (let y = 0; y <= lastWall; y++) {
      out.wallRows.push((rows[y] || '').padEnd(doc.width, '.').slice(0, doc.width));
    }
  }

  const cells = doc.elements.slice().sort((a, b) => (a.y - b.y) || (a.x - b.x));
  out.elements = cells.map((cell) => {
    const info = kindInfo(cell.kind);
    const one = { kind: cell.kind, x: cell.x, y: cell.y };

    if (info.params.indexOf('dir') >= 0) one.dir = cell.dir || 'Right';
    if (info.params.indexOf('dir2') >= 0) one.dir2 = cell.dir2 || 'Down';
    if (info.params.indexOf('sequence') >= 0) one.sequence = cell.sequence || '1';
    if (info.params.indexOf('count') >= 0) one.count = Math.max(0, Number(cell.count) || 0);
    if (info.params.indexOf('face') >= 0) one.face = cell.face === 'in' ? 'in' : 'any';
    if (info.params.indexOf('pass') >= 0) one.pass = cell.pass === 0 ? 0 : 1;
    if (cell.playerEditable) one.playerEditable = true;

    return one;
  });

  if (doc.supplies && doc.supplies.length) out.supplies = doc.supplies;
  return out;
}

/**
 * 读进来的关卡统一转成 kind 模式（参数全部展开）。
 * 类型资产（type）在这里被解析成它的默认参数 —— 用 /api/catalog 导出的快照，
 * 所以「打开 Unity 导出的关」看到的参数就是玩家实际拿到的参数。
 */
function fromDocument(input) {
  const doc = newDoc(Number(input.width) || 12, Number(input.height) || 9);
  doc.displayName = input.displayName || 'CUSTOM LEVEL';
  doc.tickInterval = Number(input.tickInterval) || 0.35;
  doc.allowBuild = !!input.allowBuild;
  doc.wallRows = (input.wallRows || []).slice(0, doc.height);
  doc.supplies = input.supplies || [];

  for (const raw of (input.elements || [])) {
    if (!raw) continue;
    const kind = raw.kind || kindOfType(raw.type);
    if (!kind || !KINDS.some((k) => k.kind === kind)) continue;

    if (kind === 'Wall') {
      setWallIn(doc, raw.x, raw.y, true);
      continue;
    }

    const spec = raw.type ? (state.catalog || []).find((t) => t.key === raw.type) : null;
    const base = spec || defaultsFor(kind);

    const cell = {
      kind: kind,
      x: raw.x,
      y: raw.y,
      dir: raw.dir || rotateBy(base.dir, raw.rot || 0),
      dir2: raw.dir2 || rotateBy(base.dir2 || 'Down', raw.rot || 0),
      sequence: raw.sequence || base.sequence || '1',
      count: raw.count >= 0 ? raw.count : (base.count >= 0 ? base.count : 1),
      face: raw.face ? (raw.face === 'in' ? 'in' : 'any') : (base.acceptAnyFace === false ? 'in' : 'any'),
      pass: raw.pass >= 0 ? raw.pass : (base.pass === 0 ? 0 : 1),
      playerEditable: !!raw.playerEditable,
    };

    doc.elements.push(cell);
  }

  return doc;
}

function kindOfType(type) {
  if (!type) return null;
  const spec = (state.catalog || []).find((t) => t.key === type);
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
      item.innerHTML = '<span class="name">' + file.name + '</span><span class="when">' + file.modified + '</span>';
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

    state.doc = fromDocument(raw);
    state.selectedKey = null;
    syncMetaInputs();
    afterEdit();
    el.jsonBox.value = JSON.stringify(serializeDoc(), null, 2);
    setStatus('已读入 ' + name + '（' + state.doc.elements.length + ' 个元件）', true);
  } catch (error) {
    setStatus('读失败：' + error.message, false);
  }
}

// ------------------------------------------------------------------ 自测

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

    el.checkResult.className = 'result ' + (result.pass ? 'pass' : 'fail');
    el.checkResult.textContent = (result.pass ? '✓ 可解' : '✗ 不可解') + '\n' + (result.report || '');
    el.problems.textContent = (result.problems || []).join('\n');
  } catch (error) {
    el.checkResult.className = 'result fail';
    el.checkResult.textContent = '连不上本地服务：' + error.message;
  }
}

// ------------------------------------------------------------------ 顶部信息栏

function syncMetaInputs() {
  el.displayName.value = state.doc.displayName || '';
  el.width.value = state.doc.width;
  el.height.value = state.doc.height;
  el.tickInterval.value = state.doc.tickInterval;
  el.allowBuild.checked = !!state.doc.allowBuild;
}

function clampSize(raw, fallback) {
  const value = parseInt(raw, 10);
  if (!Number.isFinite(value)) return fallback;
  return Math.min(40, Math.max(2, value));
}

/** 改尺寸：变小时越界的格子会被丢掉（不留「看不见但还在」的元件）。 */
function applyMeta() {
  state.doc.displayName = el.displayName.value.trim() || 'CUSTOM LEVEL';
  state.doc.tickInterval = Math.min(5, Math.max(0.05, Number(el.tickInterval.value) || 0.35));
  state.doc.allowBuild = el.allowBuild.checked;

  const width = clampSize(el.width.value, state.doc.width);
  const height = clampSize(el.height.value, state.doc.height);

  if (width !== state.doc.width || height !== state.doc.height) {
    const before = state.doc.elements.length;

    state.doc.width = width;
    state.doc.height = height;
    state.doc.elements = state.doc.elements.filter((cell) => cell.x < width && cell.y < height);

    if (state.doc.wallRows && state.doc.wallRows.length > height) {
      state.doc.wallRows.length = height;
    }

    const dropped = before - state.doc.elements.length;
    if (dropped > 0) setStatus('缩小网格后丢掉了 ' + dropped + ' 个越界元件', false);

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
  el.width.onchange = applyMeta;
  el.height.onchange = applyMeta;

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
    link.download = (state.doc.displayName || 'level') + '.json';
    link.click();
    URL.revokeObjectURL(link.href);
  };

  document.getElementById('btnImport').onclick = () => {
    try {
      const parsed = JSON.parse(el.jsonBox.value);
      state.doc = fromDocument(parsed);
      state.selectedKey = null;
      syncMetaInputs();
      afterEdit();
      setStatus('已读入文本框里的关卡（' + state.doc.elements.length + ' 个元件）', true);
    } catch (error) {
      setStatus('JSON 读不出来：' + error.message, false);
    }
  };

  document.getElementById('btnSave').onclick = saveLevel;
  document.getElementById('btnRefresh').onclick = loadFiles;

  document.getElementById('btnOpenFolder').onclick = async () => {
    const result = await apiPost('/api/open-folder', { where: state.where });
    setStatus(result.ok ? '已打开：' + result.directory : String(result.error || '打不开这个目录'), result.ok);
  };

  window.addEventListener('keydown', (event) => {
    if (interactionTargetIsField(event.target)) return;

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

    const digit = parseInt(event.key, 10);
    if (digit >= 1 && digit <= KINDS.length) {
      state.brush = KINDS[digit - 1].kind;
      renderPalette();
    }
  });
}

async function saveLevel() {
  const name = el.saveName.value.trim() || state.doc.displayName || 'level';
  const json = JSON.stringify(serializeDoc(), null, 2);

  setStatus('保存中…');
  try {
    const result = await apiPost('/api/save', { where: state.where, name: name, json: json });

    if (!result.ok) {
      setStatus(String(result.error || '保存失败'), false);
      return;
    }

    el.jsonBox.value = json;
    setStatus('已保存：' + result.file, true);
    await loadFiles();
  } catch (error) {
    setStatus('保存失败：' + error.message, false);
  }
}

// ------------------------------------------------------------------ 启动

async function boot() {
  await loadCatalog();

  state.doc = newDoc(12, 9);
  syncMetaInputs();
  renderPalette();
  renderGrid();
  renderInspector();
  bindUi();

  try {
    const info = await apiGet('/api/state');
    el.pathInfo.textContent = '工程：' + info.projectRoot
      + '\n玩家关卡目录：' + (info.playerDir || '未知')
      + (info.hasCatalog ? '' : '\n（还没导出类型目录：Unity 菜单 SHIFT GO/导出类型目录 之后，打开 Unity 导出的关才认得 type）');
  } catch (error) {
    el.pathInfo.textContent = '连不上本地服务：' + error.message;
  }

  await loadFiles();
  setStatus('左键放元件，右键擦。点格子后右边改参数。');
}

boot();





