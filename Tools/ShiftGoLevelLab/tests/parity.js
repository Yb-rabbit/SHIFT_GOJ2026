/* JS ↔ C# 对拍工装（JS 侧）。
 *
 * 用法：
 *   node tests/parity.js                 # 生成 tests/actual-js.json
 *   node tests/parity.js --check         # 与 tests/expected-csharp.json 逐字段比对
 *
 * 这个脚本存在的唯一目的：证明浏览器里跑的规则和游戏里跑的是同一份。
 * 任何一处字段对不上都直接非零退出，不给"看起来差不多"留余地。
 */
'use strict';

const fs = require('fs');
const path = require('path');

require('../src/engine/dir.js');
require('../src/engine/document.js');
require('../src/engine/simulation.js');
require('../src/engine/check.js');
const Catalog = require('../src/engine/catalog.js');

const Doc = globalThis.BitFlowDocument;
const Sim = globalThis.BitFlowSimulation;
const Check = globalThis.BitFlowCheck;

const testsDir = __dirname;
const FIXTURE_FILES = ['fixtures-core.json', 'fixtures-edge.json'];

function loadFixtures() {
  const all = [];
  for (const file of FIXTURE_FILES) {
    const raw = JSON.parse(fs.readFileSync(path.join(testsDir, file), 'utf8'));
    for (const fixture of raw.fixtures) {
      fixture.source = file;
      all.push(fixture);
    }
  }
  return all;
}

function round3(value) {
  return Math.round(value * 1000) / 1000;
}

function elementSummary(element) {
  const KIND = Doc.KIND;
  const summary = {
    kind: Doc.kindName(element.kind),
    id: element.id,
    x: element.x,
    y: element.y,
    dir: null,
    dir2: null,
    sequence: null,
    count: null,
    acceptAnyFace: null,
    pass: null,
    delay: null,
    playerEditable: !!element.playerEditable,
    typeKey: element.typeKey || '',
    label: element.label || ''
  };

  if (element.kind === KIND.Emitter) {
    summary.dir = element.facing;
    summary.sequence = element.sequence;
    summary.count = element.emitCount;
  } else if (element.kind === KIND.Receiver) {
    summary.dir = element.feedFace;
    summary.sequence = element.targetSequence;
    summary.count = element.targetCount;
    summary.acceptAnyFace = !!element.acceptAnyFace;
  } else if (element.kind === KIND.Splitter) {
    summary.dir = element.oneArm;
    summary.dir2 = element.zeroArm;
  } else if (element.kind === KIND.NotGate || element.kind === KIND.Filter) {
    summary.dir = element.outputDir;
    if (element.kind === KIND.Filter) summary.pass = element.passValue;
  } else if (element.kind === KIND.DelayGate) {
    summary.delay = element.delayTicks;
  }

  return summary;
}

function summarizeCase(fixture) {
  const read = Doc.read(fixture.document, Catalog.builtin);
  const definition = read.definition;
  const outcome = Check.run(definition, definition.hasSupplies);
  const issues = Check.runForSaveDetailed(definition);

  return {
    name: fixture.name,
    width: definition.width,
    height: definition.height,
    tickInterval: round3(definition.tickInterval),
    displayName: definition.displayName,
    allowBuild: !!definition.allowBuild,
    elements: definition.elements.map(elementSummary),
    supplies: definition.supplies.map(function (supply) {
      return {
        typeKey: supply.typeKey,
        kind: Doc.kindName(supply.kind),
        count: supply.count
      };
    }),
    problems: read.problems,
    ticks: outcome.ticks,
    success: outcome.success,
    finished: outcome.finished,
    report: outcome.report,
    receivers: outcome.result.receivers.map(function (receiver) {
      return {
        id: receiver.receiverId,
        targetCount: receiver.targetCount,
        receivedCount: receiver.receivedCount,
        received: receiver.received,
        matched: receiver.matched,
        isSuccess: receiver.isSuccess
      };
    }),
    issues: issues.map(function (issue) {
      return { severity: issue.severity, message: issue.message };
    })
  };
}

function diff(expected, actual, pathText, out) {
  if (out.length >= 25) return;

  if (Array.isArray(expected) || Array.isArray(actual)) {
    const left = expected || [];
    const right = actual || [];
    if (!Array.isArray(left) || !Array.isArray(right)) {
      out.push(pathText + ': 类型不同（' + JSON.stringify(expected) + ' vs ' + JSON.stringify(actual) + '）');
      return;
    }
    if (left.length !== right.length) {
      out.push(pathText + ': 长度不同（C# ' + left.length + ' / JS ' + right.length + '）');
    }
    const limit = Math.min(left.length, right.length);
    for (let i = 0; i < limit; i++) diff(left[i], right[i], pathText + '[' + i + ']', out);
    return;
  }

  if (expected && actual && typeof expected === 'object' && typeof actual === 'object') {
    const keys = new Set(Object.keys(expected).concat(Object.keys(actual)));
    for (const key of keys) diff(expected[key], actual[key], pathText + '.' + key, out);
    return;
  }

  if (expected !== actual) {
    out.push(pathText + ': C#=' + JSON.stringify(expected) + ' JS=' + JSON.stringify(actual));
  }
}

function main() {
  const fixtures = loadFixtures();
  const cases = fixtures.map(summarizeCase);
  const output = { engine: 'ShiftGoLevelLab JS', schema: Doc.CURRENT_SCHEMA, cases: cases };

  fs.writeFileSync(path.join(testsDir, 'actual-js.json'), JSON.stringify(output, null, 2) + '\n', 'utf8');
  console.log('JS 侧跑完 ' + cases.length + ' 个 fixture → tests/actual-js.json');

  const expectedPath = path.join(testsDir, 'expected-csharp.json');
  if (process.argv.indexOf('--check') < 0) return 0;
  if (!fs.existsSync(expectedPath)) {
    console.error('缺少 tests/expected-csharp.json，先跑 tests/parity-csharp');
    return 2;
  }

  const expected = JSON.parse(fs.readFileSync(expectedPath, 'utf8'));
  const problems = [];
  diff(expected.cases, output.cases, 'cases', problems);
  if (problems.length === 0) {
    console.log('✅ 与 C# 引擎逐字段一致（' + cases.length + ' 关）');
    return 0;
  }

  console.error('❌ 与 C# 引擎不一致：');
  for (const problem of problems) console.error('  ' + problem);
  return 1;
}

process.exitCode = main();
