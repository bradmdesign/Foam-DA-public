import fs from 'node:fs';
import path from 'node:path';
import type { Entry,Library,Relation } from './models';
import {presentEntry} from './presentation';
let cached:Library|undefined;
export function library():Library {if(!cached){cached=JSON.parse(fs.readFileSync(path.join(process.cwd(),'content/generated/library.json'),'utf8')) as Library;cached.entries=cached.entries.map(presentEntry);}return cached;}
let byRoute:Map<string,Entry>|undefined;
let byId:Map<string,Entry>|undefined;
export function entryFor(section:string,slug:string){byRoute??=new Map(library().entries.map(e=>[e.section+'/'+e.slug,e]));return byRoute.get(section+'/'+slug);}
export function entryById(id:string){byId??=new Map(library().entries.map(e=>[e.id,e]));return byId.get(id);}
export function entryHref(e:Pick<Entry,'section'|'slug'>){return '/'+e.section+'/'+e.slug+'/';}
export function sectionCounts(){return Object.fromEntries([...new Set(library().entries.map(e=>e.section))].map(s=>[s,library().entries.filter(e=>e.section===s).length]));}
let relationIndex:Map<string,Relation[]>|undefined;
export function connectionLabel(current:Entry,target:Entry){if(!relationIndex){relationIndex=new Map();for(const r of library().relations){for(const id of [r.from,r.to]){const rows=relationIndex.get(id)||[];rows.push(r);relationIndex.set(id,rows)}}}const r=relationIndex.get(current.id)?.find(r=>(r.from===target.id||r.to===target.id)&&r.type!=='related');if(!r)return target.kind;const forward=r.from===current.id;switch(r.type){case 'requires':return forward?'Prerequisite':'Unlocks';case 'material':return forward?'Required material':'Used in';case 'rewards':return forward?'Reward':'Reward from';case 'crafts':return forward?'Crafting output':'Recipe';case 'sold-by':return target.kind==='npc'?'Vendor':'Sells';case 'drops':return target.kind==='item'?'Drops':'Dropped by';case 'found-in':return target.kind==='location'?'Location':'Found here';case 'trained-by':return target.kind==='npc'?'Trainer':'Teaches';case 'connects':return 'Connected location';default:return target.kind}}
