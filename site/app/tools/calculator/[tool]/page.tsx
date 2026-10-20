import {notFound} from 'next/navigation';
import {Calculator} from '@/components/calculator';
import {ExtraTools} from '@/components/extra-tools';
import {toolInfo} from '@/lib/tool-info';
export function generateStaticParams(){return toolInfo.map(t=>({tool:t.id}))}
export async function generateMetadata({params}:{params:Promise<{tool:string}>}){const {tool}=await params;return {title:toolInfo.find(t=>t.id===tool)?.title||'Calculator'}}
export default async function CalculatorPage({params}:{params:Promise<{tool:string}>}){const {tool}=await params;const info=toolInfo.find(t=>t.id===tool);if(!info)notFound();return <main id="main" className="page-container"><nav className="breadcrumbs" aria-label="Breadcrumb"><a href="/">Library</a><span>/</span><a href="/tools/">Tools</a><span>/</span><span>{info.title}</span></nav><header className="catalog-intro"><div className="eyebrow">Plan your next step</div><h1>{info.title}</h1><p>{info.description}</p></header>{["date-comparison","level-simulator","total-experience","legend-planner","typing-practice"].includes(tool)?<ExtraTools tool={tool}/>:<Calculator tool={tool}/>}<p className="coverage-note">Uses documented game formulas and historical estimates. <a href="/progression/mastery/">Mastery requirements</a> and <a href="/progression/subpathing/">subpath requirements</a>.</p></main>}
