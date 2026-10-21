import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import https from 'node:https';
import {spawn,execFileSync} from 'node:child_process';
import {fileURLToPath,pathToFileURL} from 'node:url';

test('HTTPS transport uses normal certificate validation, bounds downloads and rejects HTTP redirects',async t=>{
  const root=await fs.mkdtemp(path.join(os.tmpdir(),'foam-update-tls-'));t.after(()=>fs.rm(root,{recursive:true,force:true}));
  const key=path.join(root,'test.key'),cert=path.join(root,'test.crt');
  const config=path.join(root,'openssl.cnf');await fs.writeFile(config,'[req]\ndistinguished_name=dn\n[dn]\nCN=localhost\n');
  execFileSync('openssl',['req','-config',config,'-x509','-newkey','rsa:2048','-noenc','-keyout',key,'-out',cert,'-subj','/CN=localhost','-addext','subjectAltName=DNS:localhost','-days','1'],{windowsHide:true,stdio:['ignore','ignore','pipe']});
  const requests=[];
  const server=https.createServer({key:await fs.readFile(key),cert:await fs.readFile(cert)},(req,res)=>{
    requests.push(req.headers);
    if(req.url==='/redirect'){res.writeHead(302,{Location:'/ok'});res.end();}
    else if(req.url==='/downgrade'){res.writeHead(302,{Location:'http://localhost/insecure'});res.end();}
    else if(req.url==='/oversized'){res.writeHead(200,{'Content-Length':'9999'});res.end('x');}
    else if(req.url==='/stream'){res.writeHead(200);res.write('1234');res.end('567890');}
    else {res.writeHead(200);res.end('valid update bytes');}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));t.after(()=>new Promise(resolve=>server.close(resolve)));
  const base='https://localhost:'+server.address().port;
  const moduleUrl=pathToFileURL(path.join(path.dirname(fileURLToPath(import.meta.url)),'core.mjs')).href;
  const script=path.join(root,'check.mjs');
  await fs.writeFile(script,`import {download} from ${JSON.stringify(moduleUrl)};import assert from 'node:assert/strict';const base=process.argv[2];if(process.argv[3]==='untrusted'){await assert.rejects(download(base+'/ok',100));}else{assert.equal((await download(base+'/redirect',100)).toString(),'valid update bytes');await assert.rejects(download(base+'/downgrade',100),/HTTPS/);await assert.rejects(download(base+'/oversized',10),/oversized/);await assert.rejects(download(base+'/stream',5),/limit/);await download(base+'/ok',100,process.argv[4]);}`);
  async function child(mode,trust){
    const env={...process.env};delete env.NODE_TLS_REJECT_UNAUTHORIZED;delete env.NODE_EXTRA_CA_CERTS;if(trust)env.NODE_EXTRA_CA_CERTS=cert;
    const processChild=spawn(process.execPath,[script,base,mode,path.join(root,'download.bin')],{env,windowsHide:true,stdio:['ignore','pipe','pipe']});let error='';processChild.stderr.on('data',chunk=>error+=chunk);return new Promise((resolve,reject)=>{processChild.on('error',reject);processChild.on('exit',code=>code===0?resolve():reject(Error(error)))});
  }
  await child('untrusted',false);await child('trusted',true);assert.equal(await fs.readFile(path.join(root,'download.bin'),'utf8'),'valid update bytes');
  for(const headers of requests){assert.equal(headers.cookie,undefined);assert.equal(headers.authorization,undefined);assert.equal(headers['user-agent'],'Foam-DA-Updater/1');}
});
