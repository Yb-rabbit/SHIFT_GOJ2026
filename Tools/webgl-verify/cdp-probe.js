// CDP probe: loads a Unity WebGL page in headless Edge and reports what the player
// actually does (console, exceptions, network status, canvas/loading-bar state).
//
// This is the tool that diagnosed the 2026-11-13 black screen: the page looked fine
// but the console said
//   Invalid serialized file version. File: "globalgamemanagers".
//
// Usage:
//   1. start a headless browser with a debug port
//      msedge --headless=new --disable-gpu --remote-debugging-port=9222 --user-data-dir=%TEMP%\edge-verify about:blank
//   2. node Tools/webgl-verify/serve.js ../Builds/WebGL 8099
//   3. node Tools/webgl-verify/cdp-probe.js http://127.0.0.1:8099/ 9222 120
//
// Output: %TEMP%/shiftgo_cdp.log (full transcript) and %TEMP%/shiftgo_shot.png
const os = require('os');
const path = require('path');
const fs = require('fs');
const url = process.argv[2];
const port = parseInt(process.argv[3] || '9222', 10);
const waitMs = parseInt(process.argv[4] || '120', 10) * 1000;
if (!url) { console.error('usage: node cdp-probe.js <url> [debugPort] [waitSeconds]'); process.exit(2); }

const events = [];
function log(line) { events.push(line); console.log(line); }

async function main() {
  const list = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
  const page = list.find(t => t.type === 'page');
  if (!page) throw new Error('no page target on port ' + port);
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  let id = 0;
  const pending = new Map();
  const send = (method, params) => new Promise((res, rej) => {
    const mid = ++id;
    pending.set(mid, { res, rej });
    ws.send(JSON.stringify({ id: mid, method, params: params || {} }));
  });

  const t0 = Date.now();
  const stamp = () => ((Date.now() - t0) / 1000).toFixed(2) + 's';

  await new Promise(r => ws.addEventListener('open', r, { once: true }));

  ws.addEventListener('message', ev => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) {
      const p = pending.get(m.id); pending.delete(m.id);
      if (m.error) p.rej(new Error(JSON.stringify(m.error))); else p.res(m.result);
      return;
    }
    const p = m.params || {};
    switch (m.method) {
      case 'Runtime.consoleAPICalled': {
        const text = (p.args || []).map(a => a.value !== undefined ? a.value : (a.description || a.type)).join(' ');
        log(stamp() + ' [console.' + p.type + '] ' + text);
        break;
      }
      case 'Runtime.exceptionThrown':
        log(stamp() + ' [EXCEPTION] ' + (p.exceptionDetails.exception && (p.exceptionDetails.exception.description || p.exceptionDetails.exception.value) || p.exceptionDetails.text));
        break;
      case 'Log.entryAdded':
        log(stamp() + ' [log.' + p.entry.level + '] ' + p.entry.text + (p.entry.url ? ' @ ' + p.entry.url : ''));
        break;
      case 'Network.responseReceived': {
        const r = p.response;
        if (r.status >= 400 || /WebGL|\.data|\.wasm|loader|framework|index/i.test(r.url))
          log(stamp() + ' [net ' + r.status + '] ' + r.url + ' type=' + p.type + ' mime=' + (r.mimeType || '?'));
        break;
      }
      case 'Network.loadingFailed':
        log(stamp() + ' [NETFAIL] ' + p.errorText + ' type=' + p.type + ' req=' + p.requestId);
        break;
      default: break;
    }
  });

  await send('Runtime.enable');
  await send('Log.enable');
  await send('Network.enable');
  await send('Page.enable');
  log(stamp() + ' navigating to ' + url);
  await send('Page.navigate', { url });

  const poll = setInterval(async () => {
    try {
      const r = await send('Runtime.evaluate', {
        expression: '(() => {' +
          'const c = document.querySelector("canvas");' +
          'const bar = document.querySelector("#unity-loading-bar");' +
          'const prog = document.querySelector("#unity-progress-bar-full");' +
          'return JSON.stringify({' +
            'hasCanvas: !!c, canvasW: c ? c.width : 0, canvasH: c ? c.height : 0,' +
            'barDisplay: bar ? getComputedStyle(bar).display : "none",' +
            'barWidth: prog ? prog.style.width : "",' +
            'unityInstance: typeof window.unityInstance !== "undefined" && !!window.unityInstance,' +
            'webgl2: (() => { try { return !!document.createElement("canvas").getContext("webgl2"); } catch (e) { return false; } })()' +
          '});' +
        '})()', returnByValue: true
      });
      log(stamp() + ' [state] ' + (r.result && r.result.value));
    } catch (e) { /* page may be mid-navigation */ }
  }, 5000);

  await new Promise(r => setTimeout(r, waitMs));
  clearInterval(poll);

  try {
    const shot = await send('Page.captureScreenshot', { format: 'png' });
    const shotPath = path.join(os.tmpdir(), 'shiftgo_shot.png');
    fs.writeFileSync(shotPath, Buffer.from(shot.data, 'base64'));
    log('screenshot -> ' + shotPath);
  } catch (e) { log('screenshot failed: ' + e.message); }

  const logPath = path.join(os.tmpdir(), 'shiftgo_cdp.log');
  fs.writeFileSync(logPath, events.join('\n'), 'utf8');
  console.log('transcript -> ' + logPath);
  ws.close();
  process.exit(0);
}

main().catch(e => { console.error('PROBE ERROR: ' + (e.stack || e.message)); process.exit(1); });
