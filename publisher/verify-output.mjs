import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {update,installApp,fileHash,readJson} from '../prototype/widescreen/updater/core.mjs';

// Exercises the real locally signed output in a disposable app, never this host.
const here=path.dirname(fileURLToPath(import.meta.url)),root=await fs.mkdtemp(path.join(os.tmpdir(),'foam-publisher-check-'));
try{
  const config=await readJson(path.join(here,'../prototype/widescreen/updater/channel.json'));delete config.installed_app;delete config.installed_library;
  const fetch=async(url,limit,dest)=>{const file=path.join(here,'output',new URL(url).pathname.split('/').at(-1));const bytes=await fs.readFile(file);assert.ok(bytes.length<=limit);if(dest){await fs.writeFile(dest,bytes,{flag:'wx'});return bytes.length}return bytes};
  await fs.writeFile(path.join(root,'Foam-DA.exe'),'previous app fixture');await fs.mkdir(path.join(root,'screenshots'));await fs.writeFile(path.join(root,'screenshots/personal.txt'),'preserve');
  const result=await update(root,config,fetch);const available=await readJson(path.join(here,'output/feed.json'));assert.deepEqual(result.changed,['library','ads','app'].filter(c=>available.components[c]));assert.equal(await fs.readFile(path.join(root,'Foam-DA.exe'),'utf8'),'previous app fixture');
  await installApp(root,config);
  const feed=await readJson(path.join(here,'output/feed.json')),manifest=await readJson(path.join(here,'output',feed.components.app.manifest));
  for(const file of manifest.files)assert.equal(await fileHash(path.join(root,file.path)),file.sha256);
  assert.equal(await fs.readFile(path.join(root,'screenshots/personal.txt'),'utf8'),'preserve');
  console.log(JSON.stringify({real_publisher_signature:'passed',download_verify_stage_install:'passed',app_files:manifest.files.length,personal_files:'preserved',game_data:'not included'}));
}finally{
  if(path.dirname(root)!==os.tmpdir()||!path.basename(root).startsWith('foam-publisher-check-'))throw Error('Unexpected verification cleanup path.');
  await fs.rm(root,{recursive:true,force:true});
}
