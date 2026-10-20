import {referenceIndex} from '@/lib/reference-index';
import type {RecordReference} from '@/lib/record-references';

export function ReferenceSprite({record}:{record:RecordReference|null}){
 const image=record?.image;if(!image)return null;
 return <img className="sprite reference-sprite" src={image.path} width={image.width} height={image.height} loading="lazy" alt=""/>;
}
export function ReferenceText({text,preferred=[],abilities=false}:{text:string;preferred?:readonly string[];abilities?:boolean}){
 return <>{referenceIndex().parts(text,preferred,{abilities}).map((part,i)=>part.reference?<a className="inline-record" key={i} href={part.reference.href}><ReferenceSprite record={part.reference}/>{part.quantity&&<span className="reference-quantity" data-quantity={part.quantity}>{part.quantity}x</span>}<span>{part.text}</span></a>:part.text)}</>;
}
