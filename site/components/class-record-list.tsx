import {compareCatalog,levelLabel,type CatalogRecord} from '@/lib/catalog-records';
import type {LearningRank} from '@/lib/learning-requirements';
import {ReferenceText} from '@/components/record-reference';
import {Table,TableBody,TableCell,TableHead,TableHeader,TableRow} from '@/components/ui/table';

type ClassRecord=CatalogRecord&{learningRanks?:LearningRank[]};
function LearningDetails({rank}:{rank:LearningRank}){
 return <div className="learning-details">{rank.rows.map(row=><div className="learning-row" key={row.label}><strong>{row.label}:</strong> <span>{row.values.map((value,i)=><span key={i}>{i>0?' · ':''}<ReferenceText text={value} abilities={row.label==='Prerequisites'}/></span>)}</span></div>)}{rank.disputed&&<small className="learning-dispute">Learning costs have conflicting references; see the ability’s details.</small>}</div>;
}
function Requirements({record}:{record:ClassRecord}){
 const ranks=record.learningRanks||[],first=ranks[0];
 return <><div>{levelLabel(record)}{ranks.length>1&&first.rank!==null&&<span className="learning-rank-label"> · Rank {first.rank}</span>}</div>{record.path==='pure'&&<small className="table-detail">Pure path</small>}{record.path==='subpath'&&<small className="table-detail">Subpath</small>}{first&&<LearningDetails rank={first}/>} {ranks.length>1&&<details className="learning-ranks"><summary>All {ranks.length} ranks</summary>{ranks.map((rank,i)=><section className="learning-rank" key={rank.name+':'+i}><h4>{rank.name} <span>· {rank.level}{rank.path==='pure'?' · Pure path':rank.path==='subpath'?' · Subpath':''}</span></h4><LearningDetails rank={rank}/></section>)}</details>}</>;
}
function RecordTable({records,section,title}:{records:ClassRecord[];section:'items'|'abilities';title:string}){
 return <div className="reference-table class-requirement-table" tabIndex={0} role="region" aria-label={title+' — scroll horizontally if needed'}><Table><TableHeader><TableRow><TableHead scope="col">{section==='items'?'Item':'Ability'}</TableHead><TableHead scope="col">{section==='items'?'Type':'Kind'}</TableHead><TableHead scope="col">Requirements</TableHead></TableRow></TableHeader><TableBody>{records.map(e=><TableRow key={e.id}><TableCell><a className="record-link" href={'/'+section+'/'+e.slug+'/'}>{e.image&&<img src={e.image.path} width="30" height="30" className="sprite" loading="lazy" alt=""/>}<span>{e.title}</span></a></TableCell><TableCell>{section==='abilities'?(e.tags.includes('spell')?'Spell':'Skill'):e.tags[0]||'Equipment'}</TableCell><TableCell className="number"><Requirements record={e}/></TableCell></TableRow>)}</TableBody></Table></div>;
}
export function ClassRecordList({records,section,title,tag,limit=8}:{records:ClassRecord[];section:'items'|'abilities';title:string;tag:string;limit?:number}){
  const sorted=[...records].sort((a,b)=>compareCatalog(a,b));
  return <div className="class-record-list"><div className="section-heading"><h3>{title}</h3><span>{records.length} records</span></div>{sorted.length?<><RecordTable records={sorted.slice(0,limit)} section={section} title={title}/>{sorted.length>limit&&<details className="class-more"><summary>Show {sorted.length-limit} more {title.toLowerCase()}<span>Continue in level order</span></summary><RecordTable records={sorted.slice(limit)} section={section} title={'More '+title.toLowerCase()}/></details>}<p className="class-list-footer"><a href={'/'+section+'/?tag='+encodeURIComponent(tag)}>Filter {title.toLowerCase()} in the catalog</a></p></>:<p className="muted">No records with confirmed class tags are available in this collection.</p>}</div>;
}
