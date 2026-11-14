/* LevelDocument → LevelDefinition —— LevelDocumentBuilder.cs 的零依赖移植。
 *
 * 为什么逐行照抄而不是"重写一个更干净的"：这份构建器的每一条规则
 * （越界跳过、同坐标先到先得、(y,x) 排序后发 Id、type 模式吃 rot 且忽略 pass、
 * kind 模式用绝对 dir、工具卡不得进 elements、墙层追加在最后）都是游戏侧已经在跑的语义。
 * 只要有一格读法不同，"编辑器里能过 / 游戏里过不了" 就会再次出现。
 */
(function (root) {
  'use strict';

  var D = root.BitFlowDir;
  if (!D && typeof require === 'function') D = require('./dir.js');

  var CURRENT_SCHEMA = 2;

  var KIND = { Wall: 0, Emitter: 1, Receiver: 2, Splitter: 3, NotGate: 4, Filter: 5, DelayGate: 6 };
  var KIND_NAMES = ['Wall', 'Emitter', 'Receiver', 'Splitter', 'NotGate', 'Filter', 'DelayGate'];

  function kindName(kind) { return KIND_NAMES[kind] || 'Wall'; }

  function int(value, fallback) {
    return typeof value === 'number' && isFinite(value) ? Math.trunc(value) : fallback;
  }

  function str(value) { return typeof value === 'string' ? value : ''; }

  /* CellKind 解析：Wall/Emitter/... 也认 not、delay_gate 这种写法，还认数字串。 */
  function parseKind(text) {
    if (typeof text !== 'string') return null;
    var trimmed = text.trim();
    if (trimmed === '') return null;

    var squeezed = trimmed.replace(/_/g, '').replace(/-/g, '');
    if (squeezed.toLowerCase() === 'not') return KIND.NotGate;

    var lower = squeezed.toLowerCase();
    for (var i = 0; i < KIND_NAMES.length; i++) {
      if (KIND_NAMES[i].toLowerCase() === lower) return i;
    }
    if (/^\d+$/.test(squeezed)) {
      var numeric = parseInt(squeezed, 10);
      return numeric >= 0 && numeric <= KIND.DelayGate ? numeric : null;
    }
    return null;
  }

  /* 接收器 face 语义：any / in；空串 = 没写，"?" = 看不懂。 */
  function parseFace(text) {
    if (typeof text !== 'string' || text.trim() === '') return '';
    var lower = text.trim().toLowerCase();
    if (lower === 'any' || lower === 'all' || lower === '四面') return 'any';
    if (lower === 'in' || lower === 'inlet' || lower === '进料面') return 'in';
    return '?';
  }

  /* EditableElement.CreateDefault 的纯数据版本 —— kind 模式的唯一默认值来源。
   * 注意接收器默认进料面是 Down（位要朝上走进去），不是 Right。 */
  function kindDefaults(kind) {
    var spec = {
      key: '', kind: kindName(kind), label: '', hint: '',
      dir: 'Right', dir2: 'Down', sequence: '1', count: 1,
      acceptAnyFace: true, pass: 1, delay: 1
    };
    if (kind === KIND.Receiver) spec.dir = 'Down';
    else if (kind === KIND.Splitter) { spec.dir = 'Up'; spec.dir2 = 'Right'; }
    return spec;
  }

  function parseDirOr(text, fallback) {
    var parsed = D.parse(text);
    return parsed === null ? fallback : parsed;
  }

  function specKind(spec) {
    var parsed = parseKind(spec && spec.kind);
    return parsed === null ? KIND.Wall : parsed;
  }

  /* LevelTypeSpec.CreateDefinition：按快照生成一份逻辑定义（朝向按 rot 旋转）。 */
  function createDefinition(spec, x, y, rotationSteps, id) {
    var kind = specKind(spec);
    var primary = parseDirOr(spec.dir, D.RIGHT);
    var secondary = parseDirOr(spec.dir2, D.DOWN);
    var base = { kind: kind, id: id, x: x, y: y, label: str(spec.label), hint: str(spec.hint), typeKey: str(spec.key) };

    switch (kind) {
      case KIND.Emitter:
        base.facing = D.rotate(primary, rotationSteps);
        base.sequence = str(spec.sequence) || '1';
        base.emitCount = Math.max(0, int(spec.count, 1));
        return base;

      case KIND.Receiver:
        base.feedFace = D.rotate(primary, rotationSteps);
        base.acceptAnyFace = !!spec.acceptAnyFace;
        base.targetSequence = str(spec.sequence) || '1';
        base.targetCount = Math.max(0, int(spec.count, 1));
        return base;

      case KIND.Splitter:
        base.oneArm = D.rotate(primary, rotationSteps);
        base.zeroArm = D.rotate(secondary, rotationSteps);
        return base;

      case KIND.NotGate:
        base.outputDir = D.rotate(primary, rotationSteps);
        return base;

      case KIND.Filter:
        base.outputDir = D.rotate(primary, rotationSteps);
        base.passValue = int(spec.pass, 1) === 0 ? 0 : 1;
        return base;

      case KIND.DelayGate:
        var delay = int(spec.delay, 1);
        base.delayTicks = delay < 1 ? 1 : (delay > 4 ? 4 : delay);
        return base;

      default:
        return base;
    }
  }


  /* kind 模式的默认值 + 目录里同类的短名/提示（纯显示，不参与规则）。 */
  function buildKindSpec(kind, catalog) {
    var defaults = kindDefaults(kind);
    var spec = {
      key: '', kind: kindName(kind), label: '', hint: '',
      dir: defaults.dir, dir2: defaults.dir2,
      sequence: defaults.sequence, count: defaults.count,
      acceptAnyFace: true, pass: defaults.pass, delay: defaults.delay
    };

    var named = catalog && catalog.findByKind ? catalog.findByKind(kind) : null;
    if (named) {
      spec.label = str(named.label);
      spec.hint = str(named.hint);
    }
    return spec;
  }

  /* 这一格自己写的参数盖到定义上 —— 与 ElementPlacement.ApplyOverrides 同义。 */
  function applyCellOverrides(element, cell, kindMode) {
    var dir;
    switch (element.kind) {
      case KIND.Emitter:
        if (kindMode && (dir = D.parse(cell.dir)) !== null) element.facing = dir;
        if (str(cell.sequence).trim() !== '') element.sequence = str(cell.sequence).trim();
        if (int(cell.count, -1) >= 0) element.emitCount = Math.max(0, int(cell.count, -1));
        break;

      case KIND.Receiver:
        if (kindMode && (dir = D.parse(cell.dir)) !== null) element.feedFace = dir;

        var face = parseFace(cell.face);
        if (face === 'any') element.acceptAnyFace = true;
        else if (face === 'in') element.acceptAnyFace = false;

        if (str(cell.sequence).trim() !== '') element.targetSequence = str(cell.sequence).trim();
        if (int(cell.count, -1) >= 0) element.targetCount = Math.max(0, int(cell.count, -1));
        break;

      case KIND.Splitter:
        if (kindMode) {
          if ((dir = D.parse(cell.dir)) !== null) element.oneArm = dir;
          if ((dir = D.parse(cell.dir2)) !== null) element.zeroArm = dir;
        }
        break;

      case KIND.NotGate:
        if (kindMode && (dir = D.parse(cell.dir)) !== null) element.outputDir = dir;
        break;

      case KIND.Filter:
        if (kindMode && (dir = D.parse(cell.dir)) !== null) element.outputDir = dir;
        if (kindMode && int(cell.pass, -1) >= 0) element.passValue = int(cell.pass, -1) === 0 ? 0 : 1;
        break;

      case KIND.DelayGate:
        if (kindMode && int(cell.delay, -1) >= 0) {
          var delay = int(cell.delay, -1);
          element.delayTicks = delay < 1 ? 1 : (delay > 4 ? 4 : delay);
        }
        break;
    }
  }

  /* 一格 → 类型快照。type 精确（走目录，找不到退回 kind）；kind 宽松（内置默认）。 */
  function tryResolveSpec(cell, catalog, where, problems) {
    var rotationSteps = ((int(cell.rot, 0) % 4) + 4) % 4;
    var kindMode = true;
    var spec = null;
    var typeText = str(cell.type);

    if (typeText !== '') {
      var found = catalog && catalog.find ? catalog.find(typeText) : null;
      if (found) {
        if (found.tool) {
          problems.push(where + ' 用的是工具卡 ' + typeText + '（工具卡只能写进 supplies，不能放进 elements），已跳过');
          return null;
        }
        return { spec: found, rotationSteps: rotationSteps, kindMode: false, typeKey: cell.type };
      }

      var fallbackKind = parseKind(cell.kind);
      if (fallbackKind === null) {
        problems.push(where + ' 找不到类型 ' + typeText + '（缺 typeCatalog.json？也不认识 kind="' + str(cell.kind) + '"）');
        return null;
      }

      problems.push(where + ' 类型目录里没有 ' + typeText + '，按 kind=' + kindName(fallbackKind) + ' 的默认值处理');
      spec = buildKindSpec(fallbackKind, catalog);
      return { spec: spec, rotationSteps: 0, kindMode: true, typeKey: cell.type };
    }

    var kind = parseKind(cell.kind);
    if (kind === null) {
      problems.push(where + ' 解析不出元件类型：kind="' + str(cell.kind) + '" type="' + typeText + '"');
      return null;
    }

    spec = buildKindSpec(kind, catalog);
    return { spec: spec, rotationSteps: 0, kindMode: true, typeKey: '' };
  }

  /* 只解析一格（UI 画图用），不进关卡。失败返回 null。 */
  function resolveCell(cell, catalog) {
    var problems = [];
    var resolved = tryResolveSpec(cell || {}, catalog, 'cell', problems);
    if (!resolved) return null;

    var element = createDefinition(resolved.spec, int(cell.x, 0), int(cell.y, 0), resolved.rotationSteps, 0);
    element.typeKey = resolved.typeKey === '' ? str(resolved.spec.key) : resolved.typeKey;
    element.playerEditable = !!cell.playerEditable;
    applyCellOverrides(element, cell, resolved.kindMode);
    return element;
  }


  function coordText(x, y) { return '(' + x + ',' + y + ')'; }

  function clampNumber(value, min, max) {
    if (value < min) return min;
    return value > max ? max : value;
  }

  /* 一关文档 → 逻辑关卡。问题（越界/未知类型/叠格…）写进 problems，不抛异常。 */
  function read(document, catalog) {
    var problems = [];
    var definition = {
      displayName: 'CUSTOM LEVEL',
      width: 1,
      height: 1,
      tickInterval: 0.35,
      elements: [],
      supplies: [],
      allowBuild: false
    };

    if (!document || typeof document !== 'object') {
      problems.push('关卡数据是空的');
      return { definition: definition, problems: problems };
    }

    var width = Math.max(1, int(document.width, 9));
    var height = Math.max(1, int(document.height, 8));

    definition.displayName = str(document.displayName) === '' ? 'CUSTOM LEVEL' : document.displayName;
    definition.width = width;
    definition.height = height;

    var interval = typeof document.tickInterval === 'number' && isFinite(document.tickInterval) ? document.tickInterval : 0;
    definition.tickInterval = interval > 0.01 ? clampNumber(interval, 0.05, 5) : 0.35;

    /* ---------------- 元件 ---------------- */
    var placed = [];
    var used = Object.create(null);
    var cells = Array.isArray(document.elements) ? document.elements : [];

    for (var i = 0; i < cells.length; i++) {
      var cell = cells[i];
      if (!cell || typeof cell !== 'object') continue;

      var where = 'elements[' + i + ']';
      var x = int(cell.x, 0);
      var y = int(cell.y, 0);

      if (x < 0 || y < 0 || x >= width || y >= height) {
        problems.push(where + ' 坐标 ' + coordText(x, y) + ' 越界（本关 ' + width + '×' + height + '），已跳过');
        continue;
      }

      var key = x + ',' + y;
      if (used[key]) {
        problems.push(where + ' 坐标 ' + coordText(x, y) + ' 与前面的格子重复，已跳过');
        continue;
      }
      used[key] = true;

      var resolved = tryResolveSpec(cell, catalog, where, problems);
      if (!resolved) {
        delete used[key];
        continue;
      }

      placed.push({
        cell: cell,
        spec: resolved.spec,
        rotationSteps: ((resolved.rotationSteps % 4) + 4) % 4,
        kindMode: resolved.kindMode,
        typeKey: resolved.typeKey,
        x: x,
        y: y
      });
    }

    /* 统一按 (y, x) 排序后发 Id —— 同一份 JSON 无论行序怎么排，构建结果都一样 */
    placed.sort(function (left, right) {
      if (left.y !== right.y) return left.y - right.y;
      return left.x - right.x;
    });

    var nextId = 1;
    var occupied = Object.create(null);
    for (var p = 0; p < placed.length; p++) {
      var item = placed[p];
      var element = createDefinition(item.spec, item.x, item.y, item.rotationSteps, nextId);
      element.label = str(item.spec.label);
      element.typeKey = item.typeKey === '' ? str(item.spec.key) : str(item.typeKey);
      element.playerEditable = !!item.cell.playerEditable;
      applyCellOverrides(element, item.cell, item.kindMode);
      /* 详情面板的提示文本：编辑器里现算，规则不影响模拟。 */
      if (element.hint === '') element.hint = describeElement(element);

      definition.elements.push(element);
      occupied[item.x + ',' + item.y] = true;
      nextId++;
    }

    /* ---------------- 墙层（追加在最后，Id 接在后面） ---------------- */
    var walls = parseWalls(document, width, height, problems);
    if (walls.length > 0) {
      var wallSpec = catalog && catalog.findByKind ? catalog.findByKind(KIND.Wall) : null;
      var wallLabel = wallSpec ? str(wallSpec.label) : '';
      var wallKey = wallSpec ? str(wallSpec.key) : '';

      var id = nextId;
      var overlapped = 0;
      for (var w = 0; w < walls.length; w++) {
        var wall = walls[w];
        if (occupied[wall.x + ',' + wall.y]) { overlapped++; continue; }
        definition.elements.push({
          kind: KIND.Wall, id: id, x: wall.x, y: wall.y,
          label: wallLabel, hint: 'WALL', typeKey: wallKey, playerEditable: false
        });
        id++;
      }

      if (overlapped > 0) {
        problems.push('墙层和 elements 有 ' + overlapped + ' 个格子重叠，那些格子的墙已忽略（元件优先）');
      }
    }

    readSupplies(document, definition, problems);
    definition.hasSupplies = definition.supplies.length > 0;
    return { definition: definition, problems: problems };
  }


  function readSupplies(document, definition, problems) {
    definition.supplies = [];
    var list = Array.isArray(document.supplies) ? document.supplies : [];
    for (var i = 0; i < list.length; i++) {
      var supply = list[i];
      if (!supply || typeof supply !== 'object') continue;

      var where = 'supplies[' + i + ']';
      var rawCount = int(supply.count, -1);
      var count = rawCount >= 0 ? rawCount : 1;
      if (count === 0) {
        problems.push(where + ' count=0 等于不给这张牌，已跳过');
        continue;
      }

      var kind = parseKind(supply.kind);
      var type = str(supply.type);
      if (type === '' && kind === null) {
        problems.push(where + ' 既没有 type 也认不出 kind，已跳过');
        continue;
      }

      definition.supplies.push({
        typeKey: type,
        kind: kind === null ? KIND.Wall : kind,
        count: count
      });
    }

    /* 有手牌就必须能动手：手写 JSON 忘了写 allowBuild 时，玩家会盯着一排点不动的卡 */
    definition.allowBuild = !!document.allowBuild || definition.supplies.length > 0;
  }

  /* ---------------- 墙层 → 坐标表 ----------------
   * 一行一个 y（第一行 y=0，从下往上），行内第 i 个字符是 x=i；
   * 短行右侧留空，超出尺寸的字符忽略，同一格只算一次。 */
  function wallLines(document, height, problems) {
    var lines = [];

    if (Array.isArray(document.wallRows) && document.wallRows.length > 0) {
      if (document.wallRows.length > height) {
        problems.push('wallRows 有 ' + document.wallRows.length + ' 行，超过 height=' + height + '，多出来的行会被忽略');
      }
      for (var i = 0; i < document.wallRows.length; i++) {
        lines.push(typeof document.wallRows[i] === 'string' ? document.wallRows[i] : '');
      }
      return lines;
    }

    var map = str(document.wallMap);
    if (map === '') return lines;
    return map.replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
  }

  function parseWalls(document, width, height, problems) {
    var walls = [];
    if (!document) return walls;

    var lines = wallLines(document, height, problems);
    var seen = Object.create(null);

    for (var y = 0; y < lines.length && y < height; y++) {
      var line = lines[y];
      if (!line) continue;
      var limit = Math.min(line.length, width);
      for (var x = 0; x < limit; x++) {
        if (line[x] !== '#') continue;
        var key = x + ',' + y;
        if (seen[key]) continue;
        seen[key] = true;
        walls.push({ x: x, y: y });
      }
    }
    return walls;
  }

  /* 只在详情面板显示，不参与规则。 */
  function describeElement(element) {
    if (!element) return '';
    switch (element.kind) {
      case KIND.Emitter:
        return 'EMITTER ' + D.mark(element.facing) + ' ×' + element.emitCount + ' 序列 ' + element.sequence;
      case KIND.Receiver:
        return 'RECEIVER ' + (element.acceptAnyFace ? '四面可收' : '只认 ' + D.mark(element.feedFace))
          + ' ×' + element.targetCount + ' 序列 ' + element.targetSequence;
      case KIND.Splitter:
        return 'SPLITTER 1→' + D.mark(element.oneArm) + ' 0→' + D.mark(element.zeroArm);
      case KIND.NotGate:
        return 'NOT →' + D.mark(element.outputDir);
      case KIND.Filter:
        return 'FILTER 放行 ' + element.passValue + ' →' + D.mark(element.outputDir);
      case KIND.DelayGate:
        return 'DELAY ' + element.delayTicks + ' tick';
      default:
        return 'WALL';
    }
  }

  var api = {
    CURRENT_SCHEMA: CURRENT_SCHEMA,
    KIND: KIND,
    KIND_NAMES: KIND_NAMES,
    kindName: kindName,
    parseKind: parseKind,
    parseFace: parseFace,
    kindDefaults: kindDefaults,
    buildKindSpec: buildKindSpec,
    createDefinition: createDefinition,
    applyCellOverrides: applyCellOverrides,
    resolveCell: resolveCell,
    read: read,
    parseWalls: parseWalls,
    describeElement: describeElement
  };

  root.BitFlowDocument = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
