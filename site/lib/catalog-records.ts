import type {Entry} from './models';

type Requirement={insight:number|null;ability:number|null;mastery:string;path:string};
export type CatalogRecord={id:string;slug:string;title:string;kind:string;summary:string;aliases:string[];tags:string[];level:number|null;levelType:string;mastery:boolean;path:string;enchantable:boolean|null;image:{path:string;alt:string}|null};
export function requirementLevel(req?:Requirement){
  if(req?.ability)return {level:req.ability,levelType:'Ability'};
  if(req?.mastery==='required')return {level:req.insight,levelType:'Mastery'};
  return {level:req?.insight||null,levelType:req?.insight?'Insight':'Unknown'};
}
function stage(record:{level:number|null;levelType:string}){return record.levelType==='Mastery'?1:record.level===null?3:record.levelType==='Ability'?2:0}
export function compareLevels(a:{level:number|null;levelType:string},b:{level:number|null;levelType:string}){return stage(a)-stage(b)||(a.level??Infinity)-(b.level??Infinity)||0}
export function compareRequirements(a:Requirement,b:Requirement){return compareLevels(requirementLevel(a),requirementLevel(b))}
export function compareCatalog(a:CatalogRecord,b:CatalogRecord,sort='level'){
  const names=a.title.localeCompare(b.title,'en',{numeric:true,sensitivity:'base'})||a.id.localeCompare(b.id);
  return sort==='reverse'?-names:sort==='level'?compareLevels(a,b)||names:names;
}
export function defaultCatalogSort(section:string){return ['items','abilities'].includes(section)?'level':'name'}
export function levelLabel(record:{level:number|null;levelType:string}){return record.levelType==='Mastery'?'Mastery'+(record.level?' · Insight '+record.level:''):record.level!==null?record.levelType+' '+record.level:'Level unknown'}
export function catalogRecord(e:Entry):CatalogRecord{
  const asset=e.assets.find(a=>(a.width||0)<=140&&(a.height||0)<=140);
  const req=e.kind==='ability'?[...e.ranks].sort((a,b)=>compareRequirements(a.requirements,b.requirements)||(a.rank??0)-(b.rank??0))[0]?.requirements:'requirements' in e?e.requirements:undefined;
  const medenianQuest=e.kind==='quest'&&[e.title,e.summary,...e.steps].some(text=>/\b(?:Medenia|Asilon|Yowien|Aman Jungle)\b/i.test(text));
  return {id:e.id,slug:e.slug,title:e.title,kind:e.kind,summary:e.summary,aliases:e.aliases,tags:medenianQuest?[...new Set([...e.tags,'Medenia'])]:e.tags,...requirementLevel(req),mastery:req?.mastery==='required',path:req?.path||'unknown',enchantable:e.kind==='item'?e.enchantable:null,image:asset?{path:asset.path,alt:asset.alt}:null};
}
const equipmentTags=new Set(['1h','2h','Accessory','Belt','Body','Boots','Bow','Dagger','Earring','Earrings','Gauntlet','Greaves','Hands','Hat','Head','Helmet','Necklace','Overcoat','Ring','Shield','Staff','Thrown','Whip']);
export function isEquipment(e:Entry){return e.kind==='item'&&e.tags.some(t=>equipmentTags.has(t)||equipmentTags.has(t[0]?.toUpperCase()+t.slice(1)))}
