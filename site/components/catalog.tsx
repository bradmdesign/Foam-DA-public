'use client';
import {useCallback,useEffect,useLayoutEffect,useMemo,useRef,useState} from 'react';
import {Search} from 'lucide-react';
import {NativeSelect} from '@/components/ui/native-select';
import {Table,TableHeader,TableBody,TableHead,TableRow,TableCell} from '@/components/ui/table';
import {Pagination,PaginationContent,PaginationItem,PaginationLink} from '@/components/ui/pagination';
import {classFamilies,classTagLabel,familyForTag,matchesClass} from '@/lib/class-taxonomy';
import {compareCatalog,defaultCatalogSort,levelLabel,type CatalogRecord} from '@/lib/catalog-records';
export type {CatalogRecord} from '@/lib/catalog-records';

type Filters={q:string;kind:string;tag:string;category:string;enchant:string;sort:string;page:number};
function defaultsFor(section:string):Filters{return {q:'',kind:'',tag:'',category:'',enchant:'',sort:defaultCatalogSort(section),page:1}}
function readFilters(section:string):Filters{
  const defaults=defaultsFor(section),p=new URLSearchParams(window.location.search),tag=p.get('tag')||'';
  const legacyCategory=tag&&!familyForTag(tag.replace(/^base:/,''));
  return {q:p.get('q')||'',kind:p.get('kind')||'',tag:legacyCategory?'':tag,category:p.get('category')||(legacyCategory?tag:''),enchant:p.get('enchant')||'',sort:p.get('sort')||defaults.sort,page:Math.max(1,Math.min(1000000,Math.floor(Number(p.get('page')))||1))};
}
function queryFor(f:Filters,section:string){const defaults=defaultsFor(section),p=new URLSearchParams();for(const [k,v] of Object.entries(f))if(v!==defaults[k as keyof Filters]&&v!=='')p.set(k,String(v));return p.size?'?'+p.toString():''}

export function Catalog({section,initial,total,kinds,tags}:{section:string;initial:CatalogRecord[];total:number;kinds:string[];tags:string[]}){
  const defaults=useMemo(()=>defaultsFor(section),[section]);
  const [data,setData]=useState(initial),[ready,setReady]=useState(false),[error,setError]=useState(''),[filters,setFilters]=useState(defaults);
  const pending=useRef<Promise<void>|null>(null),started=useRef(0),statusRef=useRef<HTMLParagraphElement>(null);
  const load=useCallback(()=>pending.current??=(fetch('/data/'+section+'.json').then(r=>{if(!r.ok)throw new Error('Catalog unavailable');return r.json() as Promise<CatalogRecord[]>}).then(records=>{setData(records);setReady(true);setError('')}).catch(()=>{pending.current=null;setError('The catalog could not be loaded. Reload this page to try again.')})),[section]);
  useEffect(()=>{const read=()=>{setFilters(readFilters(section));if(window.location.search)void load()};read();window.addEventListener('popstate',read);return()=>window.removeEventListener('popstate',read)},[load,section]);
  useLayoutEffect(()=>{if(ready&&started.current&&statusRef.current){statusRef.current.dataset.responseMs=(performance.now()-started.current).toFixed(2);started.current=0}},[ready,filters,data]);
  const indexed=useMemo(()=>data.map(e=>({...e,haystack:[e.title,...e.aliases].join(' ').toLocaleLowerCase()})),[data]);
  const results=useMemo(()=>{const terms=filters.q.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);return indexed.filter(e=>terms.every(t=>e.haystack.includes(t))&&(!filters.kind||e.kind===filters.kind)&&matchesClass(e.tags,filters.tag)&&(!filters.category||e.tags.includes(filters.category))&&(!filters.enchant||e.enchantable===true)).sort((a,b)=>compareCatalog(a,b,filters.sort))},[indexed,filters]);
  const categories=tags.filter(t=>!familyForTag(t));
  const families=classFamilies.filter(c=>tags.includes(c.name)||!!(c.medenia&&tags.includes(c.medenia.name)));
  const count=ready?results.length:total,pages=Math.max(1,Math.ceil(count/30)),page=Math.min(filters.page,pages),visible=ready?results.slice((page-1)*30,page*30):initial;
  const changed=Object.entries(filters).some(([k,v])=>v!==defaults[k as keyof Filters]);
  function change(patch:Partial<Filters>){started.current=performance.now();void load();const next={...filters,...patch,...('page' in patch?{}:{page:1})};setFilters(next);window.history.pushState(null,'',window.location.pathname+queryFor(next,section))}
  const pageHref=(n:number)=>queryFor({...filters,page:n},section)||'?page=1';
  return <div className="catalog">
    <div className="catalog-controls" onFocusCapture={()=>void load()}>
      <label className="catalog-search"><span>Find in this section</span><div><Search size={18} aria-hidden="true"/><input type="search" value={filters.q} onChange={e=>change({q:e.target.value})} placeholder="Name or alternate name"/></div></label>
      {kinds.length>1&&<label><span>Record type</span><NativeSelect value={filters.kind} onChange={e=>change({kind:e.target.value})}><option value="">All types</option>{kinds.map(k=><option key={k} value={k}>{k[0].toUpperCase()+k.slice(1)}</option>)}</NativeSelect></label>}
      {families.length>0&&<label><span>Class</span><NativeSelect value={filters.tag} onChange={e=>change({tag:e.target.value})}><option value="">All classes</option>{families.flatMap(c=>c.medenia?[<option key={c.id} value={c.name}>{c.name}</option>,<option key={c.medenia.id} value={c.medenia.name}>　{c.medenia.name}</option>]:[<option key={c.id} value={c.name}>{c.name}</option>])}</NativeSelect></label>}
      {categories.length>0&&<label><span>Category</span><NativeSelect value={filters.category} onChange={e=>change({category:e.target.value})}><option value="">All categories</option>{categories.map(t=><option key={t}>{t}</option>)}</NativeSelect></label>}
      {section==='items'&&<label><span>Enchantment</span><NativeSelect value={filters.enchant} onChange={e=>change({enchant:e.target.value})}><option value="">All items</option><option value="yes">Enchantable</option></NativeSelect></label>}
      <label><span>Sort by</span><NativeSelect value={filters.sort} onChange={e=>change({sort:e.target.value})}><option value="level">Level · low to high</option><option value="name">Name A–Z</option><option value="reverse">Name Z–A</option></NativeSelect></label>
    </div>
    {['items','abilities'].includes(section)&&<p className="catalog-order-note">Insight levels first, then mastery and ability levels. Unknown requirements appear last.</p>}
    <div className="catalog-status"><p role="status" ref={statusRef} className="number">{count.toLocaleString('en-US')} {count===1?'record':'records'}{count>0?' · '+((page-1)*30+1)+'–'+Math.min(page*30,count):''}</p>{changed&&<button onClick={()=>change(defaults)} className="text-button">Clear filters</button>}</div>
    {error&&<p role="alert" className="notice">{error}</p>}
    {!results.length?<div className="empty-state"><h2>No matching records</h2><p>Try a shorter name or remove a filter.</p><button onClick={()=>change(defaults)} className="button-primary">Clear filters</button></div>:<div className="catalog-table" tabIndex={0} role="region" aria-label="Scrollable catalog"><Table><TableHeader><TableRow><TableHead scope="col">Name</TableHead><TableHead scope="col">Type / class</TableHead><TableHead scope="col">Requirements</TableHead></TableRow></TableHeader><TableBody>{visible.map(e=><TableRow key={e.id}><TableCell><a className="record-link" href={'/'+section+'/'+e.slug+'/'}>{e.image&&<img src={e.image.path} width="36" height="36" className="sprite" loading="lazy" alt=""/>}<span>{e.title}{filters.q&&e.aliases.some(a=>a.toLowerCase().includes(filters.q.toLowerCase()))&&<small>Also found by an alternate name</small>}</span></a>{e.enchantable&&<span className="badge">Enchantable</span>}</TableCell><TableCell><span className="kind-label">{e.kind}</span>{e.tags.length>0&&<small className="table-detail">{e.tags.map(classTagLabel).join(' · ')}</small>}</TableCell><TableCell className="number">{levelLabel(e)}{e.path!=='unknown'&&<small className="table-detail">{e.path==='pure'?'Pure path':e.path==='subpath'?'Subpath':'Either path'}</small>}</TableCell></TableRow>)}</TableBody></Table></div>}
    {pages>1&&<Pagination className="catalog-pagination"><PaginationContent><PaginationItem><PaginationLink href={pageHref(Math.max(1,page-1))} aria-disabled={page===1} onClick={e=>{e.preventDefault();if(page>1)change({page:page-1})}}>Previous</PaginationLink></PaginationItem>{Array.from({length:Math.min(5,pages)},(_,i)=>Math.min(Math.max(1,page-2),Math.max(1,pages-4))+i).map(n=><PaginationItem key={n}><PaginationLink href={pageHref(n)} isActive={n===page} onClick={e=>{e.preventDefault();change({page:n})}}>{n}</PaginationLink></PaginationItem>)}<PaginationItem><PaginationLink href={pageHref(Math.min(pages,page+1))} aria-disabled={page===pages} onClick={e=>{e.preventDefault();if(page<pages)change({page:page+1})}}>Next</PaginationLink></PaginationItem></PaginationContent></Pagination>}
    <noscript><p>The first 30 entries are shown. Enable JavaScript for catalog filters; individual reference pages remain readable without it.</p></noscript>
  </div>;
}
