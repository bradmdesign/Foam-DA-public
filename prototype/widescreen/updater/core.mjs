import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';
import https from 'node:https';
import crypto from 'node:crypto';
import {createGunzip, createGzip} from 'node:zlib';
import {pipeline} from 'node:stream/promises';
import {once} from 'node:events';

export const components = ['library', 'ads', 'app'];
const MAX_FILE = 256 * 1024 * 1024, MAX_TOTAL = 1024 * 1024 * 1024;
export const hash = data => crypto.createHash('sha256').update(data).digest('hex');
export async function fileHash(file) {
  const digest = crypto.createHash('sha256');
  for await (const chunk of fs.createReadStream(file)) digest.update(chunk);
  return digest.digest('hex');
}
export function safePath(root, name) {
  if (typeof name !== 'string' || Buffer.byteLength(name,'utf8') > 220 || /[\\:\x00-\x1f]/.test(name) || name.split('/').some(p => !p || p === '.' || p === '..' || /[. ]$|[<>"|?*]/.test(p) || /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(p))) throw Error('Unsafe update path.');
  const full = path.resolve(root, name);
  if (!full.startsWith(path.resolve(root) + path.sep)) throw Error('Update path escapes its folder.');
  return full;
}
export function noLinks(file) {
  for (let p = path.resolve(file); ; p = path.dirname(p)) {
    try { if (fs.lstatSync(p).isSymbolicLink()) throw Error('An update folder contains a link or junction.'); }
    catch (e) { if (e.code !== 'ENOENT') throw e; }
    if (path.dirname(p) === p) break;
  }
}
export function allowed(component, name) {
  safePath(path.resolve('.'), name);
  if (name.split('/').some(p => p.startsWith('.') || /^(private|runtime|sessions|characters|screenshots|skulled|node_modules|build|prepared-profiles)$/i.test(p))) return false;
  if (component === 'library') return name.startsWith('dist/client/') && /\.(html|js|mjs|css|json|svg|png|gif|jpg|jpeg|webp|avif|ico|wasm|txt|woff2?|ttf|rsc|xml|webmanifest|pf_meta|pf_index|pf_fragment|pf_filter|pagefind)(\.gz)?$/i.test(name);
  if (component === 'ads') return name === 'campaigns.json' || /^images\/[a-z0-9_-]+\.(png|jpg|jpeg|webp|gif)$/i.test(name);
  if (component === 'app') return ['Foam-DA.exe', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll', 'host-dock.css', 'THIRD-PARTY-NOTICES.txt'].includes(name) || /^theme\/[a-z0-9_.-]+\.(css|png|cur|ttf)$/i.test(name) || /^client-patches\/(catalog\.json|(?:960x600|1088x640|1152x680)\/(?:client|layout)\.patch\.gz)$/.test(name) || /^updater\/(core\.mjs|worker\.mjs)$/.test(name) || /^library\/scripts\/(serve-local\.mjs|companion-runtime\.js|companion-runtime\.css)$/.test(name);
  return false;
}
export async function readJson(file, fallback = null, limit = 16 * 1024 * 1024) {
  try { noLinks(file); if ((await fsp.stat(file)).size > limit) throw Error('Update metadata is too large.'); return JSON.parse(await fsp.readFile(file, 'utf8')); }
  catch (e) { if (e.code === 'ENOENT') return fallback; throw e; }
}
export async function atomicJson(file, value) {
  noLinks(file); await fsp.mkdir(path.dirname(file), {recursive:true});
  const temp = file + '.' + crypto.randomUUID() + '.tmp';
  try { await fsp.writeFile(temp, JSON.stringify(value, null, 2) + '\n', {flag:'wx'}); await fsp.rename(temp, file); }
  finally { await fsp.rm(temp, {force:true}); }
}
export function address(text, base) {
  const url = new URL(text, base);
  if (url.protocol !== 'https:' || url.username || url.password || url.hash) throw Error('Updates require an HTTPS address.');
  return url;
}
function unavailable(error) {
  return error?.code==='FOAM_UPDATE_UNAVAILABLE'||['ENOTFOUND','EAI_AGAIN','ECONNREFUSED','ECONNRESET','ETIMEDOUT','ENETUNREACH','EHOSTUNREACH','EPIPE'].includes(error?.code);
}
// Fetches only public files. No cookies, game data, account tokens or identifiers.
export async function download(url, limit, destination = null, redirects = 0) {
  url = address(url);
  if (redirects > 5) throw Error('Too many download redirects.');
  return new Promise((resolve, reject) => {
    const request = https.get(url, {headers:{'User-Agent':'Foam-DA-Updater/1', 'Accept-Encoding':'identity'}}, response => {
      if ([301,302,303,307,308].includes(response.statusCode)) {
        response.resume();
        try { resolve(download(address(response.headers.location, url), limit, destination, redirects + 1)); } catch (e) { reject(e); }
        return;
      }
      if(response.statusCode>=500){response.resume();reject(Object.assign(Error('The update source is temporarily unavailable.'),{code:'FOAM_UPDATE_UNAVAILABLE'}));return;}
      if (response.statusCode !== 200 || Number(response.headers['content-length'] || 0) > limit) { response.resume(); reject(Error('Update source returned an unavailable or oversized file.')); return; }
      let count = 0; const chunks = [], out = destination ? fs.createWriteStream(destination, {flags:'wx'}) : null;
      response.on('data', chunk => { count += chunk.length; if (count > limit) response.destroy(Error('Update download exceeds its size limit.')); else if (!out) chunks.push(chunk); });
      response.on('error', reject);
      if (out) { pipeline(response, out).then(() => resolve(count), reject); }
      else response.on('end', () => resolve(Buffer.concat(chunks)));
    });
    request.setTimeout(30000, () => request.destroy(Object.assign(Error('Update request timed out.'),{code:'ETIMEDOUT'})));
    request.on('error', reject);
  });
}
function revision(value) { return Number.isSafeInteger(value) && value > 0; }
function digest(value) { return typeof value === 'string' && /^[a-f0-9]{64}$/.test(value); }
export function validateFeed(feed) {
  if (feed?.schema !== 1 || !feed.components || Object.keys(feed.components).some(c => !components.includes(c))) throw Error('Unknown update feed.');
  for (const d of Object.values(feed.components)) {
    if (!revision(d.revision) || typeof d.version !== 'string' || !/^[a-zA-Z0-9._-]{1,60}$/.test(d.version) || !digest(d.manifest_sha256) || !/^[a-zA-Z0-9._-]+\.json$/.test(d.manifest)) throw Error('Invalid update release.');
  }
  return feed;
}
export function verifyFeed(bytes, signature, publicKey) {
  const key = crypto.createPublicKey(publicKey);
  if (key.asymmetricKeyType !== 'rsa' || key.asymmetricKeyDetails.modulusLength < 3072 || !crypto.verify('sha256', bytes, key, signature)) throw Error('Update signature is invalid.');
  return validateFeed(JSON.parse(bytes.toString('utf8')));
}
export function validateManifest(m, component, descriptor) {
  if (m?.schema !== 1 || m.component !== component || m.revision !== descriptor.revision || m.version !== descriptor.version || !Array.isArray(m.files) || !m.files.length || m.files.length > 100000 || !digest(m.sha256) || !Number.isSafeInteger(m.bytes) || m.bytes <= 0 || m.bytes > MAX_TOTAL || !/^[a-zA-Z0-9._-]+\.fup$/.test(m.package)) throw Error('Invalid update manifest.');
  const names = new Set(); let total = 0;
  for (const f of m.files) {
    const lower = String(f.path).toLowerCase();
    if (!allowed(component, f.path) || names.has(lower) || !digest(f.sha256) || !Number.isSafeInteger(f.bytes) || f.bytes < 0 || f.bytes > MAX_FILE) throw Error('Invalid update file inventory.');
    names.add(lower); total += f.bytes;
  }
  if (total > MAX_TOTAL || (component === 'library' && !names.has('dist/client/index.html') && !names.has('dist/client/index.html.gz')) || (component === 'ads' && !names.has('campaigns.json')) || (component === 'app' && !names.has('foam-da.exe'))) throw Error('Incomplete update.');
  return m;
}
export async function writePack(files, output) {
  const gzip = createGzip({level:9}); const done = pipeline(gzip, fs.createWriteStream(output, {flags:'wx'}));
  done.catch(()=>{});
  async function write(b) { if (!gzip.write(b)) await once(gzip,'drain'); }
  try {
    await write(Buffer.from('FOAMUP1\n'));
    for (const f of files) {
      const name = Buffer.from(f.path, 'utf8'), header = Buffer.alloc(10);
      header.writeUInt16LE(name.length); header.writeBigUInt64LE(BigInt(f.bytes),2);
      await write(header); await write(name);
      for await (const chunk of fs.createReadStream(f.source)) await write(chunk);
    }
    gzip.end(); await done;
  } catch (e) { gzip.destroy(e); await done.catch(()=>{}); throw e; }
}
export async function extractPack(pack, manifest, destination) {
  noLinks(destination); await fsp.mkdir(destination, {recursive:false});
  const decoder = createGunzip(); const source = fs.createReadStream(pack); source.on('error', e => decoder.destroy(e)); source.pipe(decoder);
  const iterator = decoder[Symbol.asyncIterator](); let buffer = Buffer.alloc(0);
  async function take(n) {
    while (buffer.length < n) { const next = await iterator.next(); if (next.done) throw Error('Truncated update package.'); buffer = Buffer.concat([buffer,next.value]); }
    const result = buffer.subarray(0,n); buffer = buffer.subarray(n); return result;
  }
  try {
    if (!(await take(8)).equals(Buffer.from('FOAMUP1\n'))) throw Error('Unknown update package.');
    for (const expected of manifest.files) {
      const header = await take(10), length = header.readUInt16LE(0), size = header.readBigUInt64LE(2);
      if (length < 1 || length > 220 || size !== BigInt(expected.bytes)) throw Error('Update size does not match its manifest.');
      const name = (await take(length)).toString('utf8'); if (name !== expected.path) throw Error('Update order does not match its inventory.');
      const file = safePath(destination,name); noLinks(file); await fsp.mkdir(path.dirname(file), {recursive:true});
      const out = await fsp.open(file, 'wx'), sha = crypto.createHash('sha256');
      try { for (let remaining = expected.bytes; remaining;) { const chunk = await take(Math.min(65536,remaining)); sha.update(chunk); await out.writeFile(chunk); remaining -= chunk.length; } }
      finally { await out.close(); }
      if (sha.digest('hex') !== expected.sha256) throw Error('Update file checksum failed.');
    }
    if (buffer.length || !(await iterator.next()).done) throw Error('Unexpected extra update data.');
  } finally { source.destroy(); decoder.destroy(); }
}
export function validateCampaigns(value) {
  if (value?.schema !== 1 || !Array.isArray(value.campaigns) || value.campaigns.length > 20) throw Error('Invalid ad collection.');
  for (const c of value.campaigns) {
    if (!/^[a-z0-9_-]{1,60}$/i.test(c.id || '') || typeof c.title !== 'string' || c.title.length > 100 || (c.text && (typeof c.text !== 'string' || c.text.length > 180)) || (c.image && !allowed('ads',c.image)) || c.image === 'campaigns.json') throw Error('Invalid ad.');
    if (c.href) address(c.href);
    for (const field of ['starts','ends']) if (c[field] && !Number.isFinite(Date.parse(c[field]))) throw Error('Invalid ad schedule.');
    if(c.starts&&c.ends&&Date.parse(c.ends)<=Date.parse(c.starts))throw Error('An ad must end after it starts.');
  }
  return value;
}
export async function withLock(root, action) {
  noLinks(root); await fsp.mkdir(root, {recursive:true}); const lock = path.join(root,'update.lock'); let handle;
  try { handle = await fsp.open(lock,'wx'); await handle.writeFile(String(process.pid)); }
  catch (e) {
    if (e.code !== 'EEXIST') throw e;
    const pid = Number(await fsp.readFile(lock,'utf8')); let active = false;
    try { if (Number.isSafeInteger(pid) && pid > 0) { process.kill(pid,0); active = true; } } catch (e) { if (e.code !== 'ESRCH') active = true; }
    if (active) throw Error('An update check is already running.');
    await fsp.unlink(lock); return withLock(root,action);
  }
  try { return await action(); } finally { await handle.close(); await fsp.unlink(lock); }
}
async function removeStage(root, folder) {
  if (path.dirname(folder) !== root || !/^pending-[a-f0-9-]{36}$/.test(path.basename(folder))) throw Error('Unsafe update cleanup.');
  noLinks(folder); if (!fs.existsSync(folder)) return;
  // Inspect descendants too: recursive deletion must never follow a junction.
  const inspect = p => { noLinks(p); for (const entry of fs.readdirSync(p,{withFileTypes:true})) { const child = path.join(p,entry.name); noLinks(child); if (entry.isDirectory()) inspect(child); } };
  inspect(folder); await fsp.rm(folder,{recursive:true});
}
export async function update(root, config, fetch = download) {
  address(config.feed_url);
  const store = path.join(root,'updates');
  return withLock(store, async () => {
    const signatureUrl=new URL(config.feed_url);signatureUrl.pathname+='.sig';
    const bytes = await fetch(new URL(config.feed_url),65536), sig = await fetch(signatureUrl,4096);
    const feed = verifyFeed(bytes,sig,config.public_key);
    const state = await readJson(path.join(store,'state.json'),{schema:1,components:{}}); const changed = [];
    const required=await requiredApp(root,config), app=feed.components.app;
    const current=await readJson(path.join(store,'app-current.json'));
    const installedRevision=current?.revision||config.installed_app?.revision||0;
    if(required&&app&&app.revision<required.revision)throw Error('The update feed is older than a required app version already received.');
    // Record a verified required version before downloading any component.
    // An interrupted download must not turn into permission to run offline.
    if(app&&app.revision>installedRevision)await atomicJson(path.join(store,'app-required.json'),{schema:1,feed_base64:bytes.toString('base64'),signature_base64:sig.toString('base64')});
    else if(!required)await fsp.rm(path.join(store,'app-required.json'),{force:true});
    for (const component of components) {
      const d = feed.components[component]; if (!d) continue;
      const previous = state.components[component] || config['installed_'+component];
      if (previous && d.revision < previous.revision) throw Error('The update feed is older than a version already received.');
      if (previous && d.revision === previous.revision) {
        if (d.manifest_sha256 !== previous.manifest_sha256) throw Error('A released version was changed in place. Publish a new revision.');
        let repair=false;
        if(component==='app'){
          const pending=await readJson(path.join(store,'app-pending.json'));
          if(d.revision>installedRevision&&!pending)repair=true;
          if(pending?.revision===d.revision){
            try{
              const cached=safePath(store,'app/'+pending.release),receipt=await readJson(path.join(cached,'receipt.json'));
              const raw=Buffer.from(receipt.manifest_base64,'base64');if(hash(raw)!==d.manifest_sha256)throw Error('Damaged manifest.');
              const m=validateManifest(JSON.parse(raw),component,d);
              for(const f of m.files){const file=safePath(cached,f.path);noLinks(file);if(await fileHash(file)!==f.sha256)throw Error('Damaged cache.');}
            }catch{repair=true;}
          }
        }
        if(!repair)continue;
      }
      const raw = await fetch(address(d.manifest,config.feed_url),16*1024*1024);
      if (hash(raw) !== d.manifest_sha256) throw Error('Release manifest checksum failed.');
      const m = validateManifest(JSON.parse(raw),component,d);
      const folder = path.join(store,component); noLinks(folder); await fsp.mkdir(folder,{recursive:true});
      const stage = path.join(folder,'pending-'+crypto.randomUUID()), pack = stage+'.fup';
      try {
        const space = await fsp.statfs(store); const required = m.bytes + m.files.reduce((sum,f)=>sum+f.bytes,0) + 32*1024*1024;
        if (space.bavail * space.bsize < required) throw Error('Not enough space to stage this update safely.');
        await fetch(address(m.package,config.feed_url),m.bytes,pack);
        if ((await fsp.stat(pack)).size !== m.bytes || await fileHash(pack) !== m.sha256) throw Error('Downloaded update checksum failed.');
        await extractPack(pack,m,stage);
        if (component === 'ads') {
          const ads = validateCampaigns(await readJson(path.join(stage,'campaigns.json')));
          for (const c of ads.campaigns) if (c.image && (!m.files.some(f=>f.path===c.image) || fs.statSync(safePath(stage,c.image)).size > 2*1024*1024)) throw Error('Ad image is missing or too large.');
        }
        await atomicJson(path.join(stage,'receipt.json'),{manifest_base64:raw.toString('base64'),feed_base64:bytes.toString('base64'),signature_base64:sig.toString('base64')});
        const release = String(m.revision)+'-'+m.sha256.slice(0,12), final = path.join(folder,release);
        if (!fs.existsSync(final)) await fsp.rename(stage,final);
        else {
          let valid=true;
          try{for (const f of m.files){const file=safePath(final,f.path);noLinks(file);if(await fileHash(file)!==f.sha256)valid=false;}}catch{valid=false;}
          if(!valid){
            if(component!=='app')throw Error('An existing update cache is damaged.');
            // Quarantine the old app cache rather than deleting unknown files.
            noLinks(final);await fsp.rename(final,path.join(folder,'damaged-'+crypto.randomUUID()));await fsp.rename(stage,final);
          }
        }
        const pointer = {schema:1,release,revision:m.revision,version:m.version};
        if (component === 'app') await atomicJson(path.join(store,'app-pending.json'),pointer);
        else { const old = await readJson(path.join(store,component+'-current.json')); if (old) await atomicJson(path.join(store,component+'-previous.json'),old); await atomicJson(path.join(store,component+'-current.json'),pointer); }
        state.components[component] = {...d,release}; await atomicJson(path.join(store,'state.json'),state); changed.push(component);
        if(component!=='app')await pruneContent(store,component).catch(()=>{});
      } finally { await fsp.rm(pack,{force:true}); await removeStage(folder,stage); }
    }
    const pending=!!await readJson(path.join(store,'app-pending.json'));
    return {ok:true,changed,app_pending:pending,message:pending?'A required app update is ready.':changed.length?'Library content updated.':'Everything is up to date.'};
  });
}

// Startup is deliberately separate from a background check: a pending app
// must never be treated as permission to run the old executable.
export async function prepareLaunch(root,config,fetch=download) {
  try{
    const result=await update(root,config,fetch);
    const installing=!!await readJson(path.join(root,'updates/app-installing.json'));
    const required=await requiredApp(root,config);
    return {...result,ready:!result.app_pending&&!installing&&!required,installing};
  }catch(error){
    if(!unavailable(error))throw error;
    const pending=await readJson(path.join(root,'updates/app-pending.json'));
    const installing=await readJson(path.join(root,'updates/app-installing.json'));
    if(pending||installing||await requiredApp(root,config))throw Error('An update is pending. Reconnect to finish updating before opening Foam DA.');
    await fsp.rm(path.join(root,'updates/app-required.json'),{force:true});
    return {ok:true,changed:[],ready:true,offline:true,app_pending:false,message:'Offline. Using the installed version; no update is pending.'};
  }
}

async function requiredApp(root,config){
  const receipt=await readJson(path.join(root,'updates/app-required.json'));if(!receipt)return null;
  if(receipt.schema!==1||typeof receipt.feed_base64!=='string'||typeof receipt.signature_base64!=='string')throw Error('Invalid required update record.');
  const feed=verifyFeed(Buffer.from(receipt.feed_base64,'base64'),Buffer.from(receipt.signature_base64,'base64'),config.public_key);
  if(!feed.components.app)throw Error('Required update record has no app version.');
  const current=await readJson(path.join(root,'updates/app-current.json'));
  return feed.components.app.revision>(current?.revision||config.installed_app?.revision||0)?feed.components.app:null;
}

async function pruneContent(store,component) {
  const folder=path.join(store,component),keep=new Set();
  for(const suffix of ['current','previous']){const p=await readJson(path.join(store,component+'-'+suffix+'.json'));if(p)keep.add(p.release);}
  for(const entry of await fsp.readdir(folder,{withFileTypes:true})) {
    if(!entry.isDirectory()||keep.has(entry.name)||!/^\d+-[a-f0-9]{12}$/.test(entry.name))continue;
    const target=path.join(folder,entry.name);noLinks(target);
    const receipt=await readJson(path.join(target,'receipt.json'));if(!receipt)continue;
    const m=JSON.parse(Buffer.from(receipt.manifest_base64,'base64').toString('utf8'));
    if(m.component!==component||!Array.isArray(m.files))continue;
    const expected=new Set(['receipt.json',...m.files.map(f=>f.path)]);let owned=true;
    async function inspect(dir,prefix='') {
      noLinks(dir);
      for(const child of await fsp.readdir(dir,{withFileTypes:true})){
        const name=prefix+child.name,p=path.join(dir,child.name);noLinks(p);
        if(child.isDirectory())await inspect(p,name+'/');else if(!child.isFile()||!expected.has(name))owned=false;
      }
    }
    await inspect(target);
    if(owned&&path.dirname(target)===folder)await fsp.rm(target,{recursive:true});
  }
}

export async function installApp(root, config) {
  const store = path.join(root,'updates');
  return withLock(store,async () => {
    const pointer = await readJson(path.join(store,'app-pending.json'));
    if (!pointer || !/^\d+-[a-f0-9]{12}$/.test(pointer.release)) throw Error('No app update is ready.');
    const folder = safePath(store,'app/'+pointer.release), receipt = await readJson(path.join(folder,'receipt.json'));
    const feed = verifyFeed(Buffer.from(receipt.feed_base64,'base64'),Buffer.from(receipt.signature_base64,'base64'),config.public_key);
    const raw = Buffer.from(receipt.manifest_base64,'base64'), descriptor = feed.components.app;
    if (hash(raw) !== descriptor.manifest_sha256 || descriptor.revision !== pointer.revision) throw Error('App release signature does not match.');
    const m = validateManifest(JSON.parse(raw),'app',descriptor);
    for (const f of m.files) { const file = safePath(folder,f.path); noLinks(file); if ((await fsp.stat(file)).size !== f.bytes || await fileHash(file) !== f.sha256) throw Error('The app update cache is damaged.'); }
    // Keep a recovery journal and every overwritten file until a later release.
    // No user directories, game sessions, screenshots or preferences are replaced.
    const backup = path.join(store,'app-backup-'+crypto.randomUUID()); await fsp.mkdir(backup);
    const journal = {schema:1,backup:path.basename(backup),files:[]};
    const journalFile = path.join(store,'app-installing.json');
    try {
      for (const f of m.files) {
        const target = safePath(root,f.path), saved = safePath(backup,f.path); noLinks(target);
        await fsp.mkdir(path.dirname(target),{recursive:true}); await fsp.mkdir(path.dirname(saved),{recursive:true});
        const existed = fs.existsSync(target);
        if (existed) await fsp.copyFile(target,saved,fs.constants.COPYFILE_EXCL);
        journal.files.push({path:f.path,existed}); await atomicJson(journalFile,journal);
        const temp = target+'.foam-update'; noLinks(temp);
        await fsp.copyFile(safePath(folder,f.path),temp,fs.constants.COPYFILE_EXCL);
        await fsp.rename(temp,target);
      }
      await atomicJson(path.join(store,'app-current.json'),pointer);
      await fsp.unlink(journalFile); await fsp.unlink(path.join(store,'app-pending.json'));
      const required=await requiredApp(root,config);if(!required)await fsp.rm(path.join(store,'app-required.json'),{force:true});
      return {ok:true,message:'App update installed.',version:m.version};
    } catch (e) { await recoverApp(root); throw e; }
  });
}
export async function recoverApp(root) {
  const store = path.join(root,'updates'), file = path.join(store,'app-installing.json');
  const journal = await readJson(file); if (!journal) return;
  if (journal.schema !== 1 || !/^app-backup-[a-f0-9-]{36}$/.test(journal.backup) || !Array.isArray(journal.files)) throw Error('Invalid app recovery journal.');
  const backup = safePath(store,journal.backup);
  for (const entry of [...journal.files].reverse()) {
    if (!allowed('app',entry.path)) throw Error('Invalid recovery file.');
    const target = safePath(root,entry.path); noLinks(target);
    if (entry.existed) { const saved = safePath(backup,entry.path); noLinks(saved); await fsp.copyFile(saved,target); }
    else await fsp.rm(target,{force:true});
    await fsp.rm(target+'.foam-update',{force:true});
  }
  await fsp.unlink(file);
}
