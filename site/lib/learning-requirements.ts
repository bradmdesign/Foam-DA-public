import type {Entry} from './models';

type Ability=Extract<Entry,{kind:'ability'}>;
type Rank=Ability['ranks'][number];
export type LearningRow={label:string;values:string[]};
export type LearningRank={name:string;rank:number|null;level:string;path:string;rows:LearningRow[];disputed:boolean};
const clean=(s:string)=>s.replace(/[’‘]/g,"'").replace(/\s+/g,' ').trim();
const prerequisiteNames=(value:string)=>value.split(/\n/).map(v=>clean(v).replace(/\s*\(\d+(?:\/\d+)?\)\s*$/,'').toLowerCase()).filter(v=>v&&v!=='none');
export function materialLines(value:string,prerequisites=''):string[]{
 const names=prerequisiteNames(prerequisites);
 return value.split(/\n/).map(v=>clean(v).replace(/,$/,'')).filter(v=>v&&v!=='?'&&!/^Pure (?:Rogue|Archer|Warrior|Gladiator|Priest|Bard|Wizard|Summoner|Monk|Druid)(?: Class)?$/i.test(v)&&!names.includes(v.replace(/\s*\(\d+(?:\/\d+)?\)\s*$/,'').toLowerCase()));
}
export function materialRows(value:string,prerequisites=''):LearningRow[]{
 const groups=new Map<string,string[]>();
 const add=(label:string,v:string)=>{const list=groups.get(label)||[];if(!list.includes(v))list.push(v);groups.set(label,list);};
 for(const line of materialLines(value,prerequisites)){
  if(/^none$/i.test(line)){add('Items / costs','None recorded');continue;}
  const cost=line.match(/^([\d,]+(?:\.\d+)?)\s+(Coins?|Gold|Experience|Exp\.?|XP|Ability(?:\s+(?:Exp(?:erience)?\.?|Points?))?|A\.?\s*Exp\.?|AP)$/i);
  if(cost){const unit=cost[2].toLowerCase();add(/^(?:coin|gold)/.test(unit)?'Gold':/^(?:experience|exp|xp)/.test(unit)?'EXP':/points?|^ap$/.test(unit)?'AP':'Ability EXP',cost[1]);}
  else add('Items',line);
 }
 return [...groups].map(([label,values])=>({label,values}));
}
export function materialKey(value:string,prerequisites=''):string|null{
 const rows=materialRows(value,prerequisites);if(!rows.length)return null;
 const keys=rows.flatMap(row=>row.values.map(value=>{
  if(row.label==='Items'){
   let name=value,quantity='1';const front=value.match(/^(\d+)\s+(.+)$/),end=value.match(/^(.+?)\s*[([](\d+)[)\]]$/);
   if(front){quantity=front[1];name=front[2];}else if(end){name=end[1];quantity=end[2];}
   return 'Items:'+clean(name).toLowerCase()+':'+quantity;
  }
  return row.label+':'+value.replace(/,/g,'').toLowerCase();
 }));return [...new Set(keys)].sort().join('|');
}
export function learningRank(rank:Rank):LearningRank{
 const fact=(label:string)=>rank.facts.find(f=>f.label===label);
 const prerequisites=fact('Prerequisites')?.value||'';
 const materials=fact('Learning materials');
 const alternative=rank.facts.find(f=>/^(?:Additional|Wanderlust) learning requirements$/.test(f.label));
 const rows=materials?.value?materialRows(materials.value):alternative?.value?materialRows(alternative.value,prerequisites):[];
 if(!rows.length)rows.push({label:'Learning costs',values:['Not recorded']});
 if(prerequisites&&!/^none$/i.test(prerequisites))rows.push({label:'Prerequisites',values:[clean(prerequisites)]});
 const stats=['STR','INT','WIS','CON','DEX'].flatMap(label=>{const v=fact(label+' requirement')?.value;return v===null||v===undefined?[]:[label+' '+v];});
 if(stats.length)rows.push({label:'Stats',values:stats});
 const form=fact('Required form')?.value;if(form)rows.push({label:'Form',values:[form]});
 const primary=materials?.value?materialKey(materials.value):null;
 const comparison=alternative?.value?materialKey(alternative.value,prerequisites):null;
 const req=rank.requirements;
 return {name:rank.name,rank:rank.rank,path:req.path,level:req.ability!==null?'Ability '+req.ability:req.mastery==='required'?'Mastery'+(req.insight?' · Insight '+req.insight:''):req.insight!==null?'Insight '+req.insight:'Level unknown',rows,disputed:Boolean(primary&&comparison&&primary!==comparison)||Boolean(materials?.alternatives.length)};
}
export function learningRanks(entry:Ability):LearningRank[]{return entry.ranks.map(learningRank);}
