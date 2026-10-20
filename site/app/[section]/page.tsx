import fs from 'node:fs';
import {compareCatalog,defaultCatalogSort} from '@/lib/catalog-records';
import {ClassDirectory} from '@/components/class-directory';
import {ProgressionChoices} from '@/components/progression-choices';
import path from 'node:path';
import {notFound} from 'next/navigation';
import {sectionInfo} from '@/lib/sections';
import {Catalog,type CatalogRecord} from '@/components/catalog';
import {toolInfo} from '@/lib/tool-info';
export function generateStaticParams(){return sectionInfo.map(s=>({section:s.id}))}
export async function generateMetadata({params}:{params:Promise<{section:string}>}){const {section}=await params;return {title:sectionInfo.find(s=>s.id===section)?.title||'Library'}}
export default async function SectionPage({params}:{params:Promise<{section:string}>}){const {section}=await params;const s=sectionInfo.find(s=>s.id===section);if(!s)notFound();const data=JSON.parse(fs.readFileSync(path.join(process.cwd(),'public/data/'+section+'.json'),'utf8')) as CatalogRecord[];data.sort((a,b)=>compareCatalog(a,b,defaultCatalogSort(section)));return <main id="main" className="page-container"><nav className="breadcrumbs" aria-label="Breadcrumb"><a href="/">Library</a><span>/</span><span>{s.title}</span></nav><header className="catalog-intro"><div className="eyebrow">Explore the collection</div><h1>{s.title}</h1><p>{s.description}</p></header>{section==='tools'&&<div className="tool-grid">{toolInfo.map(t=><a key={t.id} href={'/tools/calculator/'+t.id+'/'}><h2>{t.title}</h2><p>{t.description}</p></a>)}</div>}{['progression','items','abilities'].includes(section)&&<section className="catalog-classes" aria-labelledby="class-pages-heading"><h2 id="class-pages-heading">Explore by class</h2><p className="muted">Dedicated pages bring each class’s abilities, equipment, and Medenian path together.</p><ClassDirectory/></section>}{section==='progression'&&<ProgressionChoices/>}<Catalog section={section} initial={data.slice(0,30)} total={data.length} kinds={[...new Set(data.map(e=>e.kind))].sort()} tags={[...new Set(data.flatMap(e=>e.tags))].filter(Boolean).sort()}/></main>}
