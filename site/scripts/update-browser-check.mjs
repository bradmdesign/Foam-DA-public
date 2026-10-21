import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {spawn} from 'node:child_process';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {createLibraryServer} from './serve-local.mjs';

// A new headless browser and synthetic public page; never attaches to user tabs.
const here=path.dirname(fileURLToPath(import.meta.url)),repo=path.resolve(here,'../..');
const output=path.join(repo,'prototype/widescreen/build/update-browser-check');await fs.mkdir(output,{recursive:true});
const temp=await fs.mkdtemp(path.join(os.tmpdir(),'foam-update-browser-'));
let server;
try {
  const root=path.join(temp,'client'),updates=path.join(temp,'updates'),ads=path.join(updates,'ads/1-000000000000');
  await fs.mkdir(root,{recursive:true});await fs.mkdir(ads,{recursive:true});
  await fs.copyFile(path.join(repo,'prototype/widescreen/theme/da-local.ttf'),path.join(root,'da-local.ttf'));
  await fs.writeFile(path.join(ads,'campaigns.json'),JSON.stringify({schema:1,campaigns:[{id:'beardog',title:'BearDog Games',text:'Studio news and updates',href:'https://beardog.games/'}]}));
  await fs.writeFile(path.join(updates,'ads-current.json'),JSON.stringify({release:'1-000000000000',revision:1}));
  const css=await fs.readFile(path.join(repo,'prototype/widescreen/theme/assets.css'),'utf8');
  await fs.writeFile(path.join(root,'index.html'),`<!doctype html><html><head><title>Update preview</title><style>${css}*{box-sizing:border-box}body{margin:0;background:#2e2013;color:#dbbe8e;font:14px "DA Local Bitmap",monospace}.page-surface{padding:5px;max-width:100%}main{padding:8px}h1{font-size:16px;margin:6px 0 12px}p{line-height:1.4}.row{display:flex;justify-content:space-between;padding:7px 0;border-bottom:1px solid #6a4b2b}a{color:inherit}</style></head><body><div class="page-surface"><main><h1>Archer · Skills &amp; Spells</h1><p>The updater keeps records and search together. This is an isolated preview.</p><div class="row"><span>Archery</span><span>Ability 1</span></div><div class="row"><span>Arrow Shot</span><span>Ability 1</span></div></main></div></body></html>`);
  server=createLibraryServer({root,updateRoot:updates});await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const url='http://127.0.0.1:'+server.address().port;
  const browser='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
  for(const [name,width,height]of [['corner',600,260],['library',1200,700]]){
    const screenshot=path.join(output,name+'.png');
    const args=['--headless=new','--disable-gpu','--no-first-run','--disable-background-networking','--disable-component-update','--disable-extensions','--user-data-dir='+path.join(temp,'browser-'+name),'--window-size='+width+','+height,'--screenshot='+screenshot,'--virtual-time-budget=2500','--dump-dom',url];
    const child=spawn(browser,args,{windowsHide:true,stdio:['ignore','pipe','pipe']});let dom='',errors='';
    child.stdout.on('data',chunk=>dom+=chunk);child.stderr.on('data',chunk=>errors+=chunk);
    const deadline=setTimeout(()=>child.kill(),45000);
    const code=await new Promise((resolve,reject)=>{child.on('error',reject);child.on('exit',resolve)});clearTimeout(deadline);
    assert.equal(code,0,errors.slice(-500));assert.match(dom,/id="foam-sponsored"/);assert.doesNotMatch(dom,/>Sponsored</);assert.match(dom,/BearDog Games/);assert.match(dom,/href="\/_foam\/go\/beardog"/);
    assert.ok((await fs.stat(screenshot)).size>1000);await fs.writeFile(path.join(output,name+'.html'),dom);
    console.log(name+': themed ad strip and explicit sponsor link rendered; '+screenshot);
  }
}finally{
  if(server)await new Promise(resolve=>server.close(resolve));
  // Only this newly created fixture folder is removed; user browser profiles are untouched.
  if(path.dirname(temp)!==os.tmpdir()||!path.basename(temp).startsWith('foam-update-browser-'))throw Error('Unexpected fixture cleanup path.');
  await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:100});
}
