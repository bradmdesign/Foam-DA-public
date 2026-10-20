import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
const entries=JSON.parse(fs.readFileSync('.cache/research/extracted.json','utf8'));
const manifestPath='research/assets.json';
const manifest=fs.existsSync(manifestPath)?JSON.parse(fs.readFileSync(manifestPath,'utf8')):{};
const canonical=value=>{const u=new URL(value);u.protocol='https:';if(u.hostname==='wanderlustdb.com')u.hostname='www.wanderlustdb.com';return u.href};
const candidates=new Map();
for(const e of entries){
 if(e.source!=='Wanderlust'||['/','/index.php','/introduction.php','/story.php','/classes.php','/sites.php'].includes(new URL(e.url).pathname))continue;
 for(const im of e.images){
  if(!/\.(png|gif|jpg|jpeg|webp)$/i.test(im.url)||!/wanderlustdb\.com\//i.test(im.url))continue;
  if(/\/(images|quest)\//.test(im.url)&&!/(check|cross|yes|no)\.png/i.test(im.url))continue;
  const url=canonical(im.url);if(!candidates.has(url))candidates.set(url,{...im,url,sourcePage:e.url});
 }
}
fs.mkdirSync('public/assets/game',{recursive:true});
let processed=0,downloaded=0,blocked=false;
const queue=[...candidates.values()].sort((a,b)=>Number(/\/items\//.test(a.url))-Number(/\/items\//.test(b.url)));
const save=()=>fs.writeFileSync(manifestPath,JSON.stringify(manifest,null,2));
process.on('SIGINT',()=>{save();process.exit(0)});
async function worker(){while(queue.length&&!blocked){const im=queue.shift();if(manifest[im.url]?.status==='fetched'&&fs.existsSync('public'+manifest[im.url].path))continue;
 try{
  const response=await fetch(im.url,{headers:{'User-Agent':'DarkAgesLibraryResearch/1.0 (local reference rebuild)'},signal:AbortSignal.timeout(25000)});
  if([403,429].includes(response.status)){blocked=true;throw new Error(`Access blocked: HTTP ${response.status}`)}
  if(!response.ok)throw new Error(`HTTP ${response.status}`);
  const bytes=Buffer.from(await response.arrayBuffer());if(bytes.length>12_000_000)throw new Error('Asset exceeds 12 MB review limit');
  const type=response.headers.get('content-type')||'';if(!type.startsWith('image/'))throw new Error(`Not an image: ${type}`);
  const hash=crypto.createHash('sha256').update(bytes).digest('hex');const ext=path.extname(new URL(im.url).pathname).toLowerCase();
  const destination='/assets/game/'+hash.slice(0,20)+ext;
  let width=null,height=null;
  if(bytes.subarray(1,4).toString()==='PNG'){width=bytes.readUInt32BE(16);height=bytes.readUInt32BE(20)}
  else if(bytes.subarray(0,3).toString()==='GIF'){width=bytes.readUInt16LE(6);height=bytes.readUInt16LE(8)}
  fs.writeFileSync('public'+destination,bytes);
  manifest[im.url]={...im,status:'fetched',path:destination,sha256:hash,bytes:bytes.length,width,height,retrievedAt:new Date().toISOString()};downloaded++;
 }catch(error){manifest[im.url]={...im,status:'unavailable',reason:error.message}};
 processed++;if(processed%50===0){save();console.log(JSON.stringify({processed,downloaded,remaining:queue.length}))}
 await new Promise(r=>setTimeout(r,350));
}}
console.log(JSON.stringify({assets:queue.length}));await Promise.all([worker(),worker()]);save();console.log(JSON.stringify({done:true,downloaded,blocked,remaining:queue.length}));
