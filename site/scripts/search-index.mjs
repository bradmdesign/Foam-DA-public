import fs from 'node:fs';
import * as pagefind from 'pagefind';
import {toolInfo} from '../lib/tool-info.ts';
import {classFamilies,searchClassFilters} from '../lib/class-taxonomy.ts';
import {isEquipment} from '../lib/catalog-records.ts';
import {presentEntry} from '../lib/presentation.ts';
const library=JSON.parse(fs.readFileSync('content/generated/library.json','utf8'));
library.entries=library.entries.map(presentEntry);
const iconIndex={};
const {index,errors}=await pagefind.createIndex({forceLanguage:'en'});
if(!index||errors.length)throw new Error(errors.join('\n'));
const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
for(let start=0;start<library.entries.length;start+=100){await Promise.all(library.entries.slice(start,start+100).map(async e=>{
 const body=[e.summary,...e.steps,...e.notes,...e.facts.map(f=>f.label+' '+f.value),...e.tables.flatMap(t=>[t.headers.join(' '),...t.rows.map(r=>r.join(' '))]),...(e.ranks||[]).flatMap(r=>[r.name,r.summary,...r.facts.map(f=>f.label+' '+f.value)])].join('\n');
 const abilityFilter=e.abilityType?'<span data-pagefind-filter="abilityType">'+escape(e.abilityType)+'</span>':'';
 const equipment=isEquipment(e);
 const classFilter=e.kind==='ability'||equipment?searchClassFilters(e.tags).map(c=>'<span data-pagefind-filter="class">'+escape(c)+'</span>').join(''):'';
 const equipmentFilter=equipment?'<span data-pagefind-filter="section">equipment</span>':'';
 const image=e.assets.find(a=>(a.width||0)<=140&&(a.height||0)<=140);
 if(image)iconIndex['/'+e.section+'/'+e.slug+'/']=image.path;
 const imageMeta=image?'<meta data-pagefind-meta="image" content="'+escape(image.path)+'">':'';
 const result=await index.addHTMLFile({url:'/'+e.section+'/'+e.slug+'/',content:'<html lang="en"><head>'+imageMeta+'</head><body data-pagefind-body>'+abilityFilter+classFilter+equipmentFilter+'<h1 data-pagefind-meta="title" data-pagefind-weight="10">'+escape(e.title)+'</h1><span data-pagefind-filter="section">'+e.section+'</span><span data-pagefind-filter="kind">'+e.kind+'</span><p data-pagefind-meta="description">'+escape(e.summary)+'</p><p data-pagefind-weight="8">'+escape(e.aliases.join(' '))+'</p><div>'+escape(body)+'</div></body></html>'});
 if(result.errors.length)throw new Error(result.errors.join('\n'));
 }));if(start%2000===0)console.log('Indexed '+Math.min(start+100,library.entries.length)+' / '+library.entries.length);}
for(const t of toolInfo)await index.addCustomRecord({url:'/tools/calculator/'+t.id+'/',content:t.title+' '+t.description,language:'en',meta:{title:t.title,description:t.description},filters:{section:['tools'],kind:['calculator']}});
for(const c of classFamilies){const description=c.description+(c.medenia?' Includes the '+c.medenia.name+' Medenian path.':'');const added=await index.addHTMLFile({url:'/classes/'+c.id+'/',content:'<html lang="en"><head><meta data-pagefind-meta="description" content="'+escape(description)+'"></head><body data-pagefind-body><h1 data-pagefind-meta="title" data-pagefind-weight="10">'+escape(c.name+' class reference')+'</h1><span data-pagefind-filter="section">progression</span><span data-pagefind-filter="kind">class</span><p data-pagefind-weight="10">'+escape(c.medenia?.name||'Peasant')+'</p><p>Skills, spells and equipment.</p></body></html>'});if(added.errors.length)throw new Error(added.errors.join('\n'));}
await index.addCustomRecord({url:'/classes/',content:'Classes '+classFamilies.map(c=>c.name+' '+(c.medenia?.name||'')).join(' '),language:'en',meta:{title:'Classes & their paths',description:'Explore each main class, its skills, equipment, and nested Medenian path.'},filters:{section:['progression'],kind:['class']}});
await index.writeFiles({outputPath:'public/pagefind'});await pagefind.close();
fs.writeFileSync('public/data/search-icons.json',JSON.stringify(iconIndex));
console.log('Local search ready.');

