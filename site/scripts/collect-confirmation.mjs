// Explicit research refresh. Normal builds never contact source websites.
import fs from 'node:fs';
import path from 'node:path';
import {load} from 'cheerio';
const root='.cache/da-wizard',origin='https://www.da-wizard.com/';
fs.mkdirSync(root,{recursive:true});const manifest=[];
async function read(route){
 const target=path.join(root,route);let html;
 if(fs.existsSync(target))html=fs.readFileSync(target,'utf8');
 else{const response=await fetch(new URL(route,origin),{signal:AbortSignal.timeout(20000)});if(!response.ok)throw new Error(route+' HTTP '+response.status);html=await response.text();fs.mkdirSync(path.dirname(target),{recursive:true});fs.writeFileSync(target,html);await new Promise(r=>setTimeout(r,180));}
 manifest.push({url:new URL(route,origin).href,cache:target,retrievedAt:fs.statSync(target).mtime.toISOString()});return html;
}
const routes=new Set(['spells.html','spells_med.html','quests.html','files/spell_skill/other.html','files/spell_skill/medenia.html']);
for(const sitemap of ['sitemap.html','sitemap_class.html','sitemap_quest.html']){
 const $=load(await read(sitemap));$('a[href]').each((_,el)=>{const route=$(el).attr('href').split('#')[0];
 if(/^(?:(?:warrior|wizard|monk|priest|rogue)_)?(?:skills|spells|armor|armors|weapons|gear)(?:_[a-z]+)?\.html$/.test(route)||/^files\/quests\/(?:dark|alittle|pig|law)/i.test(route))routes.add(route);
 });
}
for(const route of routes)try{await read(route);console.log('Cached '+route);}catch(error){console.log(error.message);manifest.push({url:new URL(route,origin).href,error:error.message});}
fs.mkdirSync('research',{recursive:true});fs.writeFileSync('research/da-wizard-sources.json',JSON.stringify(manifest,null,2));
console.log('Reference pages: '+manifest.filter(p=>!p.error).length);
