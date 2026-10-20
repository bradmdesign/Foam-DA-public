import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import fs from 'node:fs';
import http from 'node:http';
import net from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync, gunzipSync } from 'node:zlib';

const script = fileURLToPath(new URL('./serve-local.mjs', import.meta.url));
const parent = path.resolve(path.dirname(script), '../../prototype/widescreen/packaging');
const fixture = fs.mkdtempSync(path.join(parent, 'server-check-'));
const root = path.join(fixture, 'dist/client');
fs.mkdirSync(path.join(root, 'nested'), { recursive: true });
fs.writeFileSync(path.join(root, 'index.html.gz'), gzipSync('<title>Dark Ages Library</title>Home'));
fs.writeFileSync(path.join(root, 'search.html.gz'), gzipSync('Search'));
fs.writeFileSync(path.join(root, 'nested/index.html.gz'), gzipSync('Nested'));
fs.writeFileSync(path.join(root, 'index.rsc.gz'), gzipSync('RSC payload'));
fs.writeFileSync(path.join(root, 'app.js.gz'), gzipSync('console.log("ready")'));
fs.writeFileSync(path.join(root, 'raw.txt'), 'Raw');
fs.writeFileSync(path.join(root, '404.html.gz'), gzipSync('Missing'));
fs.writeFileSync(path.join(fixture, 'dist/outside.txt'), 'Never expose');
const reserve = net.createServer(); reserve.listen(0, '127.0.0.1'); await once(reserve, 'listening');
const port = reserve.address().port; await new Promise(r => reserve.close(r));
const server = spawn(process.execPath, [script], { cwd: fixture, env: { ...process.env, PORT: String(port) }, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
const request = (url, headers = {}, method = 'GET') => new Promise((resolve, reject) => {
  const req = http.request({ hostname: '127.0.0.1', port, path: url, headers, method }, res => {
    const chunks = []; res.on('data', d => chunks.push(d)); res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, data: Buffer.concat(chunks) }));
  }); req.on('error', reject); req.end();
});
let checks = 0;
try {
  await Promise.race([once(server.stdout, 'data'), new Promise((_, reject) => { const t = setTimeout(() => reject(new Error('Server readiness timeout')), 8000); t.unref(); }), once(server, 'error').then(([err]) => { throw err; })]);
  for (const [url, expected] of [['/', '<title>Dark Ages Library</title>Home'], ['/search/', 'Search'], ['/search', 'Search'], ['/nested/', 'Nested'], ['/index.rsc', 'RSC payload'], ['/raw.txt', 'Raw']]) {
    const r = await request(url); assert.equal(r.status, 200); assert.equal(r.data.toString(), expected); checks++;
  }
  let r = await request('/app.js', { 'Accept-Encoding': 'gzip' }); assert.equal(r.headers['content-encoding'], 'gzip'); assert.equal(gunzipSync(r.data).toString(), 'console.log("ready")'); assert.match(r.headers['content-type'], /javascript/); checks++;
  r = await request('/', { 'Accept-Encoding': 'gzip;q=0' }); assert.equal(r.headers['content-encoding'], undefined); assert.match(r.data.toString(), /Dark Ages/); checks++;
  r = await request('/', { 'Accept-Encoding': 'gzip' }, 'HEAD'); assert.equal(r.status, 200); assert.equal(r.data.length, 0); assert.ok(Number(r.headers['content-length']) > 0); checks++;
  r = await request('/', {}, 'HEAD'); assert.equal(r.status, 200); assert.equal(r.data.length, 0); checks++;
  r = await request('/missing'); assert.equal(r.status, 404); assert.equal(r.data.toString(), 'Missing'); checks++;
  r = await request('/%2e%2e%5coutside.txt'); assert.equal(r.status, 403); assert.doesNotMatch(r.data.toString(), /Never expose/); checks++;
  r = await request('/%invalid'); assert.equal(r.status, 400); checks++;
  r = await request('/', {}, 'POST'); assert.equal(r.status, 405); checks++;
  console.log(JSON.stringify({ status: 'passed', compressed_server_checks: checks }));
} finally {
  server.kill(); await once(server, 'exit').catch(() => {});
  // Only this test's freshly-created directory beneath the packaging folder.
  if (path.dirname(fixture) !== parent || !path.basename(fixture).startsWith('server-check-')) throw new Error('Unsafe cleanup target');
  fs.rmSync(fixture, { recursive: true });
}
