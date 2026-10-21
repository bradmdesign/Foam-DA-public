import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {allowed, address, hash, fileHash, writePack, extractPack, atomicJson, readJson, validateFeed, validateManifest, validateCampaigns, noLinks, verifyFeed} from '../prototype/widescreen/updater/core.mjs';

const here=path.dirname(fileURLToPath(import.meta.url)), root=path.dirname(here), host=path.join(root,'prototype/widescreen');
const [component,version,revisionText,requestedUrl]=process.argv.slice(2);
const revision=Number(revisionText), signer=path.join(here,'build/Foam-Publisher.exe');
const key=path.join(process.env.LOCALAPPDATA,'Foam-DA/Publisher/signing-key.dpapi'), channelFile=path.join(host,'updater/channel.json');
try {
  const feedUrl=requestedUrl||(await readJson(channelFile))?.feed_url;
  address(feedUrl);
  if(component==='init') {
    const publicXml=path.join(here,'build/public-key.xml');
    execFileSync(signer,['--ensure',key,publicXml],{windowsHide:true});
    const xml=await fsp.readFile(publicXml,'utf8');
    const field=name=>Buffer.from(xml.match(new RegExp('<'+name+'>([^<]+)</'+name+'>'))[1],'base64').toString('base64url');
    const public_key=crypto.createPublicKey({key:{kty:'RSA',n:field('Modulus'),e:field('Exponent')},format:'jwk'}).export({type:'spki',format:'pem'});
    const prior=await readJson(channelFile);
    if(prior?.public_key && prior.public_key!==public_key) throw Error('The pinned publisher key differs. Restore the original key.');
    await atomicJson(channelFile,{...prior,schema:1,feed_url:feedUrl,public_key,automatic:true});
    console.log('Channel configured. Private signing material stays Windows-protected outside the project and every distributable. Nothing uploaded.');
    process.exit(0);
  }
  if(!['ads','library','app'].includes(component) || !/^[a-zA-Z0-9._-]{1,60}$/.test(version||'') || !Number.isSafeInteger(revision) || revision<1) throw Error('Choose Ads, Library or App and supply Version and a strictly increasing Revision. Run Init once first.');
  const config=await readJson(channelFile); if(!config?.public_key || !fs.existsSync(key)) throw Error('Run Publish.ps1 -Component Init once first.');
  if(config.feed_url!==feedUrl) throw Error('Use the configured feed URL, or run Init with the new URL before distributing a new client.');
  const output=path.join(here,'output'); await fsp.mkdir(output,{recursive:true});
  const feedFile=path.join(output,'feed.json'), feed=await readJson(feedFile,{schema:1,components:{}});
  validateFeed(feed);
  if(fs.existsSync(feedFile))verifyFeed(await fsp.readFile(feedFile),await fsp.readFile(feedFile+'.sig'),config.public_key);
  if(revision<=(feed.components[component]?.revision||0)) throw Error('Revision must increase. Never overwrite a published release.');
  const files=[];
  async function add(source,name) { noLinks(source); if(!allowed(component,name)) throw Error('Non-public or unsupported file: '+name); const stat=await fsp.stat(source); files.push({path:name,source,bytes:stat.size,sha256:await fileHash(source)}); }
  async function walk(folder,prefix) { noLinks(folder); for(const entry of await fsp.readdir(folder,{withFileTypes:true})) { if(component==='library'&&entry.name.startsWith('.'))continue;const file=path.join(folder,entry.name), name=prefix+entry.name; noLinks(file); if(entry.isDirectory()) await walk(file,name+'/'); else if(entry.isFile()) {if(component==='library'&&!name.endsWith('.gz')&&fs.existsSync(file+'.gz'))continue;await add(file,name);} else throw Error('Linked or special source file.'); } }
  if(component==='library') {
    const client=path.join(root,'site/dist/client'); if(!fs.existsSync(client)) throw Error('Build the offline Library first; only the public static export can be published.');
    await walk(client,'dist/client/');
  } else if(component==='ads') {
    const ads=validateCampaigns(await readJson(path.join(here,'ads/campaigns.json')));
    await add(path.join(here,'ads/campaigns.json'),'campaigns.json');
    for(const image of new Set(ads.campaigns.map(c=>c.image).filter(Boolean))) { await add(path.join(here,'ads',image),image); if(files.at(-1).bytes>2*1024*1024) throw Error('Ad images must be under 2 MB.'); }
  } else {
    for(const name of ['Foam-DA.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','host-dock.css']) await add(path.join(host,name),name);
    for(const entry of await fsp.readdir(path.join(host,'theme'))) if(allowed('app','theme/'+entry)) await add(path.join(host,'theme',entry),'theme/'+entry);
    await walk(path.join(host,'client-patches'),'client-patches/');
    for(const name of ['core.mjs','worker.mjs']) await add(path.join(host,'updater',name),'updater/'+name);
    for(const name of ['serve-local.mjs','companion-runtime.js','companion-runtime.css']) await add(path.join(root,'site/scripts',name),'library/scripts/'+name);
    await add(path.join(root,'THIRD-PARTY-NOTICES.txt'),'THIRD-PARTY-NOTICES.txt');
  }
  files.sort((a,b)=>a.path.localeCompare(b.path,'en'));
  const prefix=component+'-'+revision+'-'+version, packageName=prefix+'.fup', manifestName=prefix+'.json', pack=path.join(output,packageName);
  await writePack(files,pack);
  const manifest={schema:1,component,revision,version,package:packageName,bytes:(await fsp.stat(pack)).size,sha256:await fileHash(pack),files:files.map(({source,...f})=>f)};
  const manifestBytes=Buffer.from(JSON.stringify(manifest)+'\n');
  const descriptor={revision,version,manifest:manifestName,manifest_sha256:hash(manifestBytes)};
  validateManifest(manifest,component,descriptor);
  const verifyFolder=path.join(here,'build','verify-'+crypto.randomUUID());
  try{await extractPack(pack,manifest,verifyFolder);}finally{
    if(path.dirname(verifyFolder)!==path.join(here,'build')||!/^verify-[a-f0-9-]{36}$/.test(path.basename(verifyFolder)))throw Error('Invalid verification cleanup.');
    noLinks(verifyFolder);await fsp.rm(verifyFolder,{recursive:true,force:true});
  }
  await fsp.writeFile(path.join(output,manifestName),manifestBytes,{flag:'wx'});
  feed.components[component]=descriptor;
  const draft=path.join(here,'build','feed-draft.json');await atomicJson(draft,feed);
  execFileSync(signer,['--sign',key,draft,draft+'.sig'],{windowsHide:true});
  verifyFeed(await fsp.readFile(draft),await fsp.readFile(draft+'.sig'),config.public_key);
  await fs.promises.copyFile(draft+'.sig',feedFile+'.sig');await atomicJson(feedFile,feed);
  if(component==='app'||component==='library')await atomicJson(channelFile,{...config,['installed_'+component]:descriptor});
  console.log(JSON.stringify({component,version,revision,download_bytes:manifest.bytes,upload_folder:output,destination:new URL('.',feedUrl).href,note:'Upload immutable .fup/.json release files first; publish feed.json and feed.json.sig last. Nothing uploaded automatically.'},null,2));
} catch(e) { console.error(e.message); process.exitCode=1; }
