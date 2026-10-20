import fs from 'node:fs';
import {load} from 'cheerio';
import {materialKey,materialLines,materialRows} from '../lib/learning-requirements.ts';

const nameKey=s=>s.toLowerCase().replace(/[’‘]/g,"'").replace(/\s+/g,' ').trim();
const textKey=s=>s.toLowerCase().replace(/[’‘]/g,"'").replace(/[−–—]/g,'-').replace(/\s+/g,' ').trim();
export function sourceFamily(url){
 const host=new URL(url).hostname.replace(/^www\./,'');
 return ['vorlof.com','wanderlustdb.com','da-wizard.com'].includes(host)?host:null;
}
export function comparableValue(label,value){
 if(label==='Learning materials')return materialKey(String(value));
 const clean=textKey(String(value)).replace(/,/g,'');
 if(/^(?:Insight|Ability level|Armor class|Value|Durability|Weight|Experience reward|(?:STR|INT|WIS|CON|DEX) requirement)$/.test(label))return /^[-+]?\d+(?:\.\d+)?$/.test(clean)?String(Number(clean)):null;
 if(label==='Cooldown')return /^\d+(?:\.\d+)?(?:\s*(?:s|sec|seconds?))?$/.test(clean)?String(parseFloat(clean)):null;
 if(label==='Effect radius')return /^\d+ tiles?$/.test(clean)?String(parseInt(clean)):null;
 return textKey(String(value));
}
// One vote per website, not one per page. A website contradicting itself abstains.
export function resolveFact(fact){
 const all=[...fact.evidence,...fact.alternatives],votes=new Map();
 for(const e of all){const family=sourceFamily(e.source),key=comparableValue(fact.label,e.value);if(!family||key===null)continue;const options=votes.get(family)||new Set();options.add(key);votes.set(family,options);}
 const counts=new Map();for(const options of votes.values())if(options.size===1){const [key]=options;counts.set(key,(counts.get(key)||0)+1);}
 const winner=[...counts].find(([,count])=>count>=2);
 if(!winner)return {agreed:false,changed:false};
 const matching=all.filter(e=>comparableValue(fact.label,e.value)===winner[0]);
 const old=fact.value,keep=comparableValue(fact.label,old)===winner[0];
 let selected=keep?old:matching[0].value,preserved=false;
 if(fact.label==='Learning materials'&&!keep){
  // Omission is unknown, not a vote that EXP/AP/gold is free. Keep a recorded
  // cost if the agreeing rows simply leave that entire cost category out.
  const listed=new Set(materialRows(selected).map(row=>row.label));
  for(const row of materialRows(old||''))if(['Gold','EXP','Ability EXP','AP'].includes(row.label)&&!listed.has(row.label)){
   const unit={'Gold':'Coins','EXP':'Experience','Ability EXP':'Ability','AP':'AP'}[row.label];
   selected+='\n'+row.values.map(value=>value+' '+unit).join('\n');preserved=true;
  }
 }
 fact.value=comparableValue(fact.label,selected)===comparableValue(fact.label,old)?old:selected;
 fact.evidence=preserved?[...matching,...fact.evidence]:matching;
 fact.alternatives=all.filter(e=>comparableValue(fact.label,e.value)!==winner[0]);
 return {agreed:true,changed:fact.value!==old,before:old,after:fact.value,votes:winner[1]};
}

export function confirmWithWizard(entries){
 const manifest=JSON.parse(fs.readFileSync('research/da-wizard-sources.json','utf8'));
 const byName=new Map(),used=new Map(),report={pages:manifest.filter(p=>!p.error).length,matchedFields:0,agreedFields:0,changed:[],unresolved:[]};
 // Compare the recorded costs as a whole; normalize quantities and currency
 // spelling, but never guess item aliases or count prerequisites as materials.
 for(const e of entries.filter(e=>e.kind==='ability'))for(const rank of e.ranks){
  const materials=rank.facts.find(f=>f.label==='Learning materials'),other=rank.facts.find(f=>f.label==='Wanderlust learning requirements'),prerequisites=rank.facts.find(f=>f.label==='Prerequisites')?.value||'';
  if(!materials||!other)continue;
  for(const evidence of [...other.evidence,...other.alternatives]){
   const value=materialLines(evidence.value,prerequisites).join('\n');if(!value||materialKey(value)===null)continue;
   if(![...materials.evidence,...materials.alternatives].some(ev=>ev.source===evidence.source&&ev.value===value))materials.alternatives.push({...evidence,value});
  }
 }
 function index(name,entry,target,kind){const key=nameKey(name);const rows=byName.get(key)||[];rows.push({entry,target,kind});byName.set(key,rows);}
 for(const e of entries){if(e.kind==='item'){index(e.title,e,e,'item');for(const v of e.variants)index(v.name,e,v,'item');}else if(e.kind==='ability')for(const r of e.ranks)index(r.name,e,r,e.abilityType);}
 function add(target,entry,label,value,page,locator){
  const f=target.facts.find(f=>f.label===label);if(!f||!value||comparableValue(label,value)===null)return;
  const ev={value,source:page.url,locator};
  if([...f.evidence,...f.alternatives].some(e=>e.source===ev.source&&e.value===value))return;
  f.alternatives.push(ev);report.matchedFields++;
  const source={url:page.url,author:'DA-Wizard',retrievedAt:page.retrievedAt,sourceUpdatedAt:null};
  for(const obj of new Set([entry,target]))if(obj.sources&&!obj.sources.some(s=>s.url===page.url))obj.sources.push(source);
  const ids=used.get(page.url)||new Set();ids.add(entry.id);used.set(page.url,ids);
 }
 const numbers={insight:'Insight',ab:'Ability level',ac:'Armor class',value:'Value',durability:'Durability',weight:'Weight',aether:'Cooldown'};
 const advanced={warrior:'gladiator',wizard:'summoner',priest:'bard',monk:'druid',rogue:'archer'};
 for(const page of manifest.filter(p=>!p.error)){
  const $=load(fs.readFileSync(page.cache,'utf8'));$('script,style').remove();
  const route=new URL(page.url).pathname,kind=/_(?:skills)|\/skills/.test(route)?'skill':/_(?:spells)|\/spells/.test(route)?'spell':/gear|armor|weapons/.test(route)?'item':null;
  const cls=route.match(/\/(warrior|wizard|priest|monk|rogue)_/)?.[1];const tag=route.includes('_med.')?advanced[cls]:cls;
  if(kind)$('table').each((ti,table)=>{
   const rows=$(table).children('tr').add($(table).children('tbody,thead,tfoot').children('tr'));let headers=[];
   rows.each((ri,row)=>{
    const cells=$(row).children('td,th');const vals=cells.map((_,c)=>$(c).text().replace(/\s+/g,' ').trim()).get();
    if(vals[0]==='Name'){headers=vals.map(v=>v.toLowerCase());return;}
    if(!headers.length||vals.length!==headers.length)return;
    const matches=(byName.get(nameKey(vals[0]||''))||[]).filter(m=>m.kind===kind&&(!tag||kind==='item'||m.entry.tags.some(t=>t.toLowerCase()===tag)));
    if(matches.length!==1)return; // Never infer aliases, same-name variants, or pure/subpath identity.
    const {entry,target}=matches[0],locator=`table ${ti+1}, row ${ri+1}`;
    for(let i=1;i<headers.length;i++)if(numbers[headers[i]])add(target,entry,numbers[headers[i]],vals[i],page,locator);
    const materialsIndex=headers.indexOf('required items');
    if(kind!=='item'&&materialsIndex>=0){const cell=cells.eq(materialsIndex).clone();cell.find('br').replaceWith('\n');const value=cell.text().split('\n').map(v=>v.replace(/\s+/g,' ').trim().replace(/,$/,'')).filter(Boolean).join('\n');add(target,entry,'Learning materials',value,page,locator);}
    const si=headers.indexOf('stats');if(si>=0){const statRows=cells.eq(si).find('tr').toArray().map(r=>$(r).children('td,th').map((_,c)=>$(c).text().trim()).get());const stats=statRows.find(r=>r.length===5&&r.every(v=>/^\d+$/.test(v)));if(stats)for(let i=0;i<5;i++)add(target,entry,['STR','INT','WIS','CON','DEX'][i]+' requirement',stats[i],page,locator);}
    if(entry.id==='ability:mana-ward-summoner-spell'){const radius=vals[headers.indexOf('description')]?.match(/\b(\d+) tile radius/i);if(radius)add(entry,entry,'Effect radius',radius[1]+' tiles',page,locator);}
   });
  });
  const questIds={'/files/quests/darkthings.html':'quest:dark-things','/files/quests/alittlebitofthat.html':'quest:a-little-bit-of-that'};
  if(questIds[route]){const e=entries.find(e=>e.id===questIds[route]);const reward=$('body').text().replace(/\s+/g,' ').match(/Reward:\s*([\d,]+) Experience/i);if(e&&reward)add(e,e,'Experience reward',reward[1],page,'Reward');}
 }
 for(const e of entries)for(const target of [e,...(e.ranks||[]),...(e.variants||[])]){
  for(const f of target.facts){const outcome=resolveFact(f),wizard=[...f.evidence,...f.alternatives].some(ev=>sourceFamily(ev.source)==='da-wizard.com');if(outcome.agreed&&wizard)report.agreedFields++;
   if(outcome.changed)report.changed.push({entry:e.id,rank:target.name||null,field:f.label,...outcome});
   else if(!outcome.agreed&&f.alternatives.length)report.unresolved.push({entry:e.id,rank:target.name||null,field:f.label,checked:wizard});
  }
  if(e.kind==='ability'&&target.requirements)for(const [label,prop] of [['Insight','insight'],['Ability level','ability']]){const v=target.facts.find(f=>f.label===label)?.value;if(v&&/^\d+$/.test(v)){target.requirements[prop]=+v;target.requirements.text=target.requirements.text.replace(prop==='ability'?/^Ability \d+/:/^\d+/,prop==='ability'?'Ability '+v:v);}}
 }
 const ward=entries.find(e=>e.id==='ability:mana-ward-summoner-spell');if(ward?.facts.find(f=>f.label==='Effect radius')?.value==='3 tiles'){ward.summary=ward.summary.replace(/thirteen-tile/g,'three-tile');for(const rank of ward.ranks)rank.summary=rank.summary.replace(/thirteen-tile/g,'three-tile');}
 fs.writeFileSync('research/da-wizard-confirmation.json',JSON.stringify(report,null,2));
 return manifest.filter(p=>!p.error).map(p=>({url:p.url,source:'DA-Wizard',status:'fetched',reviewStatus:used.has(p.url)?'compared-and-reconciled':'navigation-index',destinations:[...(used.get(p.url)||[])].map(id=>{const e=entries.find(e=>e.id===id);return '/'+e.section+'/'+e.slug+'/';}),retrievedAt:p.retrievedAt}));
}
