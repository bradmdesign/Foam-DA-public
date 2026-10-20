import fs from 'node:fs';
import {load} from 'cheerio';
const [section='quests',start='0',count='12']=process.argv.slice(2);
const all=Object.values(JSON.parse(fs.readFileSync('research/coverage.json','utf8')));
console.log(JSON.stringify(all.reduce((a,e)=>(a[e.status]=(a[e.status]||0)+1,a),{})));
const entries=all.filter(e=>e.status==='fetched'&&e.source==='Vorlof'&&new URL(e.url).pathname.startsWith('/'+section+'/')).sort((a,b)=>a.url.localeCompare(b.url));
console.log(`${section}: ${entries.length} retrieved`);
for(const e of entries.slice(+start,+start+(+count))){
 const html=fs.readFileSync(e.cache,'utf8');
 const body=html.match(/<!-- InstanceBeginEditable name="Content" -->([\s\S]*?)<!-- InstanceEndEditable -->/)?.[1];
 const $=load(body||html);
 $('script,style,nav,header,footer').remove();
 if(process.argv.includes('--prose'))$('table').each((_,t)=>{if(!$(t).find('table').length){const rows=$(t).find('tr');const heading=rows.first().text().replace(/\s+/g,' ').trim();$(t).replaceWith('\n[Data table: '+rows.length+' rows; '+heading.slice(0,180)+']\n')}});
 $('br').replaceWith('\n'); $('td,th').append(' | ');$('tr,p,h1,h2,h3,h4,li').append('\n');
 console.log('\nSOURCE '+e.url+'\n'+$.root().text().replace(/[ \t]+/g,' ').replace(/\n\s*\n/g,'\n').trim());
}
