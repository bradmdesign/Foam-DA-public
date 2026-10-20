import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { load } from 'cheerio';

const root = process.cwd();
const cache = path.join(root, '.cache', 'sources');
const ledgerFile = path.join(root, 'research', 'coverage.json');
fs.mkdirSync(cache, { recursive: true });
fs.mkdirSync(path.dirname(ledgerFile), { recursive: true });
const refresh = process.argv.includes('--refresh');
const selectedSource = process.argv.find(x=>x.startsWith('--source='))?.split('=')[1];
const blockedHosts = new Set();
const previous = fs.existsSync(ledgerFile) ? JSON.parse(fs.readFileSync(ledgerFile, 'utf8')) : {};
const ledger = process.argv.includes('--reindex') ? {} : previous;
const seeds = ['https://vorlof.com/', 'https://www.wanderlustdb.com/','https://vorlof.com/general/calculator.js'];
const domain = u => new URL(u).hostname.includes('wanderlust') ? 'Wanderlust' : 'Vorlof';
function canonical(href, base) {
  try {
    const u = new URL(href.replaceAll('&amp;', '&'), base);
    if (!['http:', 'https:'].includes(u.protocol)) return null;
    if (!['vorlof.com','www.vorlof.com','wanderlustdb.com','www.wanderlustdb.com'].includes(u.hostname)) return null;
    u.protocol = 'https:'; u.hostname = u.hostname.includes('wanderlust') ? 'www.wanderlustdb.com' : 'vorlof.com';
    u.hash = ''; u.searchParams.sort();
    if (/\.(png|gif|jpe?g|webp|svg|ico|css|zip|exe|dll|mp[34]|wav|ogg|xml|pdf|rar|7z|txt|ttf|woff2?)$/i.test(u.pathname)) return null;
    return u.href;
  } catch { return null; }
}
function exclusion(url) {
  const u = new URL(url), p = u.pathname.toLowerCase();
  if (/\/(stats|selling|player|players|character|characters|playerdb|guild)\.(php|html)$/.test(p)) return 'Individual character records, rosters or selling page';
  if (p.includes('/general/newplayers') || p==='/nusg.php') return 'Dedicated new-player guide';
  if (/\/(admin|login|logout|register|toolkit|bugs_enh|cdn-cgi|1resources)(\/|\.|$)/i.test(p)) return 'Site administration, feedback or implementation resource';
  if (/\/(art|lich|its1500wordstrustme|legitabing)\.html$/.test(p)) return 'Personal artwork, essay or character narrative';
  if (/\.js$/.test(p) && !/wanderlust/.test(u.hostname)&&p!=='/general/calculator.js') return 'Legacy implementation script';
  if (/\/(js\/(ajaxcall|modal|tabbrowser)|get\/links)\./.test(p)) return 'Site implementation or external link directory';
  return null;
}
const queue = [], pending = new Set();
function enqueue(href, base, from, documentUrl) {
  const url = canonical(href, base); if (!url || pending.has(url)) return;
  if (!ledger[url]) ledger[url] = { url, source: domain(url), discoveredFrom: from ?? null, documentUrl:documentUrl||url, status: 'pending', reviewStatus: 'unreviewed' };
  const reason = exclusion(url);
  if (reason) { ledger[url].status = 'excluded'; ledger[url].reason = reason; return; }
  if (selectedSource && domain(url).toLowerCase() !== selectedSource.toLowerCase()) return;
  pending.add(url); queue.push(url);
}
for (const s of seeds) enqueue(s,s,null);
if(fs.existsSync('research/dynamic-seeds.json'))for(const s of JSON.parse(fs.readFileSync('research/dynamic-seeds.json','utf8')))enqueue(s.url,s.url,s.discoveredFrom,s.documentUrl);
for (const u of Object.keys(ledger)) if (ledger[u].status !== 'excluded') enqueue(u,u,ledger[u].discoveredFrom,ledger[u].documentUrl);
function discover(html, url, documentUrl=url) {
  const $ = load(html);
  $('a[href],area[href]').each((_,el)=>enqueue($(el).attr('href'),documentUrl,url));
  $('[onclick],[onload]').each((_,el)=>{
    const value = ($(el).attr('onclick')||'') + ' ' + ($(el).attr('onload')||'');
    for (const m of value.matchAll(/(?:getdata|loadContent|showModal|openModal)\(\s*['"]([^'"]+)['"]/g)) enqueue(m[1],documentUrl,url,documentUrl);
  });
  if (domain(url)==='Wanderlust') {
    $('script[src]').each((_,el)=>enqueue($(el).attr('src'),documentUrl,url,documentUrl));
  }
  return { title: $('title').first().text().trim(), tables: $('table').length };
}
function save(){ fs.writeFileSync(ledgerFile,JSON.stringify(ledger,null,2)); }
let cursor = 0, fetched = 0, active = 0;
function priority(url) {
  const u=new URL(url);
  if (/\/(quests|general|events|bosstactics)\/[^/]+\.html$/.test(u.pathname)) return 0;
  if (u.search) return 5;
  if (/\.js$/.test(u.pathname)) return 1;
  return 2;
}
function reorder(){ const remaining=queue.splice(cursor); remaining.sort((a,b)=>priority(a)-priority(b)); queue.push(...remaining); }
reorder();
async function runWorker(worker) {
  while (true) {
    const url = queue[cursor++];
    if (!url) { cursor--; if (!active) break; await new Promise(r=>setTimeout(r,250)); continue; }
    active++;
    const item = ledger[url];
    try {
      const key = crypto.createHash('sha256').update(url).digest('hex').slice(0,24);
      const file = path.join(cache,key+'.html');
      let html;
      if (!refresh && fs.existsSync(file) && (item.status==='fetched'||previous[url]?.status==='fetched')) {html=fs.readFileSync(file,'utf8');item.retrievedAt ||= previous[url]?.retrievedAt;item.httpStatus=200;}
      else {
        if(blockedHosts.has(new URL(url).hostname)) { item.status='deferred'; item.reason='Host temporarily denied access; retry after access is restored'; active--; continue; }
        const response = await fetch(url,{signal:AbortSignal.timeout(25000),headers:{'User-Agent':'DarkAgesLibraryResearch/1.0 (local reference rebuild)'}});
        item.httpStatus=response.status; item.retrievedAt=new Date().toISOString();
        if(response.status===403 || response.status===429) { blockedHosts.add(new URL(url).hostname); console.log(JSON.stringify({pausedHost:new URL(url).hostname,status:response.status})); }
        if (!response.ok) throw new Error('HTTP '+response.status);
        html=await response.text();
        if (/Please wait while your request is being verified|Just a moment\.\.\./i.test(html) && html.length<18000) throw new Error('Verification page, not source content');
        fs.writeFileSync(file,html); fetched++;
        await new Promise(r=>setTimeout(r,600));
      }
      Object.assign(item,{status:'fetched',cache:path.relative(root,file).replaceAll('\\','/'),bytes:Buffer.byteLength(html),sha256:crypto.createHash('sha256').update(html).digest('hex'),...discover(html,url,item.documentUrl||url)});
      delete item.error;
    } catch(e) { item.status='unavailable'; item.error=e.message; }
    active--;
    if (cursor%20===0) { save(); reorder(); }
    if (cursor%100===0) console.log(JSON.stringify({processed:cursor,discovered:queue.length,fetched,unavailable:Object.values(ledger).filter(x=>x.status==='unavailable').length}));
  }
}
process.on('SIGINT',()=>{save();process.exit(130)});
await Promise.all([runWorker(1),runWorker(2)]);
save();
const counts=Object.values(ledger).reduce((a,x)=>(a[x.status]=(a[x.status]||0)+1,a),{});
console.log(JSON.stringify({complete:true,counts}));
