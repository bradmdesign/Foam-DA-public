import type {Entry,Relation} from './models';

type Item=Extract<Entry,{kind:'item'}>;
type Table=Entry['tables'][number];
export type ItemSource={method:string;where:string;provider:string;details:string[];floors:string[];version:string;recordId?:string;locationId?:string;providerId?:string};
const key=(s:string)=>s.replace(/[’‘]/g,"'").replace(/\s+/g,' ').trim().toLowerCase();
const meaningful=(s:string)=>!!s&&!/^(?:unknown|none|n\/a|\?+|drops>+|various)$/i.test(s.trim());
const lines=(s:string)=>s.split(/\r?\n/).map(s=>s.trim()).filter(meaningful);
const fact=(e:Entry,...labels:string[])=>e.facts.find(f=>labels.some(l=>key(l)===key(f.label)))?.value||'';
export const isAcquisitionTable=(t:Table)=>/^(?:Documented acquisition|Area acquisition)$/i.test(t.title)||t.headers.some(h=>/^creature$/i.test(h))&&t.headers.some(h=>/^chance$/i.test(h));

// Build once for the immutable library snapshot. Never infer acquisition from
// an ordinary prose mention or a material/prerequisite connection.
export function createItemAcquisitionIndex(entries:Entry[],relations:Relation[]){
 const byId=new Map(entries.map(e=>[e.id,e])),byName=new Map<string,Entry[]>(),itemNames=new Map<string,{item:Item;version:string}[]>(),edges=new Map<string,Relation[]>(),result=new Map<string,Map<string,ItemSource>>();
 for(const e of entries){
  for(const name of new Set([e.title,...e.aliases].map(key))){const a=byName.get(name)||[];a.push(e);byName.set(name,a);}
  if(e.kind==='item')for(const name of new Set([e.title,...e.aliases,...e.variants.map(v=>v.name)])){
   const a=itemNames.get(key(name))||[];a.push({item:e,version:e.variants.find(v=>key(v.name)===key(name))?.name||e.title});itemNames.set(key(name),a);
  }
 }
 for(const r of relations)for(const id of [r.from,r.to]){const a=edges.get(id)||[];a.push(r);edges.set(id,a);}
 const resolveItem=(name:string)=>{const a=itemNames.get(key(name))||[];return new Set(a.map(x=>x.item.id+'|'+x.version)).size===1?a[0]:undefined;};
 function local(name:string,kind:Entry['kind'],context?:Entry){
  const a=(byName.get(key(name))||[]).filter(e=>e.kind===kind);if(a.length===1)return a[0].id;
  const preferred=a.filter(e=>context?.related.includes(e.id));return preferred.length===1?preferred[0].id:undefined;
 }
 function add(item:Item,row:ItemSource){
  row={...row,details:row.details.filter(meaningful),floors:row.floors.filter(meaningful)};
  const id=[row.method,row.where,row.provider,row.version,...row.details.filter(d=>/^(?:Chance|Quantity):/.test(d))].map(key).join('|');
  let bucket=result.get(item.id);if(!bucket){bucket=new Map();result.set(item.id,bucket);}
  const old=bucket.get(id);if(old){
   const prefer=old.floors.length===0&&row.floors.length>0;
   old.details=[...new Set([...old.details,...row.details])];old.floors=[...new Set([...old.floors,...row.floors])];
   if(prefer&&row.recordId)old.recordId=row.recordId;
  }else bucket.set(id,row);
 }
 function area(item:Item,version:string,owner:Entry,where:string,floors:string[],provider='',details:string[]=[]){
  add(item,{method:provider?'Monster drop':'Area drop',where,provider,details,floors,version,recordId:owner.id,locationId:local(where,'location',owner),providerId:provider?local(provider,'monster',owner):undefined});
 }
 // Reverse the area catalogs: Floor -> Drops and Item -> Floors describe the
 // same locations, so their floor sets are merged rather than counted twice.
 for(const owner of entries)if(owner.kind==='article'&&owner.section==='world')for(const t of owner.tables){
  const h=t.headers.map(key),itemCol=h.indexOf('item'),floorCol=h.findIndex(x=>/^floors?$/.test(x)),dropCol=h.findIndex(x=>['drops','item drops','relevant drops'].includes(x)),creatureCol=h.indexOf('creature'),areaCol=h.indexOf('area');
  if(itemCol>=0&&floorCol>=0)for(const r of t.rows){const match=resolveItem(r[itemCol]||'');if(match)area(match.item,match.version,owner,owner.title,(r[floorCol]||'').split(',').map(s=>s.trim().replace(/^0+(?=\d)/,'')));}
  if(dropCol>=0&&(floorCol>=0||creatureCol>=0||areaCol>=0))for(const r of t.rows)for(const name of lines(r[dropCol]||'')){
   const match=resolveItem(name);if(!match)continue;
   const floors=floorCol>=0?(r[floorCol]||'').split(',').map(s=>s.trim().replace(/^0+(?=\d)/,'')):[];
   area(match.item,match.version,owner,areaCol>=0?r[areaCol]||owner.title:owner.title,floors,creatureCol>=0?r[creatureCol]||'':'');
  }
 }
 for(const item of entries.filter((e):e is Item=>e.kind==='item')){
  for(const t of item.tables){
   const h=t.headers.map(key),whereCol=h.findIndex(x=>['location','location and creature','map','area'].includes(x)),creatureCol=h.indexOf('creature'),versionCol=h.indexOf('version');
   if(!isAcquisitionTable(t)||whereCol<0)continue;
   for(const r of t.rows){
    const where=r[whereCol]||'',provider=creatureCol>=0?r[creatureCol]||'':'';if(!meaningful(where)&&!meaningful(provider))continue;
    const details=['quantity','chance'].flatMap(label=>{const i=h.indexOf(label);return i>=0&&meaningful(r[i]||'')?[label[0].toUpperCase()+label.slice(1)+': '+r[i]]:[];});
    add(item,{method:creatureCol>=0?'Monster drop':'Area drop',where,provider,details,floors:[],version:versionCol>=0?r[versionCol]||'Version not recorded':item.variants.length?'Version not recorded':item.title,locationId:local(where,'location',item),providerId:provider?local(provider,'monster',item):undefined});
   }
  }
  for(const r of edges.get(item.id)||[]){
   const other=byId.get(r.from===item.id?r.to:r.from);if(!other)continue;
   let method='',where='',provider=other.title,details:string[]=[];
   if(r.type==='drops'&&other.kind==='monster')method='Monster drop';
   else if(r.type==='sold-by'&&other.kind==='npc'){
    // An inventory's sell-back column is not an offer to buy this item.
    const inventory=other.tables.filter(t=>t.title==='Vendor inventory');
    if(inventory.length&&!inventory.some(t=>t.rows.some(row=>resolveItem(row[0]||'')?.item.id===item.id&&meaningful(row[1]||''))))continue;
    method='Vendor';if(r.detail)details.push('Price: '+r.detail);if(other.coordinates)details.push('Coordinates: '+other.coordinates);
   }else if(r.type==='rewards'&&(other.kind==='quest'||other.kind==='event')){method=other.kind==='event'?'Event reward':'Quest reward';if(other.historical)details.push('Historical event; check availability');}
   else if(r.type==='crafts'&&other.kind==='recipe')method='Crafting';
   else if(r.type==='found-in'&&other.kind==='location'){method='Found in';where=other.title;provider='';}
   else continue;
   where ||= fact(other,'Location','Locations','Crafting location','Map');
   const places=(edges.get(other.id)||[]).filter(x=>x.type==='found-in').map(x=>byId.get(x.from===other.id?x.to:x.from)).filter((x):x is Extract<Entry,{kind:'location'}>=>x?.kind==='location');
   const locations=where?lines(where):places.length?places.map(p=>p.title):[''];
   // Variant-only tables must never turn into base-item drops through a
   // versionless relation. The exact table above already preserves the version.
   if(item.variants.length&&r.type==='drops'&&item.tables.some(t=>isAcquisitionTable(t)&&t.headers.some(h=>key(h)==='version')))continue;
   for(const location of locations)add(item,{method,where:location,provider,details,floors:[],version:item.variants.length?'Version not recorded':item.title,recordId:other.id,locationId:local(location,'location',other),providerId:other.kind==='npc'||other.kind==='monster'?other.id:undefined});
  }
  for(const f of item.facts.filter(f=>/^(?:Acquisition|Obtained From|Obtained From:|Crafting location)$/i.test(f.label))){
   const text=lines(f.value||'').filter(s=>!/^drops>+$/i.test(s)).join(' · ');if(!text)continue;
   // Keep source instructions intact when no structured vendor/place is known.
   const method=/buy from|^vendor:/i.test(text)?'Vendor':/craft|polish/i.test(text)?'Crafting':'Recorded acquisition';
   const detailed=[...result.get(item.id)?.values()||[]];if(method==='Recorded acquisition'&&detailed.length&&/^(?:Monster Drop: Various|Monster Drop|Drops)$/i.test(text))continue;
   add(item,{method,where:'',provider:'',details:[text],floors:[],version:item.title});
  }
 }
 return new Map([...result].map(([id,rows])=>[id,[...rows.values()].map(r=>({...r,floors:r.floors.sort((a,b)=>a.localeCompare(b,undefined,{numeric:true}))})).sort((a,b)=>a.method.localeCompare(b.method)||a.where.localeCompare(b.where,undefined,{numeric:true})||a.provider.localeCompare(b.provider)||a.version.localeCompare(b.version))]));
}
const cache=new WeakMap<Entry[],{relations:Relation[];index:ReturnType<typeof createItemAcquisitionIndex>}>();
export function itemAcquisition(item:Item,entries:Entry[],relations:Relation[]){
 let value=cache.get(entries);if(value?.relations!==relations){value={relations,index:createItemAcquisitionIndex(entries,relations)};cache.set(entries,value);}
 return value!.index.get(item.id)||[];
}
