/* 手牌试玩规则的单测（不经浏览器）。
 *
 * 用法：node tests/hand.js
 *
 * 为什么值得单独一组：这些规则一旦跑偏，作者在编辑器里「摆得出解」，
 * 玩家在游戏里却摆不出来 —— 这类偏差用肉眼看不出来，只能靠断言钉住。
 */
'use strict';

require('../src/engine/dir.js');
require('../src/engine/document.js');
require('../src/engine/simulation.js');
require('../src/engine/check.js');
const Catalog = require('../src/engine/catalog.js');
const HandLib = require('../src/hand.js');

const Doc = globalThis.BitFlowDocument;
const Check = globalThis.BitFlowCheck;

let passed = 0;
let failed = 0;

function check(name, condition, detail) {
  if (condition) {
    passed++;
    console.log('  ✔ ' + name);
  } else {
    failed++;
    console.log('  ✘ ' + name + (detail ? ' — ' + detail : ''));
  }
}

/* ---------------- 夹具 ---------------- */

function clone(value) { return JSON.parse(JSON.stringify(value)); }

/* 预设跑不通、补一枚非门就通的最小关：发射 0，接收要 1。 */
function minimal() {
  return {
    schema: 2,
    displayName: 'HAND MIN',
    width: 6,
    height: 3,
    tickInterval: 0.35,
    wallRows: ['......', '......', '......'],
    allowBuild: true,
    elements: [
      { type: '', kind: 'Emitter', x: 0, y: 1, rot: 0, dir: 'Right', dir2: '', sequence: '0', count: 1, face: '', pass: -1, delay: -1, playerEditable: false },
      { type: '', kind: 'Receiver', x: 4, y: 1, rot: 0, dir: 'Down', dir2: '', sequence: '1', count: 1, face: 'any', pass: -1, delay: -1, playerEditable: true }
    ],
    supplies: [{ type: '', kind: 'NotGate', count: 1 }]
  };
}

/* BUFFER 场景：锁定分流器 + 一张 BUFFER。 */
function withLockedPiece() {
  return {
    schema: 2,
    displayName: 'HAND BUFFER',
    width: 9,
    height: 8,
    tickInterval: 0.35,
    wallRows: [],
    allowBuild: true,
    elements: [
      { type: 'Type_Splitter_L1', kind: 'Splitter', x: 2, y: 0, rot: 0, dir: 'Up', dir2: 'Right', sequence: '1', count: 1, face: '', pass: -1, delay: -1, playerEditable: false },
      { type: 'Type_Receiver', kind: 'Receiver', x: 8, y: 7, rot: 0, dir: 'Down', dir2: '', sequence: '1', count: 1, face: 'any', pass: -1, delay: -1, playerEditable: true }
    ],
    supplies: [{ type: 'Type_Buffer', kind: 'Wall', count: 1 }]
  };
}

function judge(document_) {
  const read = Doc.read(document_, Catalog.builtin);
  const outcome = Check.run(read.definition, read.definition.hasSupplies);
  return { success: outcome.success, ticks: outcome.ticks, problems: read.problems };
}

/* ---------------- 用例 ---------------- */

console.log('hand.js');

/* 1. 按 type 记账：ONE ONLY 的三摞各 1 张。 */
(function () {
  const doc = clone(require('../src/samples.js').byId('one_only').document);
  const hand = HandLib.create(doc, Catalog.builtin);
  const keys = hand.piles.map(function (pile) { return pile.key; });
  check('1 三摞手牌按 type 记账',
    hand.piles.length === 3 && keys[0] === 'type:Type_Splitter_L1' && hand.piles[0].left === 1,
    JSON.stringify(keys) + ' · left ' + hand.piles[0].left);
  check('1b 剩余 / 总数都是 3', hand.status().left === 3 && hand.status().total === 3,
    JSON.stringify(hand.status()));
})();

/* 2. count 语义：-1 当 1 张、0 跳过并记警告、type 与 kind 都空也跳过。 */
(function () {
  const doc = minimal();
  doc.supplies = [
    { type: '', kind: 'Splitter', count: 2 },
    { type: '', kind: 'Splitter', count: -1 },
    { type: '', kind: 'NotGate', count: 0 },
    { type: '', kind: '', count: 1 }
  ];
  const hand = HandLib.create(doc, Catalog.builtin);
  check('2 -1 记 1 张并与同 kind 合摞', hand.piles.length === 1 && hand.piles[0].left === 3,
    'piles ' + hand.piles.length + ' left ' + (hand.piles[0] && hand.piles[0].left));
  check('2b count=0 与空牌各记一条警告', hand.warnings.length === 2, JSON.stringify(hand.warnings));
})();

/* 3. 落件准入：越界 / 锁定 / 占格 三种拒绝，且拒绝时不动牌库。 */
(function () {
  const doc = minimal();
  doc.supplies = [{ type: '', kind: 'NotGate', count: 2 }];
  const hand = HandLib.create(doc, Catalog.builtin);
  const key = hand.piles[0].key;

  const outside = hand.canPlace(key, 9, 0);
  const locked = hand.canPlace(key, 0, 1);
  const before = hand.piles[0].left;

  hand.place(key, 2, 1);
  const occupied = hand.canPlace(key, 2, 1);

  check('3 越界 → OutOfBounds', outside.reason === 'OutOfBounds', outside.reason);
  check('3b 锁定格 → Locked', locked.reason === 'Locked', locked.reason);
  check('3c 已占格 → Occupied', occupied.reason === 'Occupied', occupied.reason);
  check('3d 失败的落件不扣牌', before === 2 && hand.piles[0].left === 1, 'left ' + hand.piles[0].left);
})();

/* 4. 落件成格：写入 elements、playerEditable=true、记 origin、扣 1 张。 */
(function () {
  const hand = HandLib.create(minimal(), Catalog.builtin);
  const key = hand.piles[0].key;
  const result = hand.place(key, 2, 1);
  const cell = hand.document.elements[hand.document.elements.length - 1];

  check('4 落件成功', result.ok === true, result.reason);
  check('4b 新格是玩家件且朝向取 kind 默认',
    cell.kind === 'NotGate' && cell.playerEditable === true && cell.dir === 'Right',
    JSON.stringify({ kind: cell.kind, dir: cell.dir, pe: cell.playerEditable }));
  check('4c origin 记到这一摞', hand.isPlaced(2, 1) && hand.piles[0].left === 0, 'left ' + hand.piles[0].left);

  const second = hand.canPlace(key, 3, 1);
  check('4d 牌用完 → OutOfStock', second.reason === 'OutOfStock', second.reason);
})();

/* 5. 拾回：只认玩家自己放的，数量回到原摞；再拾同一格 → EmptyCell。 */
(function () {
  const hand = HandLib.create(minimal(), Catalog.builtin);
  const key = hand.piles[0].key;
  hand.place(key, 2, 1);

  const authorPick = hand.canPick(4, 1);
  const lockedPick = hand.canPick(0, 1);
  check('5 作者件 / 锁定件不能拾', authorPick.reason === 'NotPlayerPiece' && lockedPick.reason === 'Locked',
    authorPick.reason + ' / ' + lockedPick.reason);

  const picked = hand.pick(2, 1);
  check('5b 拾回成功并归还原摞', picked.ok && hand.piles[0].left === 1 &&
    hand.document.elements.length === 2, 'left ' + hand.piles[0].left);

  const again = hand.canPick(2, 1);
  check('5c 空位再拾 → EmptyCell', again.reason === 'EmptyCell', again.reason);
})();

/* 6. BUFFER：要工具卡、只收锁定件、收完生成同 type 的新摞。 */
(function () {
  const hand = HandLib.create(withLockedPiece(), Catalog.builtin);
  const buffer = hand.piles[0];
  const notLocked = hand.canRetract(8, 7, buffer.key);
  const ok = hand.canRetract(2, 0, buffer.key);

  check('6 BUFFER 不能收非锁定件', notLocked.reason === 'NotLocked', notLocked.reason);
  check('6b 能收锁定分流器', ok.ok === true, ok.reason);
  check('6c BUFFER 卡不能当棋子放', hand.canPlace(buffer.key, 5, 5).reason === 'NotPlaceable');

  const result = hand.retract(2, 0, buffer.key);
  const granted = hand.piles.filter(function (pile) { return pile.key === 'type:Type_Splitter_L1'; })[0];
  check('6d 收回后进同 type 的摞', result.ok === true && !!granted && granted.left === 1,
    JSON.stringify(hand.piles.map(function (pile) { return pile.key + '=' + pile.left; })));
  check('6e BUFFER 消耗 1 张', buffer.left === 0, 'left ' + buffer.left);
  check('6f 收回的格子变空且解锁', hand.document.elements.length === 1 && !hand.locked['2,0']);
})();

/* 7. 墙走墙层，不进 elements。 */
(function () {
  const doc = minimal();
  doc.supplies = [{ type: 'Type_Wall', kind: 'Wall', count: 2 }];
  const hand = HandLib.create(doc, Catalog.builtin);
  const key = hand.piles[0].key;
  hand.place(key, 1, 0);
  hand.place(key, 1, 1);

  check('7 墙写进 wallRows 而不是 elements',
    hand.document.elements.length === 2 &&
    (hand.document.wallRows[0] || '').charAt(1) === '#' &&
    (hand.document.wallRows[1] || '').charAt(1) === '#',
    JSON.stringify(hand.document.wallRows));

  hand.pick(1, 0);
  check('7b 拾墙同时清墙层与数量',
    (hand.document.wallRows[0] || '').charAt(1) === '.' && hand.piles[0].left === 1,
    (hand.document.wallRows[0] || '') + ' · left ' + hand.piles[0].left);
})();

/* 8. 旋转：只转自己的件；类型模式转 rot，kind 模式转 dir；延迟器不转。 */
(function () {
  const hand = HandLib.create(minimal(), Catalog.builtin);
  const key = hand.piles[0].key;
  hand.place(key, 2, 1);

  check('8 自己的件能转', hand.rotate(2, 1) === true && hand.document.elements[2].dir === 'Down',
    hand.document.elements[2].dir);
  check('8b 锁定件 / 空格不转', hand.rotate(0, 1) === false && hand.rotate(5, 2) === false);

  const typed = HandLib.create(minimal(), Catalog.builtin);
  typed.document.supplies = [{ type: 'Type_NotGate_N1', kind: 'NotGate', count: 1 }];
  const typedHand = HandLib.create(typed.document, Catalog.builtin);
  typedHand.place(typedHand.piles[0].key, 2, 1);
  const rotCell = typedHand.document.elements[typedHand.document.elements.length - 1];
  typedHand.rotate(2, 1);
  check('8c 类型模式转 rot 且保留 type', rotCell.type === 'Type_NotGate_N1' && rotCell.rot === 1,
    JSON.stringify({ type: rotCell.type, rot: rotCell.rot }));
})();

/* 9. 撤销快照：数量、锁定表、origin 一起往返。 */
(function () {
  const hand = HandLib.create(minimal(), Catalog.builtin);
  const key = hand.piles[0].key;
  const before = hand.serialize();
  hand.place(key, 2, 1);

  const restored = HandLib.restore(before, Catalog.builtin);
  check('9 还原后盘面与数量都回去了',
    restored.document.elements.length === 2 && restored.piles[0].left === 1 &&
    !restored.isPlaced(2, 1),
    'elements ' + restored.document.elements.length + ' left ' + restored.piles[0].left);

  const after = hand.serialize();
  const replay = HandLib.restore(after, Catalog.builtin);
  check('9b 快照互相独立（改一份不影响另一份）',
    replay.pick(2, 1).ok === true && replay.piles[0].left === 1,
    'left ' + replay.piles[0].left);
})();

/* 10. 端到端：预设跑不通 → 摆一枚非门 → 通关；采用解后全部转预设件。 */
(function () {
  const hand = HandLib.create(minimal(), Catalog.builtin);
  const before = judge(hand.document);
  check('10 预设布局跑不通', before.success === false, 'success ' + before.success);

  const placed = hand.place(hand.piles[0].key, 2, 1);
  const after = judge(hand.document);
  check('10b 摆上非门就通关', placed.ok === true && after.success === true,
    'success ' + after.success + ' ticks ' + after.ticks);

  const adopted = hand.adopt();
  const everyLocked = adopted.elements.every(function (cell) { return cell.playerEditable === false; });
  check('10c 采用解把玩家件转成预设件', everyLocked && adopted.supplies.length === 1 &&
    hand.document.elements[2].playerEditable === true,
    'locked ' + everyLocked);
  check('10d 预设本身可以随时取回', hand.preset().elements.length === 2);
})();

console.log('');
console.log('hand: ' + passed + '/' + (passed + failed) + ' PASS');
if (failed > 0) process.exit(1);
