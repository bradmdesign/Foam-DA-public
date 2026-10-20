import {classFamilies} from '@/lib/class-taxonomy';
export function ClassDirectory(){return <div className="class-directory">{classFamilies.map(c=><a href={'/classes/'+c.id+'/'} key={c.id}><strong>{c.name}</strong><span>{c.medenia?<><span className="class-branch" aria-hidden="true">↳</span> {c.medenia.name} <small>Medenian path</small></>:'Starting class'}</span></a>)}</div>}
