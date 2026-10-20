import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { createGunzip } from 'node:zlib';

const root = path.resolve('dist/client');
const port = Number(process.env.PORT || 4173);
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.json': 'application/json; charset=utf-8', '.svg': 'image/svg+xml', '.png': 'image/png', '.gif': 'image/gif', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.wasm': 'application/wasm', '.txt': 'text/plain; charset=utf-8', '.woff2': 'font/woff2', '.ttf': 'font/ttf', '.rsc': 'text/x-component' };
const regular = file => { try { return fs.statSync(file).isFile(); } catch { return false; } };
// Releases keep one compressed copy of text pages instead of duplicating
// gigabytes of HTML. Both ordinary HTTP clients and browsers can read them.
const available = file => regular(file) || regular(file + '.gz');
http.createServer((req, res) => {
  try {
    if (req.method !== 'GET' && req.method !== 'HEAD') {
      res.writeHead(405, { Allow: 'GET, HEAD' }); return res.end();
    }
    const url = new URL(req.url, 'http://127.0.0.1');
    const requested = decodeURIComponent(url.pathname);
    let file = path.resolve(root, '.' + requested);
    if (file !== root && !file.startsWith(root + path.sep)) { res.writeHead(403); return res.end(); }
    if (requested.endsWith('/') || (fs.existsSync(file) && fs.statSync(file).isDirectory())) file = path.join(file, 'index.html');
    if (!available(file)) {
      const html = path.resolve(root, '.' + requested.replace(/\/$/, '') + '.html');
      if (available(html)) file = html;
    }
    let status = 200;
    if (!available(file)) {
      status = 404; file = path.join(root, '404.html');
      if (!available(file)) { res.writeHead(404); return res.end('Not found'); }
    }
    const headers = {
      'Content-Type': types[path.extname(file)] || 'application/octet-stream',
      'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'strict-origin-when-cross-origin',
      'Cache-Control': url.pathname.startsWith('/assets/') ? 'public,max-age=31536000,immutable' : 'no-cache',
      Vary: 'Accept-Encoding',
      'Content-Security-Policy': "default-src 'self'; script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'"
    };
    const gzip = regular(file + '.gz');
    const acceptsGzip = String(req.headers['accept-encoding'] || '').split(',').some(part => {
      const [name, ...params] = part.trim().toLowerCase().split(';');
      return name === 'gzip' && !params.some(p => /^\s*q\s*=\s*0(?:\.0*)?\s*$/.test(p));
    });
    let decompress = false;
    if (gzip && (acceptsGzip || !regular(file))) {
      file += '.gz'; decompress = !acceptsGzip;
      if (acceptsGzip) headers['Content-Encoding'] = 'gzip';
    }
    if (!decompress) headers['Content-Length'] = String(fs.statSync(file).size);
    res.writeHead(status, headers);
    if (req.method === 'HEAD') return res.end();
    const source = fs.createReadStream(file);
    source.on('error', () => res.destroy());
    if (decompress) {
      const decode = createGunzip(); decode.on('error', () => res.destroy()); source.pipe(decode).pipe(res);
    } else source.pipe(res);
  } catch { if (!res.headersSent) res.writeHead(400); res.end('Invalid request'); }
}).listen(port, '127.0.0.1', () => console.log('Dark Ages Library: http://127.0.0.1:' + port + '/'));
