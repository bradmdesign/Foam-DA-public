import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {verifyFeed,hash,validateManifest,fileHash,readJson} from '../prototype/widescreen/updater/core.mjs';
const here=path.dirname(fileURLToPath(import.meta.url));
try {
  const config=await readJson(path.join(here,'../prototype/widescreen/updater/channel.json'));
  const output=path.join(here,'output'),feed=verifyFeed(await fs.readFile(path.join(output,'feed.json')),await fs.readFile(path.join(output,'feed.json.sig')),config.public_key);
  for(const [component,descriptor]of Object.entries(feed.components)) {
    const raw=await fs.readFile(path.join(output,descriptor.manifest));if(hash(raw)!==descriptor.manifest_sha256)throw Error('Manifest checksum failed.');
    const m=validateManifest(JSON.parse(raw),component,descriptor),file=path.join(output,m.package);
    if((await fs.stat(file)).size!==m.bytes||await fileHash(file)!==m.sha256)throw Error('Release package checksum failed.');
  }
  console.log('Channel signature and all release checksums verified.');
}catch(e){console.error(e.message);process.exitCode=1;}
