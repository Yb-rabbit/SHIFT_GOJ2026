// Static server for the Unity WebGL build, with the MIME types Unity needs.
// Unity refuses to boot without application/wasm on the .wasm response, which is
// why double-clicking index.html (file://) or using most one-liner servers fails.
//
// Usage:  node Tools/webgl-verify/serve.js [buildDir] [port]
//         node Tools/webgl-verify/serve.js ../Builds/WebGL 8099
const http = require('http');
const fs = require('fs');
const path = require('path');
const url = require('url');

const root = path.resolve(process.argv[2] || path.join(__dirname, '..', '..', 'Builds', 'WebGL'));
const port = parseInt(process.argv[3] || '8099', 10);

const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.wasm': 'application/wasm',
  '.data': 'application/octet-stream',
  '.unityweb': 'application/octet-stream',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.ico': 'image/x-icon'
};

http.createServer((req, res) => {
  let p = decodeURIComponent(url.parse(req.url).pathname);
  if (p === '/') p = '/index.html';
  const f = path.join(root, p);
  fs.stat(f, (e, st) => {
    if (e || !st.isFile()) {
      res.writeHead(404, { 'Content-Type': 'text/plain' });
      res.end('404 ' + p);
      console.log('404 ' + p);
      return;
    }
    res.writeHead(200, {
      'Content-Type': types[path.extname(f).toLowerCase()] || 'application/octet-stream',
      'Content-Length': st.size,
      'Cache-Control': 'no-store'
    });
    if (req.method === 'HEAD') { res.end(); }
    else { fs.createReadStream(f).pipe(res); }
    console.log('200 ' + p + ' ' + st.size);
  });
}).listen(port, '127.0.0.1', () => console.log('LISTENING http://127.0.0.1:' + port + ' root=' + root));
