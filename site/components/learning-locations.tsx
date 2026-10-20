import type {LearningPlace} from '@/lib/skill-list';
export function LearningLocations({places}:{places:LearningPlace[]}){
 return <>{places.length?places.map((p,i)=><span key={i}>{i>0?' / ':''}{p.href?<a href={p.href}>{p.label}</a>:p.label}{p.trainer?' · '+p.trainer:''}{p.coordinates?' '+p.coordinates:''}</span>):'Not recorded'}</>;
}
