import fs from 'node:fs';
import assert from 'node:assert/strict';
import {LibrarySchema} from '../lib/models.ts';
const l=LibrarySchema.parse(JSON.parse(fs.readFileSync('content/generated/library.json','utf8')));
const ids=new Set(),routes=new Set(),missing=[];
for(const e of l.entries){assert(!ids.has(e.id),'Duplicate ID '+e.id);ids.add(e.id);const route=e.section+'/'+e.slug;assert(!routes.has(route),'Duplicate route '+route);routes.add(route);assert(e.sources.length,'No sources for '+e.id);for(const a of e.assets)if(!fs.existsSync('public'+a.path))missing.push(a.path);for(const t of e.tables)assert(t.rows.every(r=>r.length===t.headers.length),'Ragged table '+e.id);assert(!/selling\.php|\/characters?\//.test(e.sources[0].url),'Excluded source leaked '+e.id);}
for(const r of l.relations)assert(ids.has(r.from)&&ids.has(r.to),'Broken relation');
for(const e of l.entries)for(const id of e.related)assert(ids.has(id),'Broken related link');
assert.equal(missing.length,0,'Missing local assets: '+missing.slice(0,10).join(', '));
const boots=l.entries.filter(e=>e.kind==='item'&&/cordovan boots/i.test(e.title));assert.equal(boots.length,1,'Cordovan Boots must have one base entry');assert.equal(boots[0].enchantable,true);for(const m of ['Abundance','Blessed','Cail','Ceannlaidir','Deoch','Fiosachd','Glioca','Gramail','Luathas','Magic','Might','Sgrios'])assert(boots[0].aliases.includes(m+' Cordovan Boots'),'Missing boot alias '+m);
assert(ids.has('item:stick')&&ids.has('item:oak-stick'),'Distinct same-sprite items must stay separate');
const ledger=JSON.parse(fs.readFileSync('research/coverage-ledger.json','utf8'));assert.equal(ledger.length,l.coverage.discovered);const coverage={discovered:ledger.length,cachedUnrepresented:ledger.filter(e=>e.status==='fetched'&&!e.destinations.length&&!['excluded','reviewed-empty-query','navigation-index','implementation-reviewed'].includes(e.reviewStatus)).length,needsEditorialReview:ledger.filter(e=>e.reviewStatus==='needs-editorial-review').length,sourceBlocked:l.coverage.deferred};
assert.equal(coverage.cachedUnrepresented,0,'Cached source pages need a destination or an explicit review disposition');
assert.equal(coverage.needsEditorialReview,0,'Resolve pending editorial review before building');
const canonical=url=>url.replace(/^http:/,'https:').split('#')[0],knownSources=new Set(ledger.map(e=>canonical(e.url)));
const checkSource=url=>assert(knownSources.has(canonical(url)),'Evidence URL is absent from the coverage ledger: '+url);
for(const e of l.entries){for(const s of [...e.sources,...(e.ranks||[]).flatMap(r=>r.sources)])checkSource(s.url);for(const f of [...e.facts,...(e.ranks||[]).flatMap(r=>r.facts),...(e.variants||[]).flatMap(v=>v.facts)])for(const evidence of [...f.evidence,...f.alternatives])checkSource(evidence.source)}
for(const relation of l.relations)checkSource(relation.source);
for(const e of l.entries)for(const rank of e.ranks||[])for(const [field,property] of [['Ability level','ability'],['Insight','insight']]){const displayed=rank.facts.find(f=>f.label===field)?.value;if(rank.requirements[property]&&displayed)assert.equal(Number(displayed),rank.requirements[property],'Conflicting displayed requirement in '+e.id+' / '+rank.name)}
const archery=l.entries.find(e=>e.id==='ability:archery-archer-skill').ranks.find(r=>r.rank===5).facts.find(f=>f.label==='Ability level');assert.equal(archery.value,'36');assert(archery.alternatives.some(a=>a.value==='45'),'Preserve the reviewed Archery 5 source difference');
fs.mkdirSync('research',{recursive:true});fs.writeFileSync('research/validation.json',JSON.stringify({checkedAt:new Date().toISOString(),schema:'pass',uniqueIds:'pass',uniqueRoutes:'pass',localAssets:'pass',internalRelations:'pass',cordovanBoots:'pass',distinctSameSpriteItems:'pass',entries:l.entries.length,relations:l.relations.length,coverage},null,2));console.log(JSON.stringify({validated:l.entries.length,relations:l.relations.length,...coverage}));
