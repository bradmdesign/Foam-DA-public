import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import zlib from 'node:zlib';
import http from 'node:http';
import net from 'node:net';
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {fileURLToPath} from 'node:url';
import {compressLocal} from './compress-local.mjs';

const here=path.dirname(fileURLToPath(import.meta.url));
test('Compact export remains readable through ordinary HTTP, gzip HTTP and component requests',async()=>{
  const fixture=fs.mkdtempSync(path.join(os.tmpdir(),'da-compact-export-'));
  const root=path.join(fixture,'dist/client');fs.mkdirSync(root,{recursive:true});
  const html='<html><head><title>Library</title></head><body><h1>Library</h1>'+('known reference content '.repeat(300))+'</body></html>';
  const component=JSON.stringify({component:'reference',content:'known component content '.repeat(150)});
  fs.writeFileSync(path.join(root,'index.html'),html);fs.writeFileSync(path.join(root,'reference.rsc'),component);fs.writeFileSync(path.join(root,'small.html'),'<h1>Small page</h1>');
  // A stale compressed neighbour must be regenerated, not served as current data.
  fs.writeFileSync(path.join(root,'index.html.gz'),zlib.gzipSync('older content'));
  let child;
  try{
    const result=compressLocal(root);assert.equal(result.files,2);assert.equal(fs.existsSync(path.join(root,'index.html')),false);assert.equal(fs.existsSync(path.join(root,'reference.rsc')),false);assert.equal(fs.readFileSync(path.join(root,'small.html'),'utf8'),'<h1>Small page</h1>');
    const reserve=net.createServer();reserve.listen(0,'127.0.0.1');await once(reserve,'listening');const port=reserve.address().port;await new Promise(resolve=>reserve.close(resolve));
    child=spawn(process.execPath,[path.join(here,'serve-local.mjs')],{cwd:fixture,env:{...process.env,PORT:String(port)},windowsHide:true,stdio:['ignore','pipe','pipe']});
    const ready=once(child.stdout,'data');await Promise.race([ready,new Promise((_,reject)=>{const timeout=setTimeout(()=>reject(new Error('Fixture server startup timeout')),5000);timeout.unref();})]);
    const read=(route,encoding)=>new Promise((resolve,reject)=>{http.get({host:'127.0.0.1',port,path:route,headers:{'Accept-Encoding':encoding}},response=>{const chunks=[];response.on('data',c=>chunks.push(c));response.on('end',()=>resolve({status:response.statusCode,headers:response.headers,bytes:Buffer.concat(chunks)}));response.on('error',reject);}).on('error',reject);});
    const ordinary=await read('/','gzip;q=0');assert.equal(ordinary.status,200);assert.equal(ordinary.headers['content-encoding'],undefined);assert.equal(ordinary.bytes.toString(),html);
    const compressed=await read('/','gzip');assert.equal(compressed.headers['content-encoding'],'gzip');assert.equal(zlib.gunzipSync(compressed.bytes).toString(),html);
    const rsc=await read('/reference.rsc','identity');assert.equal(rsc.status,200);assert.match(rsc.headers['content-type'],/text\/x-component/);assert.equal(rsc.bytes.toString(),component);
  }finally{if(child&&!child.killed){const closed=once(child,'exit');child.kill();await closed;}fs.rmSync(fixture,{recursive:true,force:true});}
});
test('Keep-originals remains available for exports hosted by other static servers',()=>{
  const fixture=fs.mkdtempSync(path.join(os.tmpdir(),'da-static-export-'));
  try{const input='reference '.repeat(300);fs.writeFileSync(path.join(fixture,'page.html'),input);compressLocal(fixture,true);assert.equal(fs.readFileSync(path.join(fixture,'page.html'),'utf8'),input);assert.equal(zlib.gunzipSync(fs.readFileSync(path.join(fixture,'page.html.gz'))).toString(),input);}finally{fs.rmSync(fixture,{recursive:true,force:true});}
});
