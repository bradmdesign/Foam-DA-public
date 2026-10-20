import fs from 'node:fs';
import path from 'node:path';
import {load} from 'cheerio';
const ledger=JSON.parse(fs.readFileSync('research/coverage.json','utf8'));
const clean=s=>(s||'').replace(/\u00a0/g,' ').replace(/[\t ]+/g,' ').replace(/\s*\n\s*/g,'\n').trim();
export function extract(entry){
 const raw=fs.readFileSync(entry.cache,'utf8');
 const editable=raw.match(/<!-- InstanceBeginEditable name="Content" -->([\s\S]*?)<!-- InstanceEndEditable -->/)?.[1];
 const $=load(editable||raw);
 $('script,style,nav,header,footer,iframe,noscript,.sidebar,.navbar,.adsbygoogle').remove();
 let body=$.root();
 if(!editable&&entry.source==='Wanderlust'&&$('#maincontent').length)body=$('#maincontent');
 const resolve=value=>{try{return new URL(value,entry.documentUrl||entry.url).href}catch{return null}};
 const tables=[];
 body.find('table').each((_,el)=>{
  // A real catalog can contain nested stat grids. Keep its own rows while
  // rejecting the surrounding layout tables; never pull nested rows upward.
  const directRows=$(el).children('tr').add($(el).children('thead,tbody,tfoot').children('tr'));
  const isCatalog=directRows.toArray().slice(0,4).some(tr=>{
   const cells=$(tr).children('td,th');
   return cells.length>=2&&/^(Name|Monster|Enemy|Creature|Quest Title|Item|NPC)(?:\s|$)/i.test(clean(cells.first().text()));
  });
  if($(el).find('table').length&&!isCatalog)return;
  const rows=[];
  directRows.each((_,tr)=>{
   const cells=$(tr).children('td,th').map((_,td)=>{
    const copy=$(td).clone();copy.find('br').replaceWith('\n');copy.find('td,th').append(' ');
    return {text:clean(copy.text()),links:$(td).find('a[href]').map((_,a)=>({text:clean($(a).text()),url:resolve($(a).attr('href'))})).get(),images:$(td).find('img[src]').map((_,img)=>({url:resolve($(img).attr('src')),alt:$(img).attr('alt')||$(img).attr('title')||''})).get(),colspan:Number($(td).attr('colspan')||1),rowspan:Number($(td).attr('rowspan')||1)};
   }).get();
   if(cells.some(x=>x.text||x.images.length))rows.push(cells);
  });
  if(rows.length)tables.push({id:$(el).attr('id')||null,context:$(el).parents('[id]').map((_,p)=>$(p).attr('id')).get().filter(x=>!['wrapper','page-wrapper'].includes(x)),rows});
 });
 const images=body.find('img[src]').map((_,el)=>({url:resolve($(el).attr('src')),alt:$(el).attr('alt')||$(el).attr('title')||'',width:$(el).attr('width')||null,height:$(el).attr('height')||null})).get();
 const links=body.find('a[href],area[href]').map((_,el)=>({text:clean($(el).text())||$(el).attr('title')||'',url:resolve($(el).attr('href'))})).get();
 const headings=body.find('h1,h2,h3,h4,h5').map((_,el)=>clean($(el).text())).get();
 const title=entry.source==='Wanderlust' ? clean($('#maintitle_inner').text())||headings[0]||entry.title : entry.title;
 body.find('br').replaceWith('\n');body.find('td,th').append(' | ');body.find('tr,p,h1,h2,h3,h4,li').append('\n');
 return {url:entry.url,title,source:entry.source,sha256:entry.sha256,retrievedAt:entry.retrievedAt,headings,tables,images,links,text:clean(body.text())};
}
const entries=Object.values(ledger).filter(x=>x.status==='fetched'&&!/\.js$/.test(new URL(x.url).pathname));
const result=[];
for(const e of entries)try{result.push(extract(e))}catch(error){console.error(e.url,error.message)}
fs.mkdirSync('.cache/research',{recursive:true});
fs.writeFileSync('.cache/research/extracted.json',JSON.stringify(result));
fs.writeFileSync('research/inventory.json',JSON.stringify(result.map(({text,tables,images,links,...x})=>({...x,characters:text.length,tableCount:tables.length,rowCount:tables.reduce((n,t)=>n+t.rows.length,0),imageCount:images.length,linkCount:links.length})),null,2));
console.log(JSON.stringify({documents:result.length,tables:result.reduce((n,x)=>n+x.tables.length,0),rows:result.reduce((n,x)=>n+x.tables.reduce((s,t)=>s+t.rows.length,0),0)}));
