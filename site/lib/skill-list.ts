import type {Entry,Relation} from './models';
import {learningRank} from './learning-requirements.ts';
import {classFamilies,matchesClass} from './class-taxonomy.ts';

type Ability=Extract<Entry,{kind:'ability'}>;
export type LearningPlace={label:string;href?:string;trainer?:string;coordinates?:string};
export type SkillCost={name:string;quantity:number|null;unit:'Items'|'Gold'|'EXP'|'Ability EXP'|'AP';note?:string};
export type SkillChoice={id:string;abilityId:string;title:string;href:string;kind:'skill'|'spell';classes:string[];level:string;path:string;costs:SkillCost[];requirements:{label:string;values:string[]}[];places:LearningPlace[];disputed:boolean;missing:boolean;image?:string};
export type SkillListState={schema:1;selected:string[];classFilter:string;subclass:string;pathFilter:string};
export const freshSkillList=():SkillListState=>({schema:1,selected:[],classFilter:'',subclass:'',pathFilter:''});
const key=(s:string)=>s.replace(/[’‘]/g,"'").replace(/\s+/g,' ').trim().toLowerCase();
const href=(e:Entry)=>'/'+e.section+'/'+e.slug+'/';
const placeIndexes=new WeakMap<Entry[],{relations:Relation[];byId:Map<string,Entry>;locations:Map<string,Entry[]>;training:Map<string,Relation[]>}>();
function placeIndex(entries:Entry[],relations:Relation[]){
 let cached=placeIndexes.get(entries);if(cached?.relations===relations)return cached;
 const locations=new Map<string,Entry[]>(),training=new Map<string,Relation[]>();
 for(const e of entries)if(e.kind==='location'){const k=key(e.title);locations.set(k,[...locations.get(k)||[],e]);}
 for(const r of relations)if(r.type==='trained-by')training.set(r.from,[...training.get(r.from)||[],r]);
 cached={relations,byId:new Map(entries.map(e=>[e.id,e])),locations,training};placeIndexes.set(entries,cached);return cached;
}
export function learningPlaces(entry:Ability,rank:Ability['ranks'][number],entries:Entry[],relations:Relation[]):LearningPlace[]{
 const {byId,locations:locationIndex,training:trainingIndex}=placeIndex(entries,relations);
 const training=(trainingIndex.get(entry.id)||[]).filter(r=>!r.detail||key(r.detail)===key(rank.name));
 const places:LearningPlace[]=training.flatMap(r=>{
  const npc=byId.get(r.to);if(npc?.kind!=='npc')return [];
  const locations=npc.related.map(id=>byId.get(id)).filter((e):e is Extract<Entry,{kind:'location'}>=>e?.kind==='location');
  const location=npc.facts.find(f=>f.label==='Location')?.value||locations[0]?.title;
  return [{label:location||'Location not recorded',href:locations.length===1?href(locations[0]):undefined,trainer:npc.title,coordinates:npc.coordinates||undefined}];
 });
 const trainer=rank.facts.find(f=>f.label==='Trainer')?.value;
 if(trainer){
  const text=trainer.replace(/\s*\n\s*/g,' / ').trim();
  const matches=locationIndex.get(key(trainer))||[];
  if(!places.some(p=>key(p.label.replace(/\s*\(\d+\)$/,''))===key(trainer)))places.unshift({label:text,href:matches.length===1?href(matches[0]):undefined});
 }
 return places.filter((p,i,all)=>all.findIndex(q=>key(q.label)===key(p.label)&&q.trainer===p.trainer)===i);
}
export function parseSkillCost(value:string,unit:SkillCost['unit']):SkillCost{
 if(unit!=='Items'){
  const normalized=value.replace(/,/g,'').trim();const n=/^\d+(?:\.\d+)?$/.test(normalized)?Number(normalized):NaN;
  return {name:unit,unit,quantity:Number.isSafeInteger(n)&&n>=0?n:null,...(!Number.isSafeInteger(n)||n<0?{note:'Amount needs review'}:{})};
 }
 let name=value.trim(),quantity=1;
 if(/\bor\b|\bany\b|\?|\d\s*[-–]\s*\d/i.test(name))return {name,unit,quantity:null,note:'Choice or quantity needs review'};
 const front=name.match(/^(\d[\d,]*)\s*(?:[x×]\s*|\s+)(.+)$/i),end=name.match(/^(.+?)\s*(?:[([]\s*(\d[\d,]*)\s*[)\]]|[x×]\s*(\d[\d,]*))$/i);
 if(front){quantity=Number(front[1].replace(/,/g,''));name=front[2].trim();}
 else if(end){quantity=Number((end[2]||end[3]).replace(/,/g,''));name=end[1].trim();}
 return {name,unit,quantity:Number.isSafeInteger(quantity)&&quantity>=0?quantity:null};
}
export function buildSkillChoices(entries:Entry[],relations:Relation[]):SkillChoice[]{
 const items=new Map<string,string>();const ambiguous=new Set<string>();
 for(const e of entries.filter(e=>e.kind==='item')){
  const plural=/[^aeiou]y$/i.test(e.title)?e.title.slice(0,-1)+'ies':/(?:s|sh|ch|x|z)$/i.test(e.title)?e.title+'es':e.title+'s';
  for(const name of [e.title,plural]){const k=key(name),old=items.get(k);if(old&&old!==e.title)ambiguous.add(k);else items.set(k,e.title);}
 }
 return entries.filter((e):e is Ability=>e.kind==='ability'&&!e.historical&&!e.ranks.every(r=>r.facts.some(f=>f.label==='Trainer'&&f.value==='No Longer Available'))).flatMap(e=>e.ranks.map((rank,index)=>{
  const learning=learningRank(rank);
  const costRows=learning.rows.filter(r=>['Items','Gold','EXP','Ability EXP','AP'].includes(r.label));
  const costs=costRows.flatMap(row=>row.values.map(v=>parseSkillCost(v,row.label as SkillCost['unit']))).map(c=>({...c,name:c.unit==='Items'&&!ambiguous.has(key(c.name))?items.get(key(c.name))||c.name:c.name}));
  return {id:e.id+'~'+index,abilityId:e.id,title:rank.name,href:href(e),kind:e.abilityType,classes:rank.requirements.classes.length?rank.requirements.classes:e.tags.filter(t=>classFamilies.some(f=>f.name===t||f.medenia?.name===t)),level:learning.level,path:learning.path,costs,requirements:learning.rows.filter(r=>['Prerequisites','Stats','Form'].includes(r.label)),places:learningPlaces(e,rank,entries,relations),disputed:learning.disputed,missing:learning.rows.some(r=>r.label==='Learning costs'),image:e.assets.find(a=>(a.width||999)<=140&&(a.height||999)<=140)?.path};
 })).sort((a,b)=>a.title.localeCompare(b.title,undefined,{numeric:true})||a.classes.join().localeCompare(b.classes.join()));
}
export function eligibleSkill(choice:SkillChoice,state:Pick<SkillListState,'classFilter'|'subclass'|'pathFilter'>){
 const selected=state.subclass||state.classFilter;
 if(selected&&!matchesClass(choice.classes,selected))return false;
 if(state.pathFilter==='subpath'&&choice.path==='pure')return false;
 if(state.pathFilter==='pure'&&choice.path==='subpath')return false;
 return true;
}
export function skillTotals(choices:SkillChoice[]){
 const totals=new Map<string,SkillCost>();let incomplete=false;
 for(const choice of new Map(choices.map(c=>[c.id,c])).values()){
  incomplete ||= choice.missing||choice.disputed;
  for(const cost of choice.costs){if(cost.quantity===null){incomplete=true;continue;}
   const id=cost.unit+':'+key(cost.name),old=totals.get(id),sum=(old?.quantity||0)+cost.quantity;
   if(!Number.isSafeInteger(sum)){incomplete=true;totals.set(id,{...cost,quantity:null,note:'Total exceeds supported quantity'});continue;}
   if(old?.quantity===null){incomplete=true;continue;}totals.set(id,{...cost,quantity:sum});
  }
 }
 return {items:[...totals.values()].sort((a,b)=>a.unit.localeCompare(b.unit)||a.name.localeCompare(b.name)),incomplete};
}
export function restoreSkillList(raw:unknown,choices:SkillChoice[]):SkillListState{
 const value=raw as Partial<SkillListState>|null;if(!value||value.schema!==1)return freshSkillList();
 const ids=new Set(choices.map(c=>c.id));
 const classFilter=classFamilies.some(c=>c.name===value.classFilter)?value.classFilter!:'';
 const family=classFamilies.find(c=>c.name===classFilter);
 const subclass=value.subclass==='base:'+classFilter||value.subclass===family?.medenia?.name?value.subclass||'':'';
 return {schema:1,selected:Array.isArray(value.selected)?[...new Set(value.selected.filter((s):s is string=>typeof s==='string'&&ids.has(s)))].slice(0,1000):[],classFilter,subclass,pathFilter:['pure','subpath'].includes(value.pathFilter||'')?value.pathFilter!:''};
}
export function skillSheetCsv(choices:SkillChoice[]):string{
 const rows:string[][]=[['My skill list'],['Ability','Class / subclass','Requirement','Quantity','Unit','Learn at','Other requirements','Status']];
 const status=(c:SkillChoice)=>[c.missing?'Costs not recorded':'',c.disputed?'Conflicting references':''].filter(Boolean).join('; ');
 for(const c of choices){const place=c.places.map(p=>[p.label,p.trainer,p.coordinates].filter(Boolean).join(' · ')).join(' / ')||'Not recorded';
  const requirements=[c.level,c.path!=='unknown'?c.path+' path':'',...c.requirements.map(r=>r.label+': '+r.values.join(' · '))].filter(Boolean).join('; ');
  for(const cost of c.costs.length?c.costs:[{name:c.missing?'Costs not recorded':'No materials recorded',quantity:c.missing?null:0,unit:'Items'} as SkillCost])rows.push([c.title,c.classes.join(' / '),cost.name,cost.quantity===null?'Needs review':String(cost.quantity),cost.unit,place,requirements,[status(c),cost.note].filter(Boolean).join('; ')]);
 }
 const totals=skillTotals(choices);rows.push([],['Combined requirements',totals.incomplete?'Known amounts only — review flagged abilities':'']);
 rows.push(['Requirement','Quantity','Unit']);for(const c of totals.items)rows.push([c.name,c.quantity===null?'Needs review':String(c.quantity),c.unit]);
 const escape=(v:string)=>'"'+(/^[=+@\-\t\r]/.test(v)?"'"+v:v).replace(/"/g,'""')+'"';
 return rows.map(row=>row.map(escape).join(',')).join('\r\n')+'\r\n';
}
