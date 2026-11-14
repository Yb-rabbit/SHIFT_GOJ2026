/* 类型目录（LevelTypeCatalog.cs 的浏览器版）。
 *
 * 数据抄自 Unity 菜单「导出类型目录」产出的 typeCatalog.json，
 * 也就是真实类型资产 CreateDefinition 读出来的那份快照 —— 编辑器打开
 * 只写了 type 的关卡时，靠它还原出和游戏一模一样的朝向 / 序列 / 数量。
 *
 * 手牌里的 type 也靠它认；认不出来时退回 kind 的内置默认值（与 C# 同一条规矩）。
 */
(function (root) {
  'use strict';

  var Doc = root.BitFlowDocument;
  if (!Doc && typeof require === 'function') Doc = require('./document.js');

  /* 与 Tools/LevelStudio/web-src/catalog.json 逐字段一致（schema 2）。 */
  var BUILTIN_TYPES = [
    { key: 'Type_Buffer', kind: 'Wall', label: 'BUFFER', hint: 'BUFFER  TAKE ONE FIXED PIECE BACK TO HAND', dir: 'Right', dir2: 'Down', sequence: '1', count: 1, acceptAnyFace: true, pass: 1, delay: 1, tool: true },
    { key: 'Type_DelayGate_D1', kind: 'DelayGate', label: 'D1', hint: 'DELAY D1\nHOLD 1 TICK  THROUGH', dir: 'Right', dir2: 'Down', sequence: '1', count: 1, acceptAnyFace: true, pass: 1, delay: 1, tool: false },
    { key: 'Type_DelayGate_D2', kind: 'DelayGate', label: 'D2', hint: 'DELAY D2\nHOLD 2 TICK  THROUGH', dir: 'Right', dir2: 'Down', sequence: '1', count: 1, acceptAnyFace: true, pass: 1, delay: 2, tool: false },
    { key: 'Type_DelayGate_D3', kind: 'DelayGate', label: 'D3', hint: 'DELAY D3\nHOLD 3 TICK  THROUGH', dir: 'Right', dir2: 'Down', sequence: '1', count: 1, acceptAnyFace: true, pass: 1, delay: 3, tool: false },
    { key: 'Type_DelayGate_D4', kind: 'DelayGate', label: 'D4', hint: 'DELAY D4\nHOLD 4 TICK  THROUGH', dir: 'Right', dir2: 'Down', sequence: '1', count: 1, acceptAnyFace: true, pass: 1, delay: 4, tool: false },
    { key: 'Type_Emitter', kind: 'Emitter', label: 'E', hint: 'EMITTER E\n1011 x2  FACE →', dir: 'Right', dir2: 'Down', sequence: '1011', count: 8, acceptAnyFace: true, pass: 1, delay: 1, tool: false },
    { key: 'Type_Filter_F0', kind: 'Filter', label: 'F0', hint: 'FILTER F0\nPASS 0  OUT →', dir: 'Right', dir2: 'Down', sequence: '1', count: 1, acceptAnyFace: true, pass: 0, delay: 1, tool: false },
    { key: 'Type_Filter_F1', kind: 'Filter', label: 'F1', hint: 'FILTER F1\nPASS 1  OUT →', dir: 'Right', dir2: 'Down', sequence: '1', count: 1, acceptAnyFace: true, pass: 1, delay: 1, tool: false },
    { key: 'Type_NotGate_N1', kind: 'NotGate', label: 'N1', hint: 'NOT N1\nINVERT  OUT →', dir: 'Right', dir2: 'Down', sequence: '1', count: 1, acceptAnyFace: true, pass: 1, delay: 1, tool: false },
    { key: 'Type_Receiver', kind: 'Receiver', label: 'R', hint: 'RECEIVER R\nNEED 1 x4  ANY FACE', dir: 'Down', dir2: 'Down', sequence: '1', count: 4, acceptAnyFace: true, pass: 1, delay: 1, tool: false },
    { key: 'Type_Splitter_L1', kind: 'Splitter', label: 'L1', hint: 'SPLITTER L1\n1 ↑   0 →', dir: 'Up', dir2: 'Right', sequence: '1', count: 1, acceptAnyFace: true, pass: 1, delay: 1, tool: false },
    { key: 'Type_Wall', kind: 'Wall', label: 'WALL', hint: 'WALL', dir: 'Right', dir2: 'Down', sequence: '1', count: 1, acceptAnyFace: true, pass: 1, delay: 1, tool: false }
  ];

  function normalizeType(raw) {
    return {
      key: typeof raw.key === 'string' ? raw.key : '',
      kind: typeof raw.kind === 'string' ? raw.kind : 'Wall',
      label: typeof raw.label === 'string' ? raw.label : '',
      hint: typeof raw.hint === 'string' ? raw.hint : '',
      dir: typeof raw.dir === 'string' ? raw.dir : 'Right',
      dir2: typeof raw.dir2 === 'string' ? raw.dir2 : 'Down',
      sequence: typeof raw.sequence === 'string' ? raw.sequence : '1',
      count: typeof raw.count === 'number' ? Math.trunc(raw.count) : 1,
      acceptAnyFace: raw.acceptAnyFace !== false,
      pass: typeof raw.pass === 'number' ? Math.trunc(raw.pass) : 1,
      delay: typeof raw.delay === 'number' ? Math.trunc(raw.delay) : 1,
      tool: raw.tool === true
    };
  }

  function fromTypes(rawTypes) {
    var types = [];
    var byKey = Object.create(null);
    var list = Array.isArray(rawTypes) ? rawTypes : [];

    for (var i = 0; i < list.length; i++) {
      if (!list[i] || typeof list[i] !== 'object') continue;
      var spec = normalizeType(list[i]);
      if (spec.key === '' || byKey[spec.key]) continue;
      types.push(spec);
      byKey[spec.key] = spec;
    }

    return {
      schema: Doc.CURRENT_SCHEMA,
      types: types,

      find: function (typeKey) {
        if (typeof typeKey !== 'string' || typeKey === '') return null;
        return byKey[typeKey] || null;
      },

      /** 按种类找第一份快照（墙层、kind 模式没写参数时的参照）。 */
      findByKind: function (kind) {
        for (var i = 0; i < types.length; i++) {
          if (Doc.parseKind(types[i].kind) === kind) return types[i];
        }
        return null;
      },

      /** 只列能摆上棋盘的件（工具卡是发牌用的，不能进 elements）。 */
      placeable: function () {
        var out = [];
        for (var i = 0; i < types.length; i++) {
          if (!types[i].tool) out.push(types[i]);
        }
        return out;
      }
    };
  }

  var builtin = fromTypes(BUILTIN_TYPES);

  var api = {
    BUILTIN_TYPES: BUILTIN_TYPES,
    builtin: builtin,
    fromTypes: fromTypes,
    normalizeType: normalizeType
  };

  root.BitFlowCatalog = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
