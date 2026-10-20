import fs from 'node:fs';
import {load} from 'cheerio';
const [pattern,start='0',count='5']=process.argv.slice(2);
const ledger=Object.values(JSON.parse(fs.readFileSync('research/coverage.json','utf8')));
const entries=ledger.filter(x=>x.status==='fetched'&&new RegExp(pattern).test(x.url));
console.log(`${entries.length} matching documents`);
for(const e of entries.slice(+start,+start+(+count))){const raw=fs.readFileSync(e.cache,'utf8');const d=load(raw);d('script,style,#menu,nav,header,footer').remove();let b=d('#maincontent').length?d('#maincontent'):d.root();b.find('table').each((_,el)=>{if(!d(el).find('table').length)d(el).replaceWith('[Table: '+d(el).find('tr').length+' rows]')});b.find('br').replaceWith('\n');b.find('p,h1,h2,h3,li').append('\n');console.log(e.url+'\n'+b.text().replace(/[ \t]+/g,' ').replace(/\n\s*\n/g,'\n').trim())}
