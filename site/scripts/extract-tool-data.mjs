import fs from 'node:fs';
const ledger=Object.values(JSON.parse(fs.readFileSync('research/coverage.json','utf8'))),out={};
for(const [key,file] of [['monk','typeracermonk.html'],['depths','typeracerud.html']]){const e=ledger.find(e=>e.url==='https://vorlof.com/general/'+file&&e.status==='fetched');if(!e)throw new Error('Cached typing data missing: '+file);const raw=fs.readFileSync(e.cache,'utf8');const m=raw.match(/let phrasesList\s*=\s*(\[[\s\S]*?\]);/);if(!m)throw new Error('Phrase list missing');out[key]=JSON.parse(m[1]);}
fs.writeFileSync('public/data/typing.json',JSON.stringify(out));
