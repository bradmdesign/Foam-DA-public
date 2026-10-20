import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {createReferenceIndex} from '../lib/record-references.ts';
const entries=JSON.parse(fs.readFileSync('content/generated/library.json','utf8')).entries;
const index=createReferenceIndex(entries);
const refs=(text,preferred=[])=>index.parts(text,preferred).filter(p=>p.reference);

test('learning materials show verified local sprites and preserve every quantity',()=>{
 const parts=refs('5 Royal Bow\n10 Borim\n10 Green Tonic\n3,000,000 Gold');
 assert.deepEqual(parts.map(p=>[p.reference.title,p.quantity]),[['Royal Bow','5'],['Borim','10'],['Green Tonic','10']]);
 assert.ok(parts.every(p=>p.reference.image?.path.startsWith('/assets/')));
 assert.equal(refs('Finished Ruby')[0].quantity,undefined);
 assert.equal(refs('Royal Bow (5)')[0].quantity,'5');
 assert.equal(refs('20x Scrap of Clothing')[0].quantity,'20');
 assert.equal(refs('Give 20 Crystal Bars')[0].reference.title,'Crystal Bar');
 assert.equal(refs('20 Scraps of Clothing')[0].quantity,'20');
});
test('quest targets inherit the same-name creature sprite without changing the contextual route',()=>{
 const quest=entries.find(e=>e.id==='quest:aman-jungle');
 const target=refs(quest.steps[0],quest.related).find(p=>p.reference.title==='Dendron');
 assert.ok(target.reference.image);assert.equal(target.quantity,'20');
 assert.equal(target.reference.id,'monster:dendron-v-8303-856');
 assert.equal(index.record(target.reference.id).image.path,target.reference.image.path);
});
test('quoted dialogue commands remain untouched and longer item names win',()=>{
 assert.deepEqual(refs('Say “Royal Bow” or "Green Tonic".'),[]);
 assert.equal(refs('Finished Ruby')[0].reference.title,'Finished Ruby');
 assert.equal(refs('Royal Bowmaker').length,0);
});
test('missing sprites retain readable names and unknown quantities are not invented',()=>{
 const part=refs('Archery Book')[0];
 assert.equal(part.reference.image,null);assert.equal(part.quantity,undefined);
 assert.equal(refs('Ask about the Archery Book')[0].quantity,undefined);
 assert.equal(refs('At level 10 Archery Book is available')[0].quantity,undefined);
 assert.equal(refs('Insight 10\nRoyal Bow')[0].quantity,undefined);
 assert.equal(index.parts('Unrecorded quest creature')[0].text,'Unrecorded quest creature');
});
test('ambiguous aliases and competing creature artwork require a contextual match',()=>{
 const make=(id,title,kind,image,aliases=[])=>({id,title,kind,section:'world',slug:id,aliases,assets:image?[{path:image,width:32,height:32}]:[]});
 const local=createReferenceIndex([make('a','Frog','monster','/assets/frog-a.png',['Beast']),make('b','Frog','monster','/assets/frog-b.png',['Beast']),make('c','Wolf','monster','/assets/wolf.png',['Beast'])]);
 assert.equal(local.resolve('Frog'),null);assert.equal(local.resolve('Beast'),null);
 assert.equal(local.resolve('Frog',['b']).image.path,'/assets/frog-b.png');
 assert.equal(local.resolve('Beast',['c']).title,'Wolf');
 const overlap=createReferenceIndex([make('r','Ring','item','/assets/ring.png'),make('g','Golden Ring','item','/assets/gold.png'),make('w','Golden Ring','monster','/assets/wrong.png')]);
 assert.equal(overlap.parts('Golden Ring').filter(p=>p.reference).length,0);
});
test('references contain only local links and no research URLs',()=>{
 const ref=refs('20 Crystal Bar')[0].reference;
 assert.equal(ref.href,'/items/crystal-bar/');
 assert.ok(!JSON.stringify(ref).includes('http'));
 assert.deepEqual(Object.keys(ref).sort(),['href','id','image','kind','title']);
});
test('existing contextual quest and location links remain available without global prose matches',()=>{
 const quest=entries.find(e=>e.id==='quest:aman-jungle');
 const result=refs(quest.steps[0],quest.related);
 assert.ok(result.some(p=>p.reference.title==='Aman Lounge'&&p.reference.href.startsWith('/world/')));
 assert.equal(refs('Aman Lounge').length,0);
});
test('ordinary action words do not acquire an unrelated ability icon',()=>{
 assert.equal(refs('Unlock the door, then hide until nightfall.').filter(p=>p.reference.kind==='ability').length,0);
 assert.ok(refs('Learn Unlock').some(p=>p.reference.title==='Unlock'));
 assert.ok(index.parts('Unlock, Hide',[],{abilities:true}).filter(p=>p.reference?.kind==='ability').length>=1);
});
