import type {Entry} from './models';

export type RecordReference={id:string;title:string;kind:string;href:string;image:{path:string;width:number;height:number}|null};
export type ReferencePart={text:string;reference?:RecordReference;quantity?:string};
const key=(s:string)=>s.replace(/[’‘]/g,"'").replace(/\s+/g,' ').trim().toLowerCase();
const excluded=new Set(['unknown','none','other','useful','useless','item','items','weapons','gold','coins','coin','experience','ability','skill','spell']);
const referenceKinds=new Set(['item','ability','monster','npc']);
type Trie={children:Map<string,Trie>;name?:string};
const tokens=(text:string)=>[...text.matchAll(/[\p{L}\p{N}]+|[^\s]/gu)].map(m=>({text:m[0],key:key(m[0]),at:m.index!}));
function nameTrie(names:string[]):Trie{
 const root:Trie={children:new Map()};
 for(const name of names){let node=root;for(const token of tokens(name)){let next=node.children.get(token.key);if(!next){next={children:new Map()};node.children.set(token.key,next);}node=next;}node.name=name;}
 return root;
}
function mentions(words:ReturnType<typeof tokens>,trie:Trie){
 const result:{name:string;start:number;end:number}[]=[];
 for(let i=0;i<words.length;i++){
  let node:Trie|undefined=trie,best:typeof result[number]|undefined;
  for(let j=i;j<words.length;j++){node=node?.children.get(words[j].key);if(!node)break;if(node.name)best={name:node.name,start:words[i].at,end:words[j].at+words[j].text.length};}
  if(best)result.push(best);
 }
 return result;
}
function sprite(e:Entry){
 const a=e.assets.find(a=>a.width&&a.height&&a.width<=140&&a.height<=140)||
  (e.kind==='monster'?e.assets.find(a=>a.width&&a.height&&a.width<=320&&a.height<=320):undefined);
 return a?{path:a.path,width:a.width!,height:a.height!}:null;
}

// Names identify references, never game-object IDs or live entities. Duplicate
// spawn records may share artwork, but their properties and routes stay distinct.
export function createReferenceIndex(entries:Entry[]){
 const byName=new Map<string,RecordReference[]>(),byId=new Map<string,RecordReference>();
 for(const e of entries){
  const r:RecordReference={id:e.id,title:e.title,kind:e.kind,href:'/'+e.section+'/'+e.slug+'/',image:sprite(e)};
  byId.set(e.id,r);
  if(!referenceKinds.has(e.kind))continue;
  const names=[e.title,...e.aliases,...(e.kind==='ability'?e.ranks.map(r=>r.name):[])];
  for(const name of new Set(names.map(key))){
   if(name.length<3||excluded.has(name)||!/[\p{L}]/u.test(name))continue;
   const rows=byName.get(name)||[];rows.push(r);byName.set(name,rows);
  }
 }
 // Regular plurals are reference spelling only. A real title/alias always wins.
 const pluralNames:Record<string,string>={'Scrap of Clothing':'Scraps of Clothing'};
 for(const r of byId.values())if(['item','monster'].includes(r.kind)){
  const plural=pluralNames[r.title]||(/[^aeiou]y$/i.test(r.title)?r.title.slice(0,-1)+'ies':/(?:s|sh|ch|x|z)$/i.test(r.title)?r.title+'es':r.title+'s');
  const name=key(plural);if(!byName.has(name))byName.set(name,[...byName.get(key(r.title))||[]]);
 }
 // A token trie avoids compiling thousands of alternatives into one regex.
 const trie=nameTrie([...byName.keys()]);
 function resolve(name:string,preferredIds:readonly string[]=[]){
  const rows=[...(byName.get(key(name))||[]),...preferredIds.flatMap(id=>{const r=byId.get(id);return r&&!referenceKinds.has(r.kind)&&key(r.title)===key(name)?[r]:[];})],preferred=rows.filter(r=>preferredIds.includes(r.id));
  const choices=preferred.length?preferred:rows;if(!choices.length)return null;
  // Do not choose between differently named aliases, kinds, or competing sprites.
  if(new Set(choices.map(r=>r.kind+':'+key(r.title))).size!==1)return null;
  const paths=new Set(choices.flatMap(r=>r.image?[r.image.path]:[]));if(paths.size>1)return null;
  const chosen=[...choices].sort((a,b)=>Number(!!b.image)-Number(!!a.image)||a.id.localeCompare(b.id))[0];
  if(chosen.image)return chosen;
  const siblings=byName.get(key(chosen.title))?.filter(r=>r.kind===chosen.kind&&key(r.title)===key(chosen.title))||[];
  const images=new Map(siblings.flatMap(r=>r.image?[[r.image.path,r.image] as const]:[]));
  return images.size===1?{...chosen,image:[...images.values()][0]}:chosen;
 }
 function record(id:string){const r=byId.get(id);return r?resolve(r.title,[id]):null;}
 function parts(text:string,preferredIds:readonly string[]=[],options:{abilities?:boolean}={}):ReferencePart[]{
  const result:ReferencePart[]=[];
  const additional=preferredIds.flatMap(id=>{const r=byId.get(id);return r&&!referenceKinds.has(r.kind)&&r.title.length>4&&!excluded.has(key(r.title))?[r.title]:[];}).sort((a,b)=>b.length-a.length);
  const contextTrie=additional.length?nameTrie(additional):null;
  // Quoted dialogue commands must stay uninterrupted and exactly copyable.
  for(const segment of text.split(/(“[^”]*”|"[^"]*")/g)){
   if(/^[“"]/.test(segment)){result.push({text:segment});continue;}
   const words=tokens(segment),matches=[...mentions(words,trie),...(contextTrie?mentions(words,contextTrie):[])].sort((a,b)=>a.start-b.start||b.end-a.end);let cursor=0;
   for(const match of matches){
    if(match.start<cursor)continue;
    const reference=resolve(match.name,preferredIds);
    if(!reference){result.push({text:segment.slice(cursor,match.end)});cursor=match.end;continue;}
    // "Unlock the door" is ordinary prose, not a reference to the Unlock skill.
    // One-word abilities require a named field, an explicit action, or a relation.
    if(reference.kind==='ability'&&!match.name.includes(' ')&&!options.abilities&&!preferredIds.includes(reference.id)&&key(segment)!==key(match.name)&&!/^\s*\(\d+(?:\/\d+)?\)/.test(segment.slice(match.end))&&!/\b(?:cast|use|learn|spell|skill)\s*$/i.test(segment.slice(0,match.start))){result.push({text:segment.slice(cursor,match.end)});cursor=match.end;continue;}
    let start=match.start,end=match.end,quantity:string|undefined;
    if(['item','monster'].includes(reference.kind)){
     const front=segment.slice(cursor,start).match(/([\d,]+[ \t]*(?:[x×][ \t]*)?)$/);
     const suffix=segment.slice(end).match(/^[ \t]*(?:[([][ \t]*([\d,]+)[ \t]*[)\]]|[x×][ \t]*([\d,]+))/);
     if(front&&!/\b(?:insight|level|rank|ab)\s*$/i.test(segment.slice(cursor,start-front[0].length))&&(!segment[start-front[0].length-1]||!/[\p{L}\p{N}]/u.test(segment[start-front[0].length-1]))){start-=front[0].length;quantity=front[1].replace(/\s|[x×]/g,'');}
     else if(suffix){end+=suffix[0].length;quantity=suffix[1]||suffix[2];}
    }
    if(start>cursor)result.push({text:segment.slice(cursor,start)});
    result.push({text:segment.slice(match.start,match.end),reference,...(quantity?{quantity}: {})});cursor=end;
   }
   if(cursor<segment.length)result.push({text:segment.slice(cursor)});
  }
  return result;
 }
 return {resolve,record,parts};
}
