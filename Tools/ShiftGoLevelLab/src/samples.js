/* 内置样关：两个官方关卡（与 Resources/BitFlow/Levels 下的文件同内容）+ 一关演示。
 * 单文件交付不能 fetch，所以这些数据直接写在这里。 */
(function (root) {
  'use strict';

  var SAMPLES = [
    {
      id: 'one_only',
      title: 'ONE ONLY',
      note: '官方关卡：有手牌，预设布局跑不通（自检应显示 📝）',
      document: {
        schema: 2,
        displayName: 'ONE ONLY',
        width: 9,
        height: 8,
        tickInterval: 0.35,
        wallType: 'Type_Wall',
        wallRows: [],
        elements: [
          { type: '', kind: 'Emitter', x: 0, y: 4, rot: 0, dir: 'Right', dir2: '', sequence: '10110000', count: 8, face: '', pass: -1, delay: -1, playerEditable: false },
          { type: '', kind: 'Receiver', x: 8, y: 6, rot: 0, dir: 'Down', dir2: '', sequence: '11111111', count: 8, face: 'any', pass: -1, delay: -1, playerEditable: false }
        ],
        supplies: [
          { type: 'Type_Splitter_L1', kind: 'Splitter', count: 1 },
          { type: 'Type_NotGate_N1', kind: 'NotGate', count: 1 },
          { type: 'Type_Filter_F1', kind: 'Filter', count: 1 }
        ],
        allowBuild: true
      }
    },
    {
      id: 'wait111',
      title: 'WAIT111',
      note: '官方关卡：延迟器 D4 改变节奏，无手牌（自检应显示 ✅）',
      document: {
        schema: 2,
        displayName: 'WAIT111',
        width: 9,
        height: 8,
        tickInterval: 0.35,
        wallType: '',
        wallRows: [],
        elements: [
          { type: '', kind: 'Emitter', x: 0, y: 2, rot: 0, dir: 'Right', dir2: '', sequence: '1011', count: 4, face: '', pass: -1, delay: -1, playerEditable: true },
          { type: '', kind: 'DelayGate', x: 3, y: 2, rot: 0, dir: '', dir2: '', sequence: '', count: -1, face: '', pass: -1, delay: 4, playerEditable: true },
          { type: '', kind: 'Receiver', x: 7, y: 2, rot: 0, dir: 'Down', dir2: '', sequence: '1101', count: 4, face: 'any', pass: -1, delay: -1, playerEditable: true },
          { type: '', kind: 'Emitter', x: 0, y: 6, rot: 0, dir: 'Right', dir2: '', sequence: '1011', count: 4, face: '', pass: -1, delay: -1, playerEditable: true },
          { type: '', kind: 'Receiver', x: 7, y: 6, rot: 0, dir: 'Down', dir2: '', sequence: '1101', count: 4, face: 'any', pass: -1, delay: -1, playerEditable: true }
        ],
        supplies: [],
        allowBuild: false
      }
    },
    {
      id: 'not_lab',
      title: 'NOT LAB',
      note: '演示关：发射 1111 → 非门取反 → 接收 0000（应显示 ✅ 通关）',
      document: {
        schema: 2,
        displayName: 'NOT LAB',
        width: 9,
        height: 6,
        tickInterval: 0.35,
        wallType: '',
        wallRows: ['#########', '#.......#', '#.......#', '#.......#', '#.......#', '#########'],
        elements: [
          { type: '', kind: 'Emitter', x: 1, y: 3, rot: 0, dir: 'Right', dir2: '', sequence: '1111', count: 4, face: '', pass: -1, delay: -1, playerEditable: false },
          { type: '', kind: 'NotGate', x: 3, y: 3, rot: 0, dir: 'Right', dir2: '', sequence: '', count: -1, face: '', pass: -1, delay: -1, playerEditable: false },
          { type: '', kind: 'Receiver', x: 7, y: 3, rot: 0, dir: 'Left', dir2: '', sequence: '0000', count: 4, face: 'any', pass: -1, delay: -1, playerEditable: false }
        ],
        supplies: [],
        allowBuild: false
      }
    }
  ];

  function clone(document) {
    return JSON.parse(JSON.stringify(document));
  }

  function byId(id) {
    for (var i = 0; i < SAMPLES.length; i++) {
      if (SAMPLES[i].id === id) return SAMPLES[i];
    }
    return null;
  }

  function blank() {
    return {
      schema: 2,
      displayName: 'NEW LEVEL',
      width: 9,
      height: 8,
      tickInterval: 0.35,
      wallType: '',
      wallRows: [],
      elements: [],
      supplies: [],
      allowBuild: false
    };
  }

  var api = { SAMPLES: SAMPLES, byId: byId, blank: blank, clone: clone };
  root.BitFlowSamples = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
