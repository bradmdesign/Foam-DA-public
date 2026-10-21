// Screenshots of the real built Library in a new isolated headless browser.
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {createLibraryServer} from '../site/scripts/serve-local.mjs';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const output=path.join(root,'docs/demo');await fs.mkdir(output,{recursive:true});
const temp=await fs.mkdtemp(path.join(os.tmpdir(),'foam-release-demo-'));
let server;
try {
  const runtime=path.join(temp,'runtime');await fs.mkdir(runtime);
  await fs.copyFile(path.join(root,'site/scripts/companion-runtime.js'),path.join(runtime,'companion-runtime.js'));
  const css=await Promise.all(['prototype/widescreen/theme/assets.css','prototype/widescreen/host-dock.css','site/scripts/companion-runtime.css'].map(f=>fs.readFile(path.join(root,f),'utf8')));
  await fs.writeFile(path.join(runtime,'companion-runtime.css'),css.join('\n'));
  server=createLibraryServer({root:path.join(root,'site/dist/client'),runtimeRoot:runtime});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const url='http://127.0.0.1:'+server.address().port+'/classes/rogue/';
  const args=['--headless=new','--disable-gpu','--no-first-run','--disable-background-networking','--disable-component-update','--disable-extensions','--user-data-dir='+path.join(temp,'browser'),'--window-size=1280,850','--screenshot='+path.join(output,'library.png'),'--virtual-time-budget=3000','--dump-dom',url];
  const child=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args,{windowsHide:true,stdio:['ignore','pipe','pipe']});
  let dom='',errors='';child.stdout.on('data',b=>dom+=b);child.stderr.on('data',b=>errors+=b);
  const deadline=setTimeout(()=>child.kill(),45000);
  const code=await new Promise((resolve,reject)=>{child.on('error',reject);child.on('exit',resolve)});clearTimeout(deadline);
  await fs.mkdir(path.join(root,'installer/build'),{recursive:true});await fs.writeFile(path.join(root,'installer/build/demo-dom.html'),dom);
  if(code!==0||!dom.includes('Archer'))throw Error('Library screenshot failed: '+errors.slice(-400));
  await fs.copyFile(path.join(root,'prototype/widescreen/build/updater-ui-check.png'),path.join(output,'updates.png'));
  console.log('Captured the built Library and actual native update settings.');
}finally{
  if(server)await new Promise(resolve=>server.close(resolve));
  if(path.dirname(temp)!==os.tmpdir()||!path.basename(temp).startsWith('foam-release-demo-'))throw Error('Unexpected screenshot cleanup path.');
  await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:100});
}
