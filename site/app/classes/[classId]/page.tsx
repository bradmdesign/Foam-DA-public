import {notFound} from 'next/navigation';
import {classFamilies} from '@/lib/class-taxonomy';
import {library,entryById} from '@/lib/library';
import {catalogRecord,isEquipment} from '@/lib/catalog-records';
import {ClassRecordList} from '@/components/class-record-list';
import {ReferenceTable} from '@/components/reference-table';
import {learningRanks} from '@/lib/learning-requirements';

export function generateStaticParams(){return classFamilies.map(c=>({classId:c.id}))}
export async function generateMetadata({params}:{params:Promise<{classId:string}>}){const {classId}=await params;const c=classFamilies.find(c=>c.id===classId);return {title:c?.name||'Class',description:c?c.name+' abilities, equipment, and '+(c.medenia?c.medenia.name+' Medenian path.':'starting progression.'):undefined}}
export default async function ClassPage({params}:{params:Promise<{classId:string}>}){
  const {classId}=await params,c=classFamilies.find(c=>c.id===classId);if(!c)notFound();
  const entries=library().entries;
  const abilities=(tag:string)=>entries.filter(e=>e.kind==='ability'&&e.tags.includes(tag)).map(e=>({...catalogRecord(e),learningRanks:e.kind==='ability'?learningRanks(e):[]}));
  const equipment=(tag:string)=>entries.filter(e=>isEquipment(e)&&e.tags.includes(tag)).map(catalogRecord);
  const mastery=entryById('article:mastery')?.tables.find(t=>t.headers[0]==='Class');
  const masteryRows=mastery?.rows.filter(row=>row[0]===c.name||row[0].startsWith(c.name+','))||[];
  return <main id="main" className="page-container reference-page class-page"><nav className="breadcrumbs" aria-label="Breadcrumb"><a href="/">Library</a><span>/</span><a href="/classes/">Classes</a><span>/</span><span>{c.name}</span></nav><div className="article-layout"><article>
    <header className="article-header"><div className="eyebrow">Class reference</div><h1>{c.name}</h1><p className="article-lead">{c.description}</p>{c.medenia&&<a className="medenian-link" href={'#'+c.medenia.id}><span aria-hidden="true">↳</span> {c.medenia.name}<span>Medenian path within {c.name}</span></a>}</header>
    <nav className="class-jump-links" aria-label={c.name+' sections'}><a href="#abilities">Skills & spells</a><a href="#equipment">Equipment</a>{c.medenia&&<a href={'#'+c.medenia.id}>{c.medenia.name}</a>}<a href="#progression">Progression</a></nav>
    <p className="catalog-order-note">Lower requirements appear first. Insight and ability levels are separate. Requirements include the displayed rank’s recorded items, gold, EXP, ability EXP and prerequisites; omitted costs are unconfirmed. Expand All ranks for later learning costs.</p>
    <section id="abilities" className="article-section"><h2>{c.name} skills & spells</h2><ClassRecordList title="Core abilities" records={abilities(c.name)} section="abilities" tag={'base:'+c.name}/></section>
    <section id="equipment" className="article-section"><h2>{c.name} equipment</h2><ClassRecordList title="Core equipment" records={equipment(c.name)} section="items" tag={'base:'+c.name}/>{c.id!=='peasant'&&<p className="class-list-footer">For more options, browse <a href="/classes/peasant/#equipment">Peasant-tagged equipment</a> and check each item’s restrictions.</p>}</section>
    {c.medenia&&<section id={c.medenia.id} className="article-section medenian-section"><div className="eyebrow">{c.name} / Medenian path</div><h2>{c.medenia.name}</h2><p>{c.medenia.name} is the Medenian path within {c.name}. Your main class remains {c.name}. Its equipment and abilities use ability levels, which are separate from insight.</p><p><a href="/progression/medenian-advancement/">Requirements for Medenian advancement</a></p><ClassRecordList title={c.medenia.name+' abilities'} records={abilities(c.medenia.name)} section="abilities" tag={c.medenia.name}/><ClassRecordList title={c.medenia.name+' equipment'} records={equipment(c.medenia.name)} section="items" tag={c.medenia.name}/></section>}
    <section id="progression" className="article-section"><h2>Plan your progression</h2>{c.id==='peasant'?<p>Choose a main class to explore its skills, spells, equipment, and Medenian path. <a href="/classes/">Browse the class pages</a>.</p>:<><p>Subpathing is an optional choice before mastery. You can remain on a pure path and master without subpathing, or change class first to carry learned abilities into that class.</p><div className="class-jump-links"><a href="/progression/subpathing/">Optional subpathing</a><a href="/progression/mastery/">Mastery</a><a href="/progression/the-four-creants/">The four Creants</a></div>{mastery&&masteryRows.length>0&&<><h3>{c.name} mastery requirements</h3><ReferenceTable title={c.name+' mastery requirements'} headers={mastery.headers} rows={masteryRows}/><p className="class-list-footer">All classes also require 200 million experience. <a href="/progression/mastery/">Read the complete mastery guide</a> before committing.</p></>}</> }</section>
    <p className="coverage-note">These class pages organize the locally sourced records. Individual entries retain restrictions, source notes, and missing-data notices.</p>
  </article><aside className="contents-rail" aria-label="On this class page"><div className="eyebrow">{c.name}</div><nav><a href="#abilities">Skills & spells</a><a href="#equipment">Equipment</a>{c.medenia&&<a className="nested-path" href={'#'+c.medenia.id}>↳ {c.medenia.name}</a>}<a href="#progression">Progression</a></nav><div className="rail-note"><a href="/classes/">All classes</a><a href="/about/">About the Library</a></div></aside></div></main>
}
