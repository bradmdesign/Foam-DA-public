'use client';
import {LearningLocations} from '@/components/learning-locations';
import {useEffect,useMemo,useState} from 'react';
import {classFamilies} from '@/lib/class-taxonomy';
import {eligibleSkill,freshSkillList,restoreSkillList,skillSheetCsv,skillTotals,type SkillChoice,type SkillListState} from '@/lib/skill-list';

type Host={postMessage:(value:unknown)=>void;addEventListener:(type:string,handler:(event:MessageEvent)=>void)=>void;removeEventListener:(type:string,handler:(event:MessageEvent)=>void)=>void};
const native=()=>{const w=window as unknown as {daSkillListHost?:boolean;chrome?:{webview?:Host}};return w.daSkillListHost?w.chrome?.webview:undefined;};
const storageKey='da-library-my-skill-list-v1';
const amount=(n:number|null)=>n===null?'Needs review':n.toLocaleString('en-US');
export function MySkillList({choices}:{choices:SkillChoice[]}){
 const [state,setState]=useState<SkillListState>(freshSkillList),[loaded,setLoaded]=useState(false),[query,setQuery]=useState(''),[limit,setLimit]=useState(60),[message,setMessage]=useState('');
 useEffect(()=>{
  const apply=(raw:unknown)=>{
   let next=restoreSkillList(raw,choices);const add=new URLSearchParams(window.location.search).get('add');
   if(add){if(choices.some(c=>c.id===add)&&!next.selected.includes(add))next={...next,selected:[...next.selected,add]};const url=new URL(window.location.href);url.searchParams.delete('add');window.history.replaceState(window.history.state,'',url.pathname+url.search+url.hash);}
   setState(next);setLoaded(true);
  };
  const host=native();if(host){
   const receive=(event:MessageEvent)=>{const v=event.data;if(v?.type==='da-skill-list-state'){apply(v.state);if(v.error)setMessage(v.error);}else if(v?.type==='da-skill-list-result')setMessage(v.message);};
   host.addEventListener('message',receive);host.postMessage({type:'da-skill-list-load'});return()=>host.removeEventListener('message',receive);
  }
  try{apply(JSON.parse(localStorage.getItem(storageKey)||'null'));}catch{apply(null);setMessage('This browser could not restore the saved list.');}
 },[choices]);
 useEffect(()=>{
  if(!loaded)return;const host=native();if(host){host.postMessage({type:'da-skill-list-save',state});return;}
  try{localStorage.setItem(storageKey,JSON.stringify(state));}catch{setMessage('This browser could not save the list. Export a sheet to keep a copy.');}
 },[loaded,state]);
 useEffect(()=>setLimit(60),[state.classFilter,state.subclass,state.pathFilter,query]);
 const selected=useMemo(()=>{const ids=new Set(state.selected);return choices.filter(c=>ids.has(c.id));},[choices,state.selected]);
 const eligible=useMemo(()=>choices.filter(c=>eligibleSkill(c,state)),[choices,state.classFilter,state.subclass,state.pathFilter]);
 const visible=useMemo(()=>{const term=query.trim().toLowerCase();return eligible.filter(c=>!term||[c.title,...c.classes,c.kind].join(' ').toLowerCase().includes(term));},[eligible,query]);
 const selectedIds=useMemo(()=>new Set(state.selected),[state.selected]);
 const totals=useMemo(()=>skillTotals(selected),[selected]);
 const family=classFamilies.find(f=>f.name===state.classFilter);
 function toggle(id:string){setState(old=>({...old,selected:old.selected.includes(id)?old.selected.filter(s=>s!==id):[...old.selected,id]}));}
 function exportSheet(){
  const csv=skillSheetCsv(selected),host=native();if(host){host.postMessage({type:'da-skill-list-export',csv});return;}
  const url=URL.createObjectURL(new Blob(['\ufeff',csv],{type:'text/csv;charset=utf-8'}));const a=document.createElement('a');a.href=url;a.download='My-skill-list.csv';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
 }
 return <div className="skill-planner">
  <section className="skill-picker no-print" aria-labelledby="skill-pick-heading">
   <h2 id="skill-pick-heading">Choose your skills & spells</h2>
   <div className="skill-filters">
    <label>Class<select aria-label="Class" value={state.classFilter} onChange={e=>setState(old=>({...old,classFilter:e.target.value,subclass:''}))}><option value="">All classes</option>{classFamilies.map(f=><option key={f.id} value={f.name}>{f.name}</option>)}</select></label>
    <label>Subclass<select aria-label="Subclass" disabled={!family?.medenia} value={state.subclass} onChange={e=>setState(old=>({...old,subclass:e.target.value}))}><option value="">Whole class family</option>{family&&<option value={'base:'+family.name}>{family.name} abilities</option>}{family?.medenia&&<option value={family.medenia.name}>{family.medenia.name}</option>}</select></label>
    <label>Path<select aria-label="Path" value={state.pathFilter} onChange={e=>setState(old=>({...old,pathFilter:e.target.value}))}><option value="">All paths</option><option value="pure">Pure</option><option value="subpath">Subpath</option></select></label>
    <label>Find an ability<input type="search" aria-label="Find an ability" value={query} onChange={e=>setQuery(e.target.value)} placeholder="Skill or spell name"/></label>
   </div>
   <p className="muted skill-hint">Select only the ranks you still need. Subclasses are Medenian paths; Pure/Subpath filters use recorded path restrictions. Changing a filter keeps your selections.</p>
   <p className="skill-count" aria-live="polite">{visible.length} matching ranks · {selected.length} selected</p>
   <div className="skill-choices" aria-label="Available skill and spell ranks" aria-busy={!loaded}>
    {visible.slice(0,limit).map(c=><div className="skill-choice" key={c.id}>
     <label><input type="checkbox" disabled={!loaded} checked={selectedIds.has(c.id)} onChange={()=>toggle(c.id)} aria-label={'Select '+c.title+' · '+c.classes.join(' / ')}/>{c.image&&<img className="sprite" src={c.image} width="24" height="24" alt="" loading="lazy"/>}<span><strong>{c.title}</strong><small>{c.classes.join(' / ')} · {c.kind} · {c.level}{c.path==='pure'?' · Pure only':c.path==='subpath'?' · Subpath':''}{c.disputed?' · Costs need review':''}</small></span></label><a href={c.href} aria-label={'Details for '+c.title+' · '+c.classes.join(' / ')}>Details</a>
    </div>)}
    {!visible.length&&<p>No abilities match these filters.</p>}
   </div>
   {visible.length>limit&&<button className="button-primary" onClick={()=>setLimit(n=>n+60)}>Show more ranks</button>}
  </section>
  <section className="skill-sheet" aria-labelledby="skill-sheet-heading">
   <div className="section-heading"><h2 id="skill-sheet-heading">My requirements sheet</h2><span>{selected.length} ranks</span></div>
   <div className="skill-sheet-actions no-print"><button className="button-primary" disabled={!selected.length} onClick={exportSheet}>Export sheet (CSV)</button><button className="button-primary" disabled={!selected.length} onClick={()=>window.print()}>Print sheet</button><button className="button-primary" disabled={!selected.length} onClick={()=>setState(old=>({...old,selected:[]}))}>Clear list</button></div>
   {message&&<p className="notice no-print" role="status">{message}</p>}
   {!selected.length?<p>Select skills and spells to build your item list.</p>:<>
    <h3>Combined requirements</h3>
    {totals.incomplete&&<p className="notice">Known amounts only. Some selected abilities have missing, uncertain or conflicting costs; review the marked rows below.</p>}
    <div className="reference-table skill-total-table"><table><thead><tr><th scope="col">Requirement</th><th scope="col">Total</th></tr></thead><tbody>{totals.items.map(c=><tr key={c.unit+':'+c.name}><td>{c.name}</td><td className="number">{amount(c.quantity)}{c.unit!=='Items'?' '+c.unit:''}</td></tr>)}</tbody></table>{!totals.items.length&&<p>No known material or currency amounts.</p>}</div>
    <h3>Requirements by ability</h3>
    {selected.map(c=><section className="skill-sheet-ability" key={c.id}>
     <div className="section-heading"><h4><a href={c.href}>{c.title}</a> <span className="muted">· {c.classes.join(' / ')}</span></h4><button className="skill-remove no-print" onClick={()=>toggle(c.id)} aria-label={'Remove '+c.title+' · '+c.classes.join(' / ')}>Remove</button></div>
     <p>{c.level}{c.path!=='unknown'?' · '+c.path+' path':''}</p>
     <p><strong>Learn at:</strong> <LearningLocations places={c.places}/></p>
     {c.disputed&&<p className="notice">Conflicting learning costs. The sheet uses the displayed requirements; review the ability record.</p>}
     <table className="skill-line-items"><thead><tr><th scope="col">Requirement</th><th scope="col">Amount</th></tr></thead><tbody>{c.costs.map((cost,i)=><tr key={i}><td>{cost.name}</td><td className="number">{amount(cost.quantity)}{cost.unit!=='Items'?' '+cost.unit:''}{cost.note?' · '+cost.note:''}</td></tr>)}</tbody></table>
     {!c.costs.length&&<p>{c.missing?'Learning costs are not recorded.':'No items or costs recorded.'}</p>}
     {c.requirements.map(row=><p key={row.label}><strong>{row.label}:</strong> {row.values.join(' · ')}</p>)}
    </section>)}
   </>}
  </section>
 </div>;
}
