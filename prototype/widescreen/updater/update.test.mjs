import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import {gzipSync} from 'node:zlib';
import {address,allowed,safePath,hash,fileHash,verifyFeed,validateManifest,validateCampaigns,writePack,extractPack,update,installApp,recoverApp,atomicJson,readJson,withLock} from './core.mjs';
import {createLibraryServer} from '../../../site/scripts/serve-local.mjs';

const {publicKey,privateKey}=crypto.generateKeyPairSync('rsa',{modulusLength:3072});
const config={feed_url:'https://example.invalid/feed.json',public_key:publicKey.export({type:'spki',format:'pem'})};
async function fixture(t) {const root=await fs.mkdtemp(path.join(os.tmpdir(),'foam-updater-'));t.after(()=>fs.rm(root,{recursive:true,force:true}));return root;}
async function release(root,component,revision,entries,remote={}) {
  const files=[];for(const[name,value]of Object.entries(entries)){const data=Buffer.isBuffer(value)?value:Buffer.from(value),source=safePath(root,'source-'+revision+'/'+name);await fs.mkdir(path.dirname(source),{recursive:true});await fs.writeFile(source,data);files.push({path:name,source,bytes:data.length,sha256:hash(data)});}
  const pack=path.join(root,component+'-'+revision+'.fup');await writePack(files,pack);
  const manifest={schema:1,component,revision,version:'1.'+revision,package:path.basename(pack),bytes:(await fs.stat(pack)).size,sha256:await fileHash(pack),files:files.map(({source,...f})=>f)};
  const raw=Buffer.from(JSON.stringify(manifest)),descriptor={revision,version:manifest.version,manifest:component+'-'+revision+'.json',manifest_sha256:hash(raw)};
  remote[manifest.package]=await fs.readFile(pack);remote[descriptor.manifest]=raw;
  return {manifest,descriptor,remote,pack};
}
function sign(remote,components) {const bytes=Buffer.from(JSON.stringify({schema:1,components}));remote['feed.json']=bytes;remote['feed.json.sig']=crypto.sign('sha256',bytes,privateKey);}
function fetcher(remote,calls=[]) {return async(url,limit,destination)=>{const name=new URL(url).pathname.split('/').at(-1);calls.push(name);const data=remote[name];if(!data)throw Error('Missing remote fixture.');if(data.length>limit)throw Error('Download limit.');if(destination){await fs.writeFile(destination,data,{flag:'wx'});return data.length}return data;};}

test('signed feed rejects tampering and untrusted publisher keys',()=>{
  const bytes=Buffer.from('{"schema":1,"components":{}}'),sig=crypto.sign('sha256',bytes,privateKey);
  assert.equal(verifyFeed(bytes,sig,config.public_key).schema,1);
  assert.throws(()=>verifyFeed(Buffer.concat([bytes,Buffer.from(' ')]),sig,config.public_key),/signature/);
  assert.throws(()=>verifyFeed(bytes,Buffer.alloc(384),config.public_key),/signature/);
});
test('paths and component boundaries exclude game data, secrets, links and scripts in ads',()=>{
  for(const name of ['../escape','/root','a\\b','a:b','a//b','CON.txt','a./b','a /b','a\0b'])assert.throws(()=>safePath(os.tmpdir(),name),/path/);
  for(const name of ['runtime/Darkages.exe','screenshots/a.png','characters/a.json','dist/client/.env','dist/client/private/a.json','dist/client/account.dpapi'])assert.equal(allowed('library',name),false,name);
  for(const name of ['campaigns.js','images/ad.svg','images/a.html','images/../a.png']){try{assert.equal(allowed('ads',name),false)}catch(e){assert.match(e.message,/path/)}}
  assert.equal(allowed('app','client-patches/1088x640/client.patch.gz'),true);assert.equal(allowed('app','setoa.dat'),false);
  assert.throws(()=>address('http://example.com/feed.json'),/HTTPS/);assert.throws(()=>address('https://u:p@example.com/f'),/HTTPS/);
});
test('pack extraction verifies exact bytes, truncation and checksum',async t=>{
  const root=await fixture(t),r=await release(root,'library',1,{'dist/client/index.html':'<html>old</html>','dist/client/a.png':Buffer.from([0,1,255,3])});
  validateManifest(r.manifest,'library',r.descriptor);await extractPack(r.pack,r.manifest,path.join(root,'extracted'));
  assert.deepEqual(await fs.readFile(path.join(root,'extracted/dist/client/a.png')),Buffer.from([0,1,255,3]));
  const damaged=structuredClone(r.manifest);damaged.files[0].sha256='0'.repeat(64);await assert.rejects(extractPack(r.pack,damaged,path.join(root,'bad')),/checksum/);
  const truncated=path.join(root,'truncated.fup');await fs.writeFile(truncated,(await fs.readFile(r.pack)).subarray(0,30));await assert.rejects(extractPack(truncated,r.manifest,path.join(root,'truncated')),/Truncated|unexpected end|invalid|size/i);
});
test('Library activation is atomic, repeated checks skip packages, replay is rejected',async t=>{
  const root=await fixture(t),remote={},calls=[];
  const a=await release(root,'library',1,{'dist/client/index.html':'<html>one</html>'},remote);sign(remote,{library:a.descriptor});
  assert.deepEqual((await update(root,config,fetcher(remote,calls))).changed,['library']);
  const first=await readJson(path.join(root,'updates/library-current.json'));assert.equal(first.revision,1);
  calls.length=0;assert.deepEqual((await update(root,config,fetcher(remote,calls))).changed,[]);assert.deepEqual(calls,['feed.json','feed.json.sig']);
  const b=await release(root,'library',2,{'dist/client/index.html':'<html>two</html>'},remote);sign(remote,{library:b.descriptor});
  await update(root,config,fetcher(remote));assert.equal((await readJson(path.join(root,'updates/library-previous.json'))).revision,1);
  sign(remote,{library:a.descriptor});await assert.rejects(update(root,config,fetcher(remote)),/older/);
  assert.equal((await readJson(path.join(root,'updates/library-current.json'))).revision,2);
});
test('bad or interrupted downloads never activate and release the update lock',async t=>{
  const root=await fixture(t),r=await release(root,'library',1,{'dist/client/index.html':'good'});sign(r.remote,{library:r.descriptor});
  const original=r.remote[r.manifest.package];r.remote[r.manifest.package]=Buffer.from('broken');
  await assert.rejects(update(root,config,fetcher(r.remote)),/checksum/);assert.equal(await readJson(path.join(root,'updates/library-current.json')),null);
  assert.equal((await fs.readdir(path.join(root,'updates/library'))).length,0);
  r.remote[r.manifest.package]=original;await update(root,config,fetcher(r.remote));
  assert.equal((await readJson(path.join(root,'updates/library-current.json'))).revision,1);
});

test('bundled Library baseline avoids a duplicate download and accepts the next signed snapshot',async t=>{
  const root=await fixture(t),remote={},calls=[];
  const first=await release(root,'library',1,{'dist/client/index.html':'bundled','dist/client/pagefind/index.pf_meta':Buffer.from([0,255]),'dist/client/pagefind/chunk.pf_fragment':Buffer.from([1,2])},remote);
  sign(remote,{library:first.descriptor});
  const bundled={...config,installed_library:first.descriptor};
  assert.deepEqual((await update(root,bundled,fetcher(remote,calls))).changed,[]);
  assert.deepEqual(calls,['feed.json','feed.json.sig']);
  const second=await release(root,'library',2,{'dist/client/index.html':'new','dist/client/pagefind/index.pf_index':Buffer.from([3,4])},remote);
  sign(remote,{library:second.descriptor});
  assert.deepEqual((await update(root,bundled,fetcher(remote))).changed,['library']);
  assert.deepEqual(await fs.readFile(path.join(root,'updates/library',(await readJson(path.join(root,'updates/library-current.json'))).release,'dist/client/pagefind/index.pf_index')),Buffer.from([3,4]));
});
test('bad signature and changed revision cannot replace existing content',async t=>{
  const root=await fixture(t),a=await release(root,'library',1,{'dist/client/index.html':'one'});sign(a.remote,{library:a.descriptor});await update(root,config,fetcher(a.remote));
  a.remote['feed.json.sig']=Buffer.alloc(384);await assert.rejects(update(root,config,fetcher(a.remote)),/signature/);
  sign(a.remote,{library:{...a.descriptor,manifest_sha256:'0'.repeat(64)}});await assert.rejects(update(root,config,fetcher(a.remote)),/changed in place/);
  assert.equal((await readJson(path.join(root,'updates/library-current.json'))).revision,1);
});
test('scheduled ads accept only data and local images; ads can be removed independently',async t=>{
  const root=await fixture(t);assert.throws(()=>validateCampaigns({schema:1,campaigns:[{id:'x',title:'x',href:'javascript:alert(1)'}]}),/HTTPS/);
  const remote={},a=await release(root,'ads',1,{'campaigns.json':JSON.stringify({schema:1,campaigns:[{id:'one',title:'Sponsor',image:'images/test.png',href:'https://example.com'}]}),'images/test.png':Buffer.from([1,2,3])},remote);sign(remote,{ads:a.descriptor});await update(root,config,fetcher(remote));
  const b=await release(root,'ads',2,{'campaigns.json':'{"schema":1,"campaigns":[]}'},remote);sign(remote,{ads:b.descriptor});await update(root,config,fetcher(remote));assert.equal((await readJson(path.join(root,'updates/ads-current.json'))).revision,2);
  assert.equal(await readJson(path.join(root,'updates/library-current.json')),null);
});
test('app update stays staged until installation; personal files survive',async t=>{
  const root=await fixture(t);await fs.writeFile(path.join(root,'Foam-DA.exe'),'old exe');await fs.mkdir(path.join(root,'screenshots'));await fs.writeFile(path.join(root,'screenshots/personal.png'),'private');
  const r=await release(root,'app',1,{'Foam-DA.exe':'new exe','host-dock.css':'new css'});sign(r.remote,{app:r.descriptor});await update(root,config,fetcher(r.remote));assert.equal(await fs.readFile(path.join(root,'Foam-DA.exe'),'utf8'),'old exe');
  await installApp(root,config);assert.equal(await fs.readFile(path.join(root,'Foam-DA.exe'),'utf8'),'new exe');assert.equal(await fs.readFile(path.join(root,'screenshots/personal.png'),'utf8'),'private');assert.equal(await readJson(path.join(root,'updates/app-pending.json')),null);
});
test('staged app is reverified and interrupted installation can be restored',async t=>{
  const root=await fixture(t),r=await release(root,'app',1,{'Foam-DA.exe':'new'});sign(r.remote,{app:r.descriptor});await update(root,config,fetcher(r.remote));
  const p=await readJson(path.join(root,'updates/app-pending.json'));await fs.writeFile(path.join(root,'updates/app',p.release,'Foam-DA.exe'),'bad');await assert.rejects(installApp(root,config),/damaged/);
  const backup='app-backup-'+crypto.randomUUID();await fs.mkdir(path.join(root,'updates',backup));await fs.writeFile(path.join(root,'updates',backup,'Foam-DA.exe'),'original');await fs.writeFile(path.join(root,'Foam-DA.exe'),'partial');
  await atomicJson(path.join(root,'updates/app-installing.json'),{schema:1,backup,files:[{path:'Foam-DA.exe',existed:true}]});await recoverApp(root);assert.equal(await fs.readFile(path.join(root,'Foam-DA.exe'),'utf8'),'original');
});
test('a damaged pending app can be downloaded again without accepting a replay',async t=>{
  const root=await fixture(t),r=await release(root,'app',1,{'Foam-DA.exe':'verified app'});sign(r.remote,{app:r.descriptor});await update(root,config,fetcher(r.remote));
  const p=await readJson(path.join(root,'updates/app-pending.json'));await fs.writeFile(path.join(root,'updates/app',p.release,'Foam-DA.exe'),'damaged');
  assert.deepEqual((await update(root,config,fetcher(r.remote))).changed,['app']);await installApp(root,config);assert.equal(await fs.readFile(path.join(root,'Foam-DA.exe'),'utf8'),'verified app');
  assert.ok((await fs.readdir(path.join(root,'updates/app'))).some(n=>n.startsWith('damaged-')));
});
test('two checks cannot mutate an installation at the same time',async t=>{
  const root=await fixture(t);let unblock;const first=withLock(root,()=>new Promise(resolve=>{unblock=resolve}));
  while(!unblock)await new Promise(resolve=>setImmediate(resolve));await assert.rejects(withLock(root,()=>{}),/already running/);unblock();await first;
});
test('content retention keeps current/previous and preserves unrecognized user files',async t=>{
  const root=await fixture(t),remote={};
  for(let i=1;i<=3;i++){
    const r=await release(root,'library',i,{'dist/client/index.html':'version '+i},remote);sign(remote,{library:r.descriptor});await update(root,config,fetcher(remote));
  }
  assert.equal((await fs.readdir(path.join(root,'updates/library'))).length,2);
  const p=await readJson(path.join(root,'updates/library-previous.json'));
  await fs.writeFile(path.join(root,'updates/library',p.release,'my-note.txt'),'keep');
  for(let i=4;i<=5;i++){const r=await release(root,'library',i,{'dist/client/index.html':'version '+i},remote);sign(remote,{library:r.descriptor});await update(root,config,fetcher(remote));}
  assert.equal(await fs.readFile(path.join(root,'updates/library',p.release,'my-note.txt'),'utf8'),'keep');
});
test('a freshly packaged app baseline skips its already-installed revision',async t=>{
  const root=await fixture(t),r=await release(root,'app',1,{'Foam-DA.exe':'same installed exe'}),calls=[];sign(r.remote,{app:r.descriptor});
  assert.deepEqual((await update(root,{...config,installed_app:r.descriptor},fetcher(r.remote,calls))).changed,[]);assert.deepEqual(calls,['feed.json','feed.json.sig']);
});
test('server switches content without restart, injects runtime and serves scheduled local ads',async t=>{
  const root=await fixture(t),bundled=path.join(root,'bundled');await fs.mkdir(bundled);await fs.writeFile(path.join(bundled,'index.html.gz'),gzipSync('<html><body><main>Old page</main></body></html>'));await fs.writeFile(path.join(bundled,'deleted.html'),'removed record');
  const server=createLibraryServer({root:bundled,updateRoot:path.join(root,'updates')});await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));t.after(()=>new Promise(resolve=>server.close(resolve)));const base='http://127.0.0.1:'+server.address().port;
  assert.match(await (await fetch(base)).text(),/runtime.js/);
  const remote={},a=await release(root,'library',1,{'dist/client/index.html':'<html><body>New page</body></html>'},remote),b=await release(root,'ads',2,{'campaigns.json':JSON.stringify({schema:1,campaigns:[{id:'live',title:'Live',href:'https://example.com'},{id:'future',title:'Future',starts:'2099-01-01'}]})},remote);sign(remote,{library:a.descriptor,ads:b.descriptor});await update(root,config,fetcher(remote));
  assert.match(await(await fetch(base)).text(),/New page/);assert.deepEqual(await(await fetch(base+'/_foam/status.json')).json(),{library:1,ads:2});assert.deepEqual((await(await fetch(base+'/_foam/campaigns.json')).json()).campaigns.map(c=>c.id),['live']);
  assert.equal((await fetch(base+'/deleted.html')).status,404);
  assert.equal((await fetch(base+'/_foam/runtime.js')).status,200);assert.equal((await fetch(base+'/_foam/go/live',{redirect:'manual'})).headers.get('location'),'https://example.com');assert.equal((await fetch(base+'/_foam/go/unknown',{redirect:'manual'})).status,404);assert.equal((await fetch(base,{method:'POST'})).status,405);
});
