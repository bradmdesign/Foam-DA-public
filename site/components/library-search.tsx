'use client';
import {useEffect,useLayoutEffect,useRef,useState} from 'react';
import {Search} from 'lucide-react';
import {NativeSelect} from '@/components/ui/native-select';
import {sectionInfo} from '@/lib/sections';
import {classFamilies} from '@/lib/class-taxonomy';
import {LibraryIcon} from '@/components/library-icon';
import type {CatalogRecord} from '@/lib/catalog-records';
import {questOrder,questLevel,inQuestRealm} from '@/lib/quest-order';
import type {QuestRealm} from '@/lib/quest-order';

type Result={url:string;meta:{title:string;description?:string;image?:string};excerpt:string;levelLabel?:string};
type Pagefind={search:(q:string|null,options?:{filters?:Record<string,string>})=>Promise<{results:{data:()=>Promise<Result>}[]}>};
let engine:Promise<Pagefind>|undefined;
let questCatalog:Promise<CatalogRecord[]>|undefined;
let iconIndex:Promise<Record<string,string>>|undefined;
const restoreKey='da-library-search-return';
function getQuests(){return questCatalog??=fetch('/data/quests.json').then(r=>{if(!r.ok)throw new Error('Quest catalog unavailable');return r.json() as Promise<CatalogRecord[]>}).then(rows=>rows.sort(questOrder)).catch(e=>{questCatalog=undefined;throw e})}
function getIcons():Promise<Record<string,string>>{return iconIndex??=fetch('/data/search-icons.json').then(r=>r.ok?r.json() as Promise<Record<string,string>>:{} as Record<string,string>).catch(()=>({} as Record<string,string>))}
function getEngine(){const modulePath='/pagefind/pagefind.js';return engine??=import(/* @vite-ignore */ modulePath).catch(e=>{engine=undefined;throw e}) as Promise<Pagefind>}

export function LibrarySearch(){
 const [query,setQuery]=useState(''),[section,setSection]=useState(''),[abilityType,setAbilityType]=useState(''),[abilityClass,setAbilityClass]=useState('');
 const [browse,setBrowse]=useState(false),[questRealm,setQuestRealm]=useState<QuestRealm>('all'),[results,setResults]=useState<Result[]>([]);
 const [count,setCount]=useState(0),[limit,setLimit]=useState(20),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const serial=useRef(0),started=useRef(0),resultRef=useRef<HTMLOListElement>(null),restore=useRef<{url:string;limit:number;scrollY:number;anchor:string;offset:number}|null>(null);
 const active=!!query.trim()||browse;
 useLayoutEffect(()=>{if(!busy&&started.current&&resultRef.current){resultRef.current.dataset.responseMs=(performance.now()-started.current).toFixed(2);started.current=0}},[results,busy]);
 useLayoutEffect(()=>{
  const saved=restore.current;if(!saved||busy||count===0||results.length<Math.min(saved.limit,count))return;
  restore.current=null;
  requestAnimationFrame(()=>requestAnimationFrame(()=>{
   const anchor=[...document.querySelectorAll<HTMLAnchorElement>('.search-results a')].find(a=>a.getAttribute('href')===saved.anchor);
   if(anchor)window.scrollTo(0,Math.max(0,window.scrollY+anchor.getBoundingClientRect().top-saved.offset));
   else window.scrollTo(0,saved.scrollY);
  }));
 },[busy,results,count]);
 useEffect(()=>{
  const read=()=>{restore.current=null;const p=new URLSearchParams(window.location.search),selected=p.get('section')||'';setQuery(p.get('q')||'');setSection(selected);setAbilityType(selected==='abilities'?(p.get('type')||''):'');setAbilityClass(['abilities','equipment'].includes(selected)?(p.get('class')||''):'');setBrowse(p.get('browse')==='1');setQuestRealm(selected==='quests'&&['temuair','medenia'].includes(p.get('realm')||'')?p.get('realm') as QuestRealm:'all');
   try{const saved=JSON.parse(sessionStorage.getItem(restoreKey)||'null');if(saved?.url===window.location.href&&Number.isInteger(saved.limit)&&saved.limit>=20&&saved.limit<=10000){restore.current=saved;setLimit(saved.limit);sessionStorage.removeItem(restoreKey)}else setLimit(20)}catch{setLimit(20)}
  };
  read();window.addEventListener('popstate',read);return()=>window.removeEventListener('popstate',read);
 },[]);
 useEffect(()=>{
  const id=++serial.current;
  // This effect owns the external Pagefind request lifecycle, including reset/loading state.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  if(!active){setResults([]);setCount(0);setBusy(false);setError('');return}
  started.current=performance.now();setBusy(true);setError('');
  const timer=setTimeout(async()=>{
   try{
    const filters:Record<string,string>={};if(section)filters.section=section;if(section==='abilities'&&abilityType)filters.abilityType=abilityType;if(['abilities','equipment'].includes(section)&&abilityClass)filters.class=abilityClass;
    if(section==='quests'&&!query.trim()){
     const quests=(await getQuests()).filter(r=>inQuestRealm(r,questRealm));
     if(serial.current===id){setResults(quests.slice(0,limit).map(r=>({url:'/quests/'+r.slug+'/',meta:{title:r.title,description:r.summary,image:r.image?.path},excerpt:r.summary,levelLabel:questLevel(r)})));setCount(quests.length);setBusy(false)}
     return;
    }
    const pf=await getEngine();
    // A null query lists records matching filters without a search term.
    const found=await pf.search(query.trim()||null,{filters});
    const [data,icons]=await Promise.all([Promise.all(found.results.slice(0,limit).map(r=>r.data())),getIcons()]);
    if(serial.current===id){setResults(data.map(r=>({...r,meta:{...r.meta,image:icons[r.url]||r.meta.image}})));setCount(found.results.length);setError('');setBusy(false)}
   }catch{if(serial.current===id){setResults([]);setCount(0);setError('Search is unavailable. Browse a section below, or rebuild the local search index.');setBusy(false)}}
  },30);
  // The ref is a request generation counter, not a mounted DOM element.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  return()=>{clearTimeout(timer);if(serial.current===id)serial.current++};
 },[query,section,abilityType,abilityClass,questRealm,limit,active]);
 function update(q:string,s:string,list=false,type=abilityType,selectedClass=abilityClass,realm:QuestRealm='all'){
  if(s!=='abilities')type='';if(s!=='abilities'&&s!=='equipment')selectedClass='';
  restore.current=null;setQuery(q);setSection(s);setBrowse(list);setAbilityType(type);setAbilityClass(selectedClass);setQuestRealm(s==='quests'?realm:'all');setLimit(20);
  const p=new URLSearchParams();if(q)p.set('q',q);if(s)p.set('section',s);if(list)p.set('browse','1');if(type)p.set('type',type);if(selectedClass)p.set('class',selectedClass);if(s==='quests'&&realm!=='all')p.set('realm',realm);
  window.history.pushState(null,'','/search/'+(p.size?'?'+p.toString():''));
 }
 return <>
  <form className="search-form" role="search" onSubmit={e=>{e.preventDefault();if(!query.trim())update('',section,true,abilityType)}}>
   <Search aria-hidden="true"/><label className="sr-only" htmlFor="global-search">Search the library</label>
   <input id="global-search" type="search" value={query} onChange={e=>update(e.target.value,section,browse)} placeholder="Search names, aliases, requirements, or guide text…" autoComplete="off" autoFocus/>
   <button className="button-primary" type="submit">Search</button>
  </form>
  <div className="search-options">
   <label><span>Search within</span><NativeSelect value={section} onChange={e=>update(query,e.target.value,!query.trim())}>
    <option value="">All sections</option>{sectionInfo.flatMap(s=>s.id==='items'?[<option value={s.id} key={s.id}>{s.title}</option>,<option value="equipment" key="equipment">Equipment</option>]:[<option value={s.id} key={s.id}>{s.title}</option>])}
   </NativeSelect></label>
   {['abilities','equipment'].includes(section)&&<label className="search-class"><span>Class</span><NativeSelect aria-label={section==='equipment'?'Equipment class':'Ability class'} value={abilityClass} onChange={e=>update(query,section,!query.trim(),abilityType,e.target.value)}>
    <option value="">All classes</option>{classFamilies.flatMap(c=>c.medenia?[<option key={c.id} value={c.name}>{c.name}</option>,<option key={c.medenia.id} value={c.medenia.name}>　{c.medenia.name}</option>]:[<option key={c.id} value={c.name}>{c.name}</option>])}
   </NativeSelect></label>}
   {!query.trim()&&(section==='quests'?<><button className="button-primary" onClick={()=>update('',section,true,abilityType,abilityClass,'all')}>List all</button><button className="button-primary" onClick={()=>update('',section,true,abilityType,abilityClass,'temuair')}>List all Temuair</button><button className="button-primary" onClick={()=>update('',section,true,abilityType,abilityClass,'medenia')}>List all Medenia</button></>:<button className="button-primary" onClick={()=>update('',section,true)}>List all</button>)}
   <p role="status">{busy?'Searching…':active?count.toLocaleString('en-US')+(count===1?' result':' results'):''}</p>
  </div>
  {error&&<p role="alert" className="notice">{error}</p>}
  {!busy&&active&&!count&&!error&&<div className="empty-state"><h2>No matching records</h2><p>Try fewer words, another spelling, or all sections.</p></div>}
  <ol className={'search-results'+(browse&&!query.trim()?' browse-results':'')} ref={resultRef}>
   {results.map(r=>{const resultSection=sectionInfo.find(s=>s.id===r.url.split('/')[1]);return <li key={r.url}><a href={r.url} onClick={e=>{try{sessionStorage.setItem(restoreKey,JSON.stringify({url:window.location.href,limit,scrollY:window.scrollY,anchor:r.url,offset:e.currentTarget.getBoundingClientRect().top}))}catch{}}}><span className="search-result-icon">{r.meta.image?<img src={r.meta.image} width="32" height="32" alt="" loading="lazy"/>:<LibraryIcon name={resultSection?.icon||'book'}/>}</span><div className="search-result-copy"><span>{section==='equipment'?'Equipment':resultSection?.title||'Library'}{r.levelLabel?' · '+r.levelLabel:''}</span><h2>{r.meta.title}</h2></div></a>{!browse&&<p>{(r.meta.description||r.excerpt).replace(/<[^>]*>/g,'')}</p>}</li>})}
  </ol>
  {count>results.length&&<button className="button-primary" onClick={()=>setLimit(n=>n+20)}>Show more results</button>}
  {!active&&<div className="section-links">{sectionInfo.map(s=><a href={'/'+s.id+'/'} key={s.id}>{s.title}</a>)}</div>}
 </>
}
