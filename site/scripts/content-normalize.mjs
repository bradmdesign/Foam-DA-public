import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {LibrarySchema} from '../lib/models.ts';
import {catalogRecord,compareRequirements} from '../lib/catalog-records.ts';
import {describeAbility} from '../content/ability-effects.mjs';
import sourceNotes from '../content/source-notes.mjs';
import {normalizeSupplement} from './normalize-supplement.mjs';
import {itemEffects} from '../content/item-effects.mjs';
import {confirmWithWizard} from './source-consensus.mjs';
import {presentEntry} from '../lib/presentation.ts';

const docs=JSON.parse(fs.readFileSync('.cache/research/extracted.json','utf8'));
const ledger=JSON.parse(fs.readFileSync('research/coverage.json','utf8'));
const assets=fs.existsSync('research/assets.json')?JSON.parse(fs.readFileSync('research/assets.json','utf8')):{};
const clean=s=>(s||'').replace(/\u00a0/g,' ').replace(/[ \t]+/g,' ').trim();
const key=s=>clean(s).normalize('NFKD').replace(/[’‘]/g,"'").replace(/[–—]/g,'-').toLowerCase().replace(/[^a-z0-9]+/g,'');
const valueKey=s=>clean(s).replace(/[’‘]/g,"'").replace(/[−–—]/g,'-').toLowerCase().replace(/[^a-z0-9%.-]+/g,'');
const slug=s=>clean(s).normalize('NFKD').replace(/['’]/g,'').toLowerCase().replace(/[^a-z0-9]+/g,'-').replace(/^-|-$/g,'');
const hash=s=>crypto.createHash('sha256').update(s).digest('hex').slice(0,8);
function canonical(value){try{const u=new URL(value);u.protocol='https:';u.hash='';if(u.hostname==='wanderlustdb.com')u.hostname='www.wanderlustdb.com';if(u.hostname==='www.vorlof.com')u.hostname='vorlof.com';u.searchParams.sort();return u.href}catch{return value}}
const docByUrl=new Map(docs.map(d=>[canonical(d.url),d]));
const entries=new Map(),sourceDest=new Map(),relations=new Map(),issues=[],variantMap=new Map(),aliases=new Map();
const priority=url=>url.includes('vorlof.com/daitems/')?1:url.includes('vorlof.com')?0:2;
const source=d=>({url:d.url,author:d.url.includes('/daitems/')?'Russell / Sostratos — DA-Items':d.source==='Vorlof'?'Vorlof':'Wanderlust contributors',retrievedAt:d.retrievedAt||null,sourceUpdatedAt:null});
function base(kind,title,section,idSuffix=''){const s=slug(title)||'unnamed';const id=kind+':'+s+idSuffix;if(entries.has(id))return entries.get(id);const e={id,slug:kind==='item'||kind==='quest'||kind==='event'||kind==='ability'||kind==='article'?s+idSuffix:kind+'-'+s+idSuffix,title:clean(title),section,kind,summary:'',aliases:[],sources:[],facts:[],notes:[],steps:[],tables:[],assets:[],related:[],tags:[],historical:false,review:'structured'};entries.set(id,e);return e}
function attach(e,d){if(!e.sources.some(x=>x.url===d.url))e.sources.push(source(d));const u=canonical(d.url);if(!sourceDest.has(u))sourceDest.set(u,new Set());sourceDest.get(u).add(e.id)}
function fact(e,label,value,d,locator='',variant){value=clean(String(value??''));if(!value||/^(?:\?+|unknown|n\/a|value|weight|description)$/i.test(value))return;
 const ev={value,source:d.url,...(locator?{locator}:{}),...(variant?{variant}:{})};let f=e.facts.find(f=>f.label===label);
 if(!f){e.facts.push({label,value,evidence:[ev],alternatives:[]});return}
 if(valueKey(f.value)===valueKey(value)){if(!f.evidence.some(x=>x.source===d.url))f.evidence.push(ev);return}
 if(priority(d.url)<priority(f.evidence[0].source)){f.alternatives.push(...f.evidence);f.value=value;f.evidence=[ev]}
 else if(!f.alternatives.some(x=>valueKey(x.value)===valueKey(value)&&x.source===d.url))f.alternatives.push(ev);
}
function asset(e,im,alt){if(!im?.url)return;const a=assets[canonical(im.url)];if(a?.status==='fetched'&&!e.assets.some(x=>x.path===a.path))e.assets.push({path:a.path,width:a.width,height:a.height,alt:alt||im.alt||e.title,source:im.url});}
const requirements=text=>{const t=clean(text);return {insight:/\b(?:AB|Ability)\s*\d/i.test(t)?null:Number(t.match(/^([1-9]\d?)(?:\b|\+)/)?.[1])||null,ability:Number(t.match(/\b(?:AB|Ability)\s*0*(\d+)/i)?.[1])||null,mastery:/master/i.test(t)?'required':'unknown',path:/non.pure|subpath/i.test(t)?'subpath':/pure/i.test(t)?'pure':'unknown',classes:[],text:t}};
const emptyReq=()=>requirements('');
function relation(from,to,type,d,detail){if(!from||!to||from===to)return;const value={from,to,type,source:d.url||d,...(detail?{detail}:{})};relations.set([from,to,type,detail||''].join('|'),value)}
function mapUrl(url,id){url=canonical(url);if(!sourceDest.has(url))sourceDest.set(url,new Set());sourceDest.get(url).add(id)}
const allowedModifiers=new Set(['blessed','abundance','might','magic','cail','ceannlaidir','deoch','fiosachd','glioca','gramail','luathas','sgrios','good','fine','grand','great','noble','silk','wisdom']);
function variantKind(mod){return ['blessed','abundance','might','magic'].includes(mod)?'enchantment':['cail','ceannlaidir','deoch','fiosachd','glioca','gramail','luathas','sgrios'].includes(mod)?'consecration':['good','fine','grand','great','noble','silk','wisdom'].includes(mod)?'quality':'random-stats'}
function addVariant(name,baseName,d,kind){if(key(name)===key(baseName))return;const k=key(name);if(variantMap.has(k)&&key(variantMap.get(k).base)!==key(baseName)){issues.push({type:'variant-conflict',name,base:baseName,source:d.url});return}variantMap.set(k,{name,base:baseName,source:d.url,kind});}
// Explicit paired display names are reviewed relationships, not sprite matches.
for(const d of docs.filter(d=>d.source==='Wanderlust'))for(const t of d.tables){if(!/^Name/.test(t.rows[0]?.[0]?.text||''))continue;for(const r of t.rows.slice(1)){const text=r[0]?.text||'';const lines=text.split('\n');const paren=lines.find(s=>/^\(.+\)$/.test(s));if(!paren)continue;const baseName=lines[0];const v=paren.slice(1,-1);const mod=v.split(' ')[0].toLowerCase();if(allowedModifiers.has(mod)&&key(v.slice(mod.length))===key(baseName))addVariant(v,baseName,d,variantKind(mod));}}
for(const d of docs.filter(d=>d.url.includes('/daitems/')))for(const t of d.tables){if(t.rows[0]?.[0]?.text!=='Name')continue;let previous=null;for(const r of t.rows.slice(1)){const name=r[0]?.text;if(!name)continue;if(name.startsWith('~')){const mod=name.slice(1).split(/\n/)[0].trim();if(previous&&allowedModifiers.has(mod.toLowerCase()))addVariant(mod+' '+previous,previous,d,variantKind(mod.toLowerCase()));}else previous=name;}}
const bootEvidence=docByUrl.get('https://vorlof.com/general/itemlist.php');
for(const modifier of ['Abundance','Blessed','Cail','Ceannlaidir','Deoch','Fiosachd','Glioca','Gramail','Luathas','Magic','Might','Sgrios'])addVariant(modifier+' Cordovan Boots','Cordovan Boots',bootEvidence,variantKind(modifier.toLowerCase()));
function item(name,d){name=clean(name).replace(/\nLev[\s\S]*$/i,'').split('\n')[0];if(!name||/^(?:Name|Item|None|\?|N\/A|Nothing|Drops>>>|~)/i.test(name)&&/^(?:Name|Item|None|\?|N\/A|Nothing|Drops>>>|~.*)$/i.test(name))return null;
 const v=variantMap.get(key(name));const canonicalName=v?.base||name;let e=aliases.get(key(canonicalName));if(!e){e=base('item',canonicalName,'items');e.enchantable=null;e.variants=[];e.requirements=emptyReq();aliases.set(key(canonicalName),e)}
 if(v&&!e.variants.some(x=>key(x.name)===key(name))){e.variants.push({name:v.name,kind:v.kind,evidence:v.source,facts:[]});e.aliases.push(v.name);if(v.kind==='enchantment')e.enchantable=true;}
 aliases.set(key(name),e);attach(e,d);return e;
}
const itemIndex=docByUrl.get('https://vorlof.com/general/itemlist.php');
for(const r of itemIndex?.tables[0]?.rows.slice(1)||[]){const e=item(r[1]?.text,itemIndex);if(!e)continue;for(const l of r[1].links)mapUrl(l.url,e.id);e.review='index-only';}
// Modern catalogs and the hosted archive are read as data, not replayed HTML.
const itemDocuments=docs.filter(d=>['/equipment.php','/armors.php','/weapons.php','/accessories.php','/trinkets.html'].includes(new URL(d.url).pathname)&&d.source==='Vorlof'||/\/daitems\/(?!Vendors|crafting)/.test(d.url)||d.source==='Wanderlust'&&/\/(item_view\.php|armor\/|weapons\/|equipment\/|consumableItems\/|miscItems\/|trinkets\/)/.test(d.url)).sort((a,b)=>priority(a.url)-priority(b.url));
const labels={lev:'Insight',level:'Insight',ability:'Ability level',ac:'Armor class',value:'Value',durability:'Durability',wt:'Weight',weight:'Weight',stats:'Attributes',attributes:'Attributes',str:'Strength',int:'Intelligence',wis:'Wisdom',con:'Constitution',dex:'Dexterity',hp:'Health',mp:'Mana',hit:'Hit',dmg:'Damage',regen:'Regeneration',mr:'Magic resistance',type:'Type',stack:'Stack limit',kruna:'Kruna',bound:'Bound',dyeable:'Dyeable',damage:'Weapon damage','smalldamage':'Small-target damage','largedamage':'Large-target damage',gender:'Gender',class:'Class',cooldown:'Cooldown',drop:'Droppable',tradeable:'Tradeable',tradable:'Tradeable',perishable:'Perishable'};
const rawItemRows=[];
for(const d of itemDocuments)for(const t of d.tables){const h=t.rows[0]?.map(c=>key(c.text))||[];if(!['name','namelev','item'].includes(h[0])||!h.some(x=>['level','ac','type','durability','wt','weight','desc','description','use','obtained','contains'].includes(x)))continue;
 let previous='';for(let ri=1;ri<t.rows.length;ri++){const r=t.rows[ri];if(r.length<2)continue;let name=r[0]?.text||'';if(!name)continue;
 if(name.startsWith('~')){const mod=name.slice(1).split('\n')[0].trim();if(previous)name=mod+' '+previous;else continue;}else previous=name.split('\n')[0];
 const paren=name.split('\n').find(s=>/^\(.+\)$/.test(s));const actual=paren?paren.slice(1,-1):name.split('\n')[0];
 const rowDescription=r[h.findIndex(x=>x==='description'||x==='desc'||x==='use')]?.text||'';
 const e=item(actual==='Cauldron'&&/Cooking at Oren Ball/.test(rowDescription)?'Cauldron (Oren Ball)':actual,d);if(!e)continue;e.review='structured';const variant=e.variants.find(v=>key(v.name)===key(actual));const target=variant||e;
 const locator=`table ${d.tables.indexOf(t)+1}, row ${ri+1}`;
 for(let i=1;i<h.length;i++)if(labels[h[i]])fact(target,labels[h[i]],r[i]?.text,d,locator,variant?.name);
 const lev=name.match(/\bLev\s*(Ab\s*)?(\d+)/i);if(lev)fact(target,lev[1]?'Ability level':'Insight',lev[2],d,locator,variant?.name);
 const context=t.context?.[0]||'';const params=new URL(d.url).searchParams;
 const cls=params.get('class')||context.match(/(?:-|^)(Peasant|Warrior|Wizard|Priest|Monk|Rogue|Gladiator|Summoner|Bard|Druid|Archer)$/i)?.[1];if(cls&&!variant)fact(e,'Class',cls,d,locator);
 const slot=params.get('slot')||(context==='content'?'':context.split('-')[0]);if(slot&&!/^(?:main|maincontent|armor|weapon|peasant|warrior|wizard|priest|monk|rogue)/i.test(slot)&&slot.length<24&&!variant)fact(e,'Slot',slot,d,locator);
 const desc=rowDescription;
 if(/\benchantable\b/i.test(desc)&&!/(?:not|non)[ -]?enchantable/i.test(desc))e.enchantable=true;
 if(/not enchantable|non-enchantable/i.test(desc))e.enchantable=false;
 if(desc&&!/^enchantable\.?$/i.test(desc))rawItemRows.push({id:e.id,name:actual,description:desc,source:d.url});
 const contents=r[h.indexOf('contains')]?.text;if(contents)rawItemRows.push({id:e.id,name:actual,description:'Contains:\n'+contents,source:d.url});
 for(const cell of r)for(const l of cell.links)if(/\/(items?|item_view)\.php/.test(l.url)&&(!l.text||key(l.text)===key(actual)))mapUrl(l.url,e.id);
 asset(e,r[0]?.images[0],e.title+' game sprite');
 const location=r[h.findIndex(x=>['location','obtained'].includes(x))];if(location?.text){const value=location.text.replace(/^Buy from (.*?) for ([\d,]+) coins\.?$/i,'Vendor: $1; price: $2 coins').replace(/^Drops>>>$/,'');if(value.length<220)fact(target,'Acquisition',value,d,locator,variant?.name)}
 if(d.url.includes('/item_view.php'))mapUrl(d.url,e.id);
 }}
// Explicitly register every variant even when only its alternate row was cached.
for(const v of variantMap.values()){const d=docByUrl.get(canonical(v.source));if(d)item(v.name,d)}
const effectSeen=new Set();for(const row of rawItemRows){const sig=row.id+'|'+row.description;if(effectSeen.has(sig))continue;effectSeen.add(sig);const e=entries.get(row.id),d=docByUrl.get(canonical(row.source));const result=itemEffects(row.name,row.description);for(const [label,value] of result.facts)fact(e,label,value,d);for(const note of result.notes)if(!e.notes.includes(note))e.notes.push(note);for(const table of result.tables)e.tables.push({...table,source:d.url});if(!result.handled)issues.push({type:'item-effect-review',entry:e.id,name:row.name,description:row.description,source:d.url})}

// World map identity is the source map ID, not its occasionally repeated title.
const locationByMap=new Map();
const mapsDoc=docByUrl.get('https://vorlof.com/general/maplist.php');
for(const r of mapsDoc?.tables[0]?.rows.slice(1)||[]){if(!/^\d+$/.test(r[0]?.text||''))continue;const id=r[0].text;const e=base('location',r[1].text,'world','-'+id);e.mapId=id;e.summary='Map details and connected records for '+e.title+'.';fact(e,'Map dimensions',r[2]?.text,mapsDoc);attach(e,mapsDoc);locationByMap.set(id,e);for(const l of r.flatMap(c=>c.links))mapUrl(l.url,e.id);mapUrl('https://www.wanderlustdb.com/map_view.php?id='+id,e.id)}
const npcsDoc=docByUrl.get('https://vorlof.com/general/npclist.php');
for(const r of npcsDoc?.tables[0]?.rows.slice(1)||[]){const name=r[0]?.text;if(!name)continue;const mapid=r[1]?.links?.[0]?.url&&new URL(r[1].links[0].url).searchParams.get('map');const e=base('npc',name,'world','-'+slug(mapid+'-'+r[2]?.text));e.coordinates=r[2]?.text||null;e.summary='Find '+name+' and the documented services associated with this NPC.';attach(e,npcsDoc);fact(e,'Location',r[1]?.text,npcsDoc);fact(e,'Coordinates',e.coordinates,npcsDoc);for(const l of r[0].links)mapUrl(l.url,e.id);if(locationByMap.has(mapid))relation(e.id,locationByMap.get(mapid).id,'found-in',npcsDoc,e.coordinates)}
for(const d of docs.filter(d=>d.source==='Wanderlust'&&new URL(d.url).pathname==='/map_view.php')){
 const mapid=new URL(d.url).searchParams.get('id');let e=locationByMap.get(mapid);if(!e){e=base('location',d.title.replace(/Map Details: */i,'')||'Map '+mapid,'world','-'+mapid);e.mapId=mapid;locationByMap.set(mapid,e)}attach(e,d);mapUrl(d.url,e.id);
 for(const t of d.tables){if(t.rows[0]?.[0]?.text==='Map Features')for(const r of t.rows.slice(1))fact(e,r[0]?.text.replace(/:$/,''),r[1]?.text,d);}
 for(const im of d.images.filter(im=>!/(?:\/items\/|\/creatures\/|check\.png|cross\.png|main_sub)/.test(im.url)))asset(e,im,e.title+' map');
}

// Monster IDs in Wanderlust identify spawn records; same-name creatures remain distinct.
const monsterByUrl=new Map();
for(const d of docs.filter(d=>d.source==='Wanderlust'))for(const t of d.tables){let hi=t.rows.findIndex(r=>r.some(c=>['Monster','Enemy','Name'].includes(c.text))&&r.some(c=>/^(HP|Max HP|Level Range|Levels|Lev)$/.test(c.text)));if(hi<0)continue;const h=t.rows[hi].map(c=>c.text);
 for(const r of t.rows.slice(hi+1)){const c=r[0];const link=c?.links.find(l=>l.url.includes('/mon_view.php'));if(!link)continue;const u=canonical(link.url);const id=new URL(u).searchParams.get('id');let e=monsterByUrl.get(u);if(!e){e=base('monster',c.text.split('\n')[0]||link.text,'world','-'+id);e.requirements=emptyReq();monsterByUrl.set(u,e)}attach(e,d);mapUrl(u,e.id);asset(e,c.images[0],e.title+' game sprite');for(let j=1;j<h.length;j++){if(!/Drops|Locations/i.test(h[j]))fact(e,h[j],r[j]?.text,d);}
 }}
// Abilities are grouped by class and base name, with each rank kept as structured data.
const classes=['gladiator','summoner','bard','druid','archer','warrior','wizard','priest','monk','rogue','peasant'];
const abilityRows=[];
for(const d of docs.filter(d=>d.source==='Vorlof'&&/^\/(skills|spells)\.html$/.test(new URL(d.url).pathname))){
 for(const t of d.tables){const h=t.rows[0]?.map(c=>key(c.text))||[];if(h[0]!=='name')continue;const context=(t.context||[]).join(' ');const cls=classes.find(c=>context.includes(c))||'peasant';const isAdvanced=classes.indexOf(cls)<5;
 for(const r of t.rows.slice(1)){const name=r[0]?.text;if(!name||name==='Name'||name.length>90)continue;const abilityType=d.url.includes('skills')?'skill':'spell';const rankMatch=isAdvanced?name.match(/ (\d+)$/):null;const groupName=rankMatch?name.slice(0,-rankMatch[0].length):name;
 const e=base('ability',groupName,'abilities','-'+cls+'-'+abilityType);e.abilityType=abilityType;e.ranks??=[];e.tags=[cls[0].toUpperCase()+cls.slice(1),abilityType];attach(e,d);asset(e,r[0].images[0],name+' icon');
 const read=label=>r[h.indexOf(label)]?.text||'';const req=read('requiredskills')||read('requiredspells');const level=read('ability')||read('level');const requirementsValue=requirements((isAdvanced?'Ability ':'')+level+' '+req);requirementsValue.classes=[cls[0].toUpperCase()+cls.slice(1)];
 const rawDescription=read('description');const rewritten=describeAbility(name,rawDescription);
 const rank={name,rank:rankMatch?Number(rankMatch[1]):null,requirements:requirementsValue,facts:[],summary:rewritten||'Effect details have not yet been independently rewritten.',sources:[source(d)]};
 if(requirementsValue.ability)fact(rank,'Ability level',String(requirementsValue.ability),d);
 if(requirementsValue.insight)fact(rank,'Insight',String(requirementsValue.insight),d);
 const statValues=read('stats').trim().match(/^(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)$/);
 if(statValues)for(let i=1;i<=5;i++)fact(rank,['','STR requirement','INT requirement','WIS requirement','CON requirement','DEX requirement'][i],statValues[i],d);
 for(const [label,column] of [['Stat requirements','stats'],['Prerequisites',h.includes('requiredskills')?'requiredskills':'requiredspells'],['Learning materials','requireditems'],['Trainer','learnedfrom'],['Cooldown','cooldown'],['Lines / mana cost','linescost'],['Required form','formrequired']])fact(rank,label,read(column),d);
 if(statValues)rank.facts=rank.facts.filter(f=>f.label!=='Stat requirements');
 if(!rewritten&&rawDescription)issues.push({type:'ability-effect-review',entry:e.id,name,description:rawDescription,source:d.url});
 e.ranks.push(rank);if(rankMatch)e.aliases.push(name);if(!e.summary)e.summary=rewritten||'Learning requirements and rank data for '+groupName+'.';
 abilityRows.push({entry:e,rank,row:r,header:h,doc:d});
 }}}

// Authored guides: originals never fall back to a copied source body.
const articlesBySource=new Map();
for(const filename of fs.readdirSync('content/editorial').filter(f=>f.endsWith('.mjs'))){const mod=(await import(pathToFileURL(path.resolve('content/editorial',filename)).href)).default;
 for(const [name,editorial] of Object.entries(mod)){
  let d;if(editorial.source)d=docByUrl.get(canonical(editorial.source));else {const folder=filename.startsWith('quests-')?'quests':filename.startsWith('events-')?'events':filename.startsWith('knowledge-')?'general':filename==='bosses.mjs'?'bosstactics':null;d=docs.find(d=>d.source==='Vorlof'&&new URL(d.url).pathname.replace(/\.[^.]+$/,'')===(folder?'/'+folder:'')+'/'+name)}
  if(!d){issues.push({type:'editorial-source-missing',name,file:filename});continue}
  if(editorial.excluded){mapUrl(d.url,'excluded:'+editorial.excluded);continue}
  const section=editorial.section|| (filename.startsWith('quests-')?'quests':filename.startsWith('events-')?'events':filename==='bosses.mjs'?'world':'knowledge');const kind=section==='quests'?'quest':section==='events'?'event':'article';
  const title=editorial.title||(name==='reincarnationofchadul'?'Reincarnation of Chadul':name==='reincarnationofdanaan'?'Reincarnation of Danaan':d.headings[0]||d.title||name);const e=base(kind,title,section,filename==='bosses.mjs'?'-tactics':'');
  e.summary=editorial.summary;e.steps=editorial.steps||[];e.notes=editorial.notes||[];e.historical=editorial.historical||false;e.review='rewritten';e.tags=filename==='bosses.mjs'?['Boss tactics']:[];
  if(kind==='quest'||kind==='event'){e.requirements=emptyReq();e.rewards=[]}
  if(editorial.routes)e.tables.push({title:'At a glance',headers:editorial.routes[0],rows:editorial.routes.slice(1)});
  if(editorial.credits)e.notes.push('Additional source credit: '+editorial.credits.join('; ')+'.');
  attach(e,d);articlesBySource.set(canonical(d.url),{entry:e,editorial,doc:d,name});
 }}
// Join quest requirements and rewards from the source index, including its links.
for(const root of ['quests','events']){const d=docByUrl.get('https://vorlof.com/'+root+'.html');if(!d)continue;for(const t of d.tables){let h=[];for(const r of t.rows){if(r.some(c=>c.text==='Rewards')&&r.some(c=>/Title|Name/.test(c.text))){h=r.map(c=>c.text);continue}if(!h.length||r.length!==h.length)continue;const link=r[0].links.find(l=>articlesBySource.has(canonical(l.url)));if(!link)continue;const e=articlesBySource.get(canonical(link.url)).entry;attach(e,d);for(let i=1;i<r.length;i++){const label=h[i];if(label==='Rewards'&&e.rewards)e.rewards.push(cellText(r[i]));if(/Level/.test(label)&&e.requirements)e.requirements=requirements(r[i].text);if(/Level|Items Required|Rewards/.test(label))fact(e,label,cellText(r[i]),d);}}}
}
function cellText(cell){if(!cell)return '';if(cell.text)return cell.text;return cell.links.map(l=>{if(l.text)return l.text;try{const u=new URL(l.url);const id=u.searchParams.get('id');return id&&/^\d+_/.test(id)?id.replace(/^\d+_/,''):''}catch{return ''}}).filter(Boolean).join(', ')}
function factualTable(t,d){let rows=t.rows.map(r=>r.map(cellText));if(rows.length<2)return null;let title='Reference details';if(rows[0].length===1){title=rows.shift()[0];}
 const headers=rows.shift();if(!headers||headers.length<2||headers.some(x=>x.length>140))return null;
 rows=rows.filter(r=>r.length===headers.length&&!r.every(c=>!c)&&!r.some(c=>c.length>450));
 // Dialogue and literary text are represented by the independently written guide.
 if(!rows.length||headers.some(h=>/Dialogue|Conversation|Usefulness/i.test(h)))return null;
 return {title,headers,rows,source:d.url};
}
for(const {entry:e,editorial,doc:d} of articlesBySource.values()){
 if(editorial.includeTables)for(const t of d.tables){const table=factualTable(t,d);if(table)e.tables.push(table)}
 if(editorial.abilityGraph){const rows=[...d.text.matchAll(/\["([^"]+)"\]\s*--\|([^|]+)\|-->\s*\w+\["([^"]+)"\]/g)].map(m=>[m[1],m[2],m[3]]);if(rows.length)e.tables.push({title:'Ability prerequisites',headers:['Prerequisite','Required training','Unlocks'],rows,source:d.url})}
 if(editorial.factLists){let group='Reference';const rows=[];for(const line of d.text.split('\n').map(s=>s.trim()).filter(Boolean)){if(d.headings.includes(line)){group=line;continue}if(/^Base stats|^by |^\(|^Note:/.test(line))continue;if(/^\d+\s+\S|^.+:\s*\d+$/.test(line)||/Set|Boots|Belt|Earrings|Helmet|Sword|Rings|Amulet|Gauntlet/.test(line)||group==='Remaining Spells:'||group==='Skills:')rows.push([group,line]);}if(rows.length)e.tables.push({title:'Planning checklist',headers:['Group','Material, quantity, or equipment'],rows,source:d.url})}
 for(const l of d.links){const target=articlesBySource.get(canonical(l.url));if(target&&target.entry.id!==e.id)relation(e.id,target.entry.id,'related',d);}
}

// Crafting references preserve inputs and outputs as relationships.
for(const d of docs.filter(d=>d.source==='Wanderlust'&&d.url.includes('/craft/')))for(const t of d.tables){const h=t.rows[0]?.map(c=>key(c.text))||[];if(!h.includes('requirements')||h[0]!=='name')continue;for(const r of t.rows.slice(1)){const name=r[0]?.text;if(!name)continue;const e=base('recipe',name,'crafting');e.output=name;e.materials=[];e.summary='Materials and crafting location for '+name+'.';attach(e,d);asset(e,r[0].images[0],name+' icon');const req=r[h.indexOf('requirements')]?.text||'';fact(e,'Materials',req,d);fact(e,'Crafting location',r[h.indexOf('location')]?.text,d);for(const line of req.split('\n')){if(!line)continue;let m=line.match(/^(.+?)\s*\((\d+)\)$/);e.materials.push({name:m?m[1]:line,quantity:m?+m[2]:null});}const output=aliases.get(key(name));if(output)relation(e.id,output.id,'crafts',d);}}

// Region and atlas pages keep their factual tables, maps and connected catalogs.
for(const d of docs.filter(d=>d.source==='Vorlof'&&/^\/(maps|mapsmonsters)\//.test(new URL(d.url).pathname)||d.source==='Wanderlust'&&/^\/(towns|hunting|events)\/[^/]+\.php$/.test(new URL(d.url).pathname))){
 if(articlesBySource.has(canonical(d.url)))continue;
 const p=new URL(d.url).pathname;let title=d.headings[0]||d.title;if(!title||/Wanderlust|::|^\[/.test(title))title=path.basename(p).replace(/\.php$/,'').replace(/([a-z])([A-Z])/g,'$1 $2').replace(/^./,c=>c.toUpperCase());
 const e=base('article',title,/^\/events\//.test(p)?'events':'world','-'+(p.includes('mapsmonsters')?'bestiary':p.includes('/maps/')?'atlas':'region'));
 e.summary=p.includes('mapsmonsters')?'Creature records, drops, and hunting-area details for '+title+'.':'Explore the local maps and connected reference records for '+title+'.';attach(e,d);
 for(const im of d.images.filter(im=>!/(?:\/items2?\/|\/creatures\/|\/monsters\/|\/images\/|\/skills\/pic\/)/.test(im.url)))asset(e,im,title+' map');
 for(const t of d.tables){const table=factualTable(t,d);if(table)e.tables.push(table)}
}

const {deferredEditorial,comparedSources}=normalizeSupplement({docs,entries,sourceDest,base,attach,fact,asset,assets,mapUrl,canonical,key,slug,aliases,item,requirements,emptyReq,relation,source,issues,locationByMap,monsterByUrl,factualTable});
// Link known source references in both directions through one validated edge list.
for(const d of docs){const fromIds=sourceDest.get(canonical(d.url));if(!fromIds||fromIds.size>8)continue;
 for(const l of d.links){const targets=sourceDest.get(canonical(l.url));if(!targets||targets.size>8)continue;for(const from of fromIds)for(const to of targets){const a=entries.get(from),b=entries.get(to);if(!a||!b)continue;const type=a.kind==='item'&&b.kind==='monster'?'drops':a.kind==='item'&&b.kind==='npc'?'sold-by':a.kind==='monster'&&b.kind==='location'?'found-in':a.kind==='location'&&b.kind==='location'?'connects':'related';relation(from,to,type,d);}}
}
for(const {entry:e,rank,row,header,doc:d} of abilityRows){for(const field of ['requireditems','learnedfrom','requiredskills','requiredspells']){const cell=row[header.indexOf(field)];for(const l of cell?.links||[]){for(const target of sourceDest.get(canonical(l.url))||[])if(entries.has(target))relation(e.id,target,field==='requireditems'?'material':field==='learnedfrom'?'trained-by':'requires',d,rank.name);}}}
for(const e of entries.values())if(e.kind==='recipe')for(const material of e.materials){const target=aliases.get(key(material.name));if(target)relation(e.id,target.id,'material',e.sources[0].url,material.quantity===null?undefined:String(material.quantity))}
// Exact names in rewritten instructions connect rewards/materials without asserting an unverified role.
const namedItems=[...new Set(aliases.values())].filter(e=>e.title.length>=5&&e.title.length<65);
const words=s=>' '+clean(s).toLowerCase().replace(/[^a-z0-9]+/g,' ')+' ';
for(const {entry:e} of articlesBySource.values()){const prose=words([e.summary,...e.steps].join(' '));for(const target of namedItems){if(prose.includes(words(target.title)))relation(e.id,target.id,'related',e.sources[0].url)}}
// Source index fields establish item roles; ordinary mentions remain untyped references.
for(const e of entries.values())if(e.kind==='quest'||e.kind==='event')for(const f of e.facts){const type=/^Rewards$/i.test(f.label)?'rewards':/Items Required/i.test(f.label)?'material':null;if(type)for(const target of namedItems)if(words(f.value).includes(words(target.title)))relation(e.id,target.id,type,f.evidence[0].source);}
for(const [from,to] of [['article:mastery','article:subpathing'],['article:the-four-creants','article:mastery'],['article:medenian-advancement','article:the-four-creants']]){const a=entries.get(from),b=entries.get(to);if(a&&b)relation(from,to,from==='article:mastery'?'related':'requires',a.sources[0].url);}
for(const r of relations.values()){const a=entries.get(r.from),b=entries.get(r.to);if(a&&b){if(!a.related.includes(b.id))a.related.push(b.id);if(!b.related.includes(a.id))b.related.push(a.id)}}

for(const n of sourceNotes){const e=entries.get(n.target);if(!e){issues.push({type:'source-note-target',target:n.target});continue}const primary=docByUrl.get(canonical(n.primaryUrl)),other=docByUrl.get(canonical(n.url));if(primary){fact(e,n.field,n.primary,primary);attach(e,primary)}if(other){fact(e,n.field,n.alternative,other);attach(e,other)}}
const wizardCoverage=confirmWithWizard([...entries.values()]);
// Apply requirements after consensus, so catalog filters and visible facts agree.
for(const e of entries.values()){
 // Consolidate repeated source tables without discarding distinct variation drop locations.
 const tables=new Map();for(const table of e.tables){const t={...table,headers:[...table.headers],rows:table.rows.map(r=>[...r])};if(t.title==='Documented acquisition'&&e.kind==='item'&&e.variants.length){const doc=docByUrl.get(canonical(t.source||''));const variant=doc?.title.match(/\(([^)]+)\)/)?.[1]||e.title;t.headers.push('Version');t.rows=t.rows.map(r=>[...r,variant]);}const k=JSON.stringify([t.title,t.headers]);const prev=tables.get(k);if(prev){prev.rows.push(...t.rows);if(t.source&&!prev.sources.includes(t.source))prev.sources.push(t.source)}else tables.set(k,{...t,sources:t.source?[t.source]:[]});}e.tables=[...tables.values()].map(t=>({...t,rows:[...new Map(t.rows.map(r=>[JSON.stringify(r),r])).values()]}));
 if(e.kind==='item'){
  const find=l=>e.facts.find(f=>f.label===l)?.value;
  e.requirements=requirements((find('Ability level')?'Ability '+find('Ability level'):find('Insight')||'')+' '+(find('Class')||''));
  e.requirements.classes=find('Class')?[find('Class')]:[];
  const type=find('Slot')||find('Type')||'Item';const displayTag=t=>({bodyOver:'Overcoat',headOver:'Head accessory',EventCon:'Event consumable',ExpBonus:'Experience bonus',MiscCon:'Other consumable',ModifyChar:'Appearance',feet:'Boots',hat:'Hat'}[t]||t.replace(/^(monk|peasant|priest|rogue|warrior|wizard)$/i,v=>v[0].toUpperCase()+v.slice(1).toLowerCase()));e.tags=[...new Set([type,...e.requirements.classes].map(displayTag))];
  e.summary=e.enchantable?`An enchantable item. Base properties and verified modified versions of ${e.title} are collected here.`:`Base properties, acquisition records, and source notes for ${e.title}.`;
  if(e.review==='index-only')e.notes.push('The cached index confirms this item’s name. Its detailed Vorlof page was not retrieved before the host began denying access; missing properties remain unknown.');
 }
 if(e.kind==='monster'&&!e.summary)e.summary='A distinct creature record for '+e.title+'. See its level, location, and drops before comparing same-name creatures.';
 e.aliases=[...new Set(e.aliases)].filter(a=>key(a)!==key(e.title));
 e.sources.sort((a,b)=>priority(a.url)-priority(b.url));
}
const occupiedRoutes=new Map();for(const e of [...entries.values()].sort((a,b)=>(a.kind==='item'?-1:1)-(b.kind==='item'?-1:1))){const route=e.section+'/'+e.slug;if(occupiedRoutes.has(route))e.slug=e.kind+'-'+e.slug;occupiedRoutes.set(e.section+'/'+e.slug,e.id)}
const discrepancies=[];
 for(const e of entries.values()){for(const f of e.facts)if(f.alternatives.length)discrepancies.push({entry:e.id,field:f.label,preferred:f.value,alternatives:f.alternatives});for(const r of e.ranks||[])for(const f of r.facts)if(f.alternatives.length)discrepancies.push({entry:e.id,field:r.name+' · '+f.label,preferred:f.value,alternatives:f.alternatives});}
// Ledger reporting is separate from retrieval so a cached page is never called reviewed by accident.
const authoredSources=new Set([...articlesBySource.keys()]);
const coverage=Object.values(ledger).map(e=>{const url=canonical(e.url),targets=[...(sourceDest.get(url)||[])];const actual=targets.filter(id=>entries.has(id));const blocked=e.httpStatus===403||e.httpStatus===429||e.source==='Vorlof'&&e.status==='pending';return {...e,status:blocked?'deferred':e.status,destinations:[...actual.map(id=>'/'+entries.get(id).section+'/'+entries.get(id).slug+'/'),...targets.filter(t=>t.startsWith('section:')).map(t=>'/'+t.slice(8)+'/'),...targets.filter(t=>t.startsWith('tool:')).map(t=>'/tools/calculator/'+t.slice(5)+'/')],reviewStatus:targets.some(x=>x.startsWith('excluded:'))&&!actual.length?'excluded':targets.some(x=>x.startsWith('empty:'))?'reviewed-empty-query':(targets.some(x=>x.startsWith('implementation:'))||e.status==='fetched'&&/\.(js|dwt|au3)$/.test(new URL(url).pathname))?'implementation-reviewed':targets.some(x=>x.startsWith('tool:'))?'tool-rebuilt':comparedSources.has(url)?'compared-and-reconciled':deferredEditorial.has(url)?'needs-editorial-review':authoredSources.has(url)?'rewritten':actual.length?'structured-extraction':targets.some(t=>t.startsWith('section:'))?'navigation-index':'unreviewed',...(blocked?{reason:'Source access denied (HTTP 403/429); refresh required when available.'}:{})}});
coverage.push(...wizardCoverage);
const counts={discovered:coverage.length,fetched:coverage.filter(x=>x.status==='fetched').length,excluded:coverage.filter(x=>x.status==='excluded'||x.reviewStatus==='excluded').length,unavailable:coverage.filter(x=>x.status==='unavailable').length,deferred:coverage.filter(x=>x.status==='deferred'||x.status==='pending').length,represented:coverage.filter(x=>x.destinations.length).length,rewritten:[...entries.values()].filter(e=>e.review==='rewritten').length};
for(const e of entries.values())if(e.kind==='ability')e.ranks.sort((a,b)=>compareRequirements(a.requirements,b.requirements)||(a.rank??0)-(b.rank??0));
const result=LibrarySchema.parse({generatedAt:new Date().toISOString(),entries:[...entries.values()].sort((a,b)=>a.id.localeCompare(b.id)),relations:[...relations.values()].filter(r=>entries.has(r.from)&&entries.has(r.to)),coverage:counts,discrepancies});
fs.mkdirSync('content/generated',{recursive:true});fs.mkdirSync('public/data',{recursive:true});
fs.writeFileSync('content/generated/library.json',JSON.stringify(result));
fs.writeFileSync('research/coverage-ledger.json',JSON.stringify(coverage,null,2));
fs.writeFileSync('research/discrepancies.json',JSON.stringify(discrepancies,null,2));
fs.writeFileSync('research/review-issues.json',JSON.stringify(issues,null,2));
fs.writeFileSync('research/variant-mappings.json',JSON.stringify([...variantMap.values()],null,2));
fs.writeFileSync('.cache/research/item-descriptions.json',JSON.stringify(rawItemRows,null,2));
const bySection={};for(const e of result.entries)(bySection[e.section]??=[]).push(catalogRecord(presentEntry(e)));
for(const [section,list] of Object.entries(bySection))fs.writeFileSync('public/data/'+section+'.json',JSON.stringify(list));
console.log(JSON.stringify({entries:result.entries.length,bySection:Object.fromEntries(Object.entries(bySection).map(([k,v])=>[k,v.length])),relations:result.relations.length,variants:variantMap.size,discrepancies:discrepancies.length,issues:issues.length,coverage:counts}));
