import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import {pathToFileURL} from 'node:url';

export function compressLocal(root='dist/client',keepOriginals=false){
  let count=0,removedBytes=0,compressedBytes=0;
  const walk=directory=>{
    for(const entry of fs.readdirSync(directory,{withFileTypes:true})){
      const file=path.join(directory,entry.name);
      if(entry.isDirectory()){walk(file);continue;}
      if(!entry.isFile()||!/\.(html|rsc|js|css|json|wasm|svg|txt)$/.test(file))continue;
      const input=fs.readFileSync(file);if(input.length<1000)continue;
      const target=file+'.gz';
      let existing;
      try{existing=zlib.gunzipSync(fs.readFileSync(target));}catch{}
      if(!existing?.equals(input)){
        const output=zlib.gzipSync(input,{level:6});
        if(!zlib.gunzipSync(output).equals(input))throw new Error('Compressed content mismatch: '+file);
        const pending=target+'.'+process.pid+'.pending';fs.writeFileSync(pending,output);fs.renameSync(pending,target);
      }
      compressedBytes+=fs.statSync(target).size;count++;
      // serve-local supports .gz-only files for both gzip and ordinary clients.
      if(!keepOriginals){fs.unlinkSync(file);removedBytes+=input.length;}
    }
  };
  walk(root);return {files:count,removedBytes,compressedBytes,keepOriginals};
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href){
  console.log(JSON.stringify(compressLocal(process.env.DA_LIBRARY_EXPORT_ROOT||'dist/client',process.argv.includes('--keep-originals'))));
}
