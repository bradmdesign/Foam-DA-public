import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createGunzip, gunzipSync} from 'node:zlib';

const types={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.json':'application/json; charset=utf-8','.svg':'image/svg+xml','.png':'image/png','.gif':'image/gif','.jpg':'image/jpeg','.jpeg':'image/jpeg','.webp':'image/webp','.avif':'image/avif','.ico':'image/x-icon','.wasm':'application/wasm','.txt':'text/plain; charset=utf-8','.woff2':'font/woff2','.woff':'font/woff','.ttf':'font/ttf','.rsc':'text/x-component'};
const regular=file=>{try{return fs.statSync(file).isFile()}catch{return false}};
const available=file=>regular(file)||regular(file+'.gz');
const readJson=(file,fallback)=>{try{if(fs.statSync(file).size>65536)return fallback;return JSON.parse(fs.readFileSync(file,'utf8'))}catch{return fallback}};
export function createLibraryServer({root=path.resolve('dist/client'),updateRoot=process.env.FOAM_UPDATE_ROOT,runtimeRoot=path.dirname(fileURLToPath(import.meta.url))}={}) {
  root=path.resolve(root);
  function pointer(component,previous=false) {
    if(!updateRoot)return null;
    const p=readJson(path.join(updateRoot,component+(previous?'-previous':'-current')+'.json'),null);
    return /^\d+-[a-f0-9]{12}$/.test(p?.release||'')?p:null;
  }
  function releaseRoot(component,p) {return p?path.join(updateRoot,component,p.release):null;}
  function activeAds() {
    const folder=releaseRoot('ads',pointer('ads'));if(!folder)return {schema:1,campaigns:[]};
    const value=readJson(path.join(folder,'campaigns.json'),{schema:1,campaigns:[]}),now=Date.now();
    return {schema:1,campaigns:Array.isArray(value.campaigns)?value.campaigns.filter(c=>(!c.starts||Date.parse(c.starts)<=now)&&(!c.ends||Date.parse(c.ends)>now)):[]};
  }
  return http.createServer((req,res)=>{
    try {
      if(req.method!=='GET'&&req.method!=='HEAD'){res.writeHead(405,{Allow:'GET, HEAD'});return res.end()}
      const url=new URL(req.url,'http://127.0.0.1'),requested=decodeURIComponent(url.pathname);
      const headers={'X-Content-Type-Options':'nosniff','Referrer-Policy':'no-referrer','Cache-Control':'no-cache','Content-Security-Policy':"default-src 'self'; script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'"};
      function json(value){res.writeHead(200,{...headers,'Content-Type':'application/json; charset=utf-8'});res.end(req.method==='HEAD'?undefined:JSON.stringify(value));}
      if(requested==='/_foam/status.json')return json({library:pointer('library')?.revision||0,ads:pointer('ads')?.revision||0});
      if(requested==='/_foam/campaigns.json')return json(activeAds());
      if(requested.startsWith('/_foam/go/')){
        const c=activeAds().campaigns.find(c=>c.id===requested.slice('/_foam/go/'.length));
        if(!c?.href||!/^https:\/\//.test(c.href)){res.writeHead(404);return res.end()}
        res.writeHead(302,{...headers,Location:c.href});return res.end();
      }
      let file,status=200;
      if(requested==='/_foam/runtime.js'||requested==='/_foam/runtime.css')file=path.join(runtimeRoot,requested.endsWith('.js')?'companion-runtime.js':'companion-runtime.css');
      else if(requested.startsWith('/_foam/ad-images/')) {
        const name=requested.slice('/_foam/ad-images/'.length),folder=releaseRoot('ads',pointer('ads'));
        if(!folder||!/^images\/[a-z0-9_-]+\.(png|jpg|jpeg|webp|gif)$/i.test(name)){res.writeHead(404);return res.end()}
        file=path.join(folder,name);
      }else{
        const live=releaseRoot('library',pointer('library')),prior=releaseRoot('library',pointer('library',true));
        const roots=[live?path.join(live,'dist/client'):root];
        // Keep previous assets available to pages opened before an update.
        if(live&&/\.(js|mjs|css|wasm|woff2?|ttf|png|gif|jpg|jpeg|webp|avif|svg|ico)$/.test(requested)){
          if(prior)roots.push(path.join(prior,'dist/client'));roots.push(root);
        }
        for(const candidateRoot of roots.filter(Boolean)){
          let candidate=path.resolve(candidateRoot,'.'+requested);
          if(candidate!==candidateRoot&&!candidate.startsWith(candidateRoot+path.sep)){res.writeHead(403);return res.end()}
          if(requested.endsWith('/')||(fs.existsSync(candidate)&&fs.statSync(candidate).isDirectory()))candidate=path.join(candidate,'index.html');
          if(!available(candidate)){const html=path.resolve(candidateRoot,'.'+requested.replace(/\/$/,'')+'.html');if(available(html))candidate=html;}
          if(available(candidate)){file=candidate;break;}
        }
        if(!file){status=404;file=path.join(live?path.join(live,'dist/client'):root,'404.html');}
      }
      if(!available(file)){res.writeHead(404,headers);return res.end('Not found')}
      headers['Content-Type']=types[path.extname(file)]||'application/octet-stream';
      if(path.extname(file)==='.html'){
        // Also works with existing exports; no React rebuild is needed for ads.
        let body=regular(file)?fs.readFileSync(file):gunzipSync(fs.readFileSync(file+'.gz'),{maxOutputLength:16*1024*1024});
        body=Buffer.from(body.toString('utf8').replace(/<\/body>/i,'<link rel="stylesheet" href="/_foam/runtime.css"><script defer src="/_foam/runtime.js"></script></body>'));
        headers['Content-Length']=body.length;res.writeHead(status,headers);return res.end(req.method==='HEAD'?undefined:body);
      }
      const acceptsGzip=String(req.headers['accept-encoding']||'').split(',').some(part=>{const[name,...params]=part.trim().toLowerCase().split(';');return name==='gzip'&&!params.some(p=>/^\s*q\s*=\s*0(?:\.0*)?\s*$/.test(p))});
      let decompress=false;
      if(regular(file+'.gz')&&(acceptsGzip||!regular(file))){file+='.gz';decompress=!acceptsGzip;if(acceptsGzip)headers['Content-Encoding']='gzip';}
      if(!decompress)headers['Content-Length']=String(fs.statSync(file).size);
      headers.Vary='Accept-Encoding';res.writeHead(status,headers);if(req.method==='HEAD')return res.end();
      const source=fs.createReadStream(file);source.on('error',()=>res.destroy());
      if(decompress){const decode=createGunzip();decode.on('error',()=>res.destroy());source.pipe(decode).pipe(res)}else source.pipe(res);
    }catch{if(!res.headersSent)res.writeHead(400);res.end('Invalid request')}
  });
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
  const port=Number(process.env.PORT||4173);
  const localUpdates=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../prototype/widescreen/updates');
  createLibraryServer({updateRoot:process.env.FOAM_UPDATE_ROOT||localUpdates}).listen(port,'127.0.0.1',()=>console.log('Dark Ages Library: http://127.0.0.1:'+port+'/'));
}
