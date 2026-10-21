import path from 'node:path';
import fs from 'node:fs';
import {fileURLToPath} from 'node:url';
import {spawn} from 'node:child_process';
import {setTimeout as delay} from 'node:timers/promises';
import {atomicJson, readJson, update, prepareLaunch, installApp, recoverApp, withLock} from './core.mjs';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url))), store = path.join(root,'updates');
const command = process.argv[2];
let hostExited=false;
function reopenHost(){const host=path.join(root,'Foam-DA.exe');if(fs.existsSync(host)){const child=spawn(host,['--host'],{cwd:root,detached:true,stdio:'ignore',windowsHide:true});child.unref();}}
try {
  const config = await readJson(path.join(root,'updater/channel.json'));
  if (!config?.public_key || !config.feed_url) throw Error('The publisher has not configured an update channel yet.');
  let result;
  if (command === 'check') result = await update(root,config);
  else if(command==='startup')result=await prepareLaunch(root,config);
  else if (command === 'recover') { await withLock(store,()=>recoverApp(root)); result={ok:true}; }
  else if (command === 'install' || command === 'install-required' || command === 'repair') {
    const pid = Number(process.argv[3]); if (!Number.isSafeInteger(pid) || pid < 1) throw Error('Invalid host process.');
    const deadline = Date.now()+120000; let running = true;
    while (running && Date.now()<deadline) {
      try { process.kill(pid,0); await delay(300); } catch (e) { if (e.code !== 'ESRCH') throw e; running=false; }
    }
    if (running) throw Error('The host is still open. The update remains ready.');
    hostExited=true;
    await withLock(store,()=>recoverApp(root)); result = command !== 'repair' ? await installApp(root,config) : {ok:true,message:'Interrupted app update restored.'};
    reopenHost();
  } else throw Error('Use startup, check, recover or install.');
  await atomicJson(path.join(store,'last-result.json'),{...result,checked:new Date().toISOString()});
  console.log(JSON.stringify(result));
} catch (e) {
  const result={ok:false,message:e.message};
  await atomicJson(path.join(store,'last-result.json'),{...result,checked:new Date().toISOString()}).catch(()=>{});
  // If installation was rejected or rolled back, return to the working host.
  // An unfinished recovery journal is left for the explicit repair path.
  if(hostExited&&command!=='install-required'&&!fs.existsSync(path.join(store,'app-installing.json')))reopenHost();
  console.log(JSON.stringify(result)); process.exitCode=1;
}
