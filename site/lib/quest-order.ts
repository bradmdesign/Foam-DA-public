import type {CatalogRecord} from './catalog-records';
export function questOrder(a:CatalogRecord,b:CatalogRecord){
 const group=(r:CatalogRecord)=>r.level===null?2:r.levelType==='Ability'?1:(r.levelType==='Insight'||r.levelType==='Mastery')?0:2;
 return group(a)-group(b)||(a.level??Infinity)-(b.level??Infinity)||a.title.localeCompare(b.title,'en',{numeric:true,sensitivity:'base'})||a.id.localeCompare(b.id);
}
export function questLevel(r:CatalogRecord){return r.level===null?'Level unspecified':(r.levelType==='Ability'?'AB ':'Lv ')+r.level;}
export type QuestRealm='all'|'temuair'|'medenia';
export function inQuestRealm(r:CatalogRecord,realm:QuestRealm){
 if(realm==='all')return true;
 const medenian=(r.levelType==='Ability'&&r.level!==null&&r.level>=1&&r.level<=99)||r.tags.some(tag=>/^medenia(n)?$/i.test(tag))||/\b(?:Medenia|Asilon|Yowien|Aman Jungle)\b/i.test(r.title+' '+r.summary);
 return realm==='medenia'?medenian:r.level!==null&&r.level>=1&&r.level<=99&&(r.levelType==='Insight'||r.levelType==='Mastery');
}
