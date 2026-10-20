import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {createItemAcquisitionIndex,itemAcquisition,isAcquisitionTable} from '../lib/item-acquisition.ts';
const data=JSON.parse(fs.readFileSync(new URL('../content/generated/library.json',import.meta.url),'utf8'));
const index=createItemAcquisitionIndex(data.entries,data.relations);
const entry=(id,kind,title,extra={})=>({id,kind,title,slug:id.replace(':','-'),section:kind==='item'?'items':'world',aliases:[],facts:[],tables:[],related:[],variants:[],...extra});
const relation=(from,to,type,extra={})=>({from,to,type,...extra});

test('Wind Stone links back to all 50 recorded Andor floors without treating quest requirements as rewards',()=>{
 const rows=index.get('item:wind-stone');assert.ok(rows?.length);
 const andor=rows.filter(r=>r.where==='Andor');assert.equal(andor.length,1);
 assert.deepEqual(andor[0].floors,['8','14','15','17','23','26','28','29','30','33','34','35','37','38','39','40','41','42','43','54','56','57','58','60','61','62','63','64','66','67','68','69','71','72','73','74','77','79','81','82','83','84','89','90','91','96','100','101','107','113']);
 assert.equal(andor[0].recordId,'article:andor-bestiary');
 assert.ok(!rows.some(r=>/Desert Dunes/i.test(r.where+' '+r.provider)||r.method.includes('reward')));
});
test('area catalogs merge duplicate floor records, exact names only, and preserve creature provenance',()=>{
 const item=entry('item:wind','item','Wind Stone'),area=entry('article:andor','article','Andor',{tables:[
  {title:'Drops',headers:['Floor','Drops'],rows:[['008','Wind Stone'],['14','Wind Stone'],['1','Wind Stone and other items']]},
  {title:'Items',headers:['Item','Floors'],rows:[['Wind Stone','8, 14, 113']]},
  {title:'Creatures',headers:['Creature','Item Drops','Floors'],rows:[['Bat','Wind Stone','14']]}
 ]});
 const rows=createItemAcquisitionIndex([item,area],[]).get(item.id);
 assert.equal(rows.length,2);assert.deepEqual(rows.find(r=>r.method==='Area drop').floors,['8','14','113']);
 assert.equal(rows.find(r=>r.method==='Monster drop').provider,'Bat');
});
test('vendors distinguish buying from sell-back and show map, price and coordinates',()=>{
 const item=entry('i','item','Stone'),vendor=entry('v','npc','Trader',{coordinates:'(5,9)',facts:[{label:'Location',value:'Town'}],tables:[{title:'Vendor inventory',headers:['Item','Buy','Sell'],rows:[['Stone','100','50']]}]}),sell=entry('s','npc','Buyer',{tables:[{title:'Vendor inventory',headers:['Item','Buy','Sell'],rows:[['Stone','','50']]}]}),town=entry('t','location','Town');
 const rows=createItemAcquisitionIndex([item,vendor,sell,town],[relation('i','v','sold-by',{detail:'100 coins'}),relation('i','s','sold-by')]).get('i');
 assert.equal(rows.length,1);assert.equal(rows[0].locationId,'t');assert.equal(rows[0].providerId,'v');
 assert.deepEqual(rows[0].details,['Price: 100 coins','Coordinates: (5,9)']);
});
test('only rewards, recipe outputs and explicit found-in links are acquisition sources',()=>{
 const item=entry('i','item','Stone'),quest=entry('q','quest','Prize'),requirement=entry('r','quest','Bring stones'),recipe=entry('c','recipe','Polish stone'),material=entry('m','recipe','Consume stones'),place=entry('p','location','Cave'),event=entry('e','event','Festival',{historical:true});
 const rows=createItemAcquisitionIndex([item,quest,requirement,recipe,material,place,event],[relation('i','q','rewards'),relation('r','i','requires'),relation('c','i','crafts'),relation('m','i','material'),relation('i','p','found-in'),relation('e','i','rewards')]).get('i');
 assert.deepEqual(rows.map(r=>r.method).sort(),['Crafting','Event reward','Found in','Quest reward']);
 assert.ok(!rows.some(r=>r.provider==='Bring stones'||r.provider==='Consume stones'));
 assert.match(rows.find(r=>r.method==='Event reward').details.join(),/Historical/);
 assert.equal(rows.find(r=>r.method==='Quest reward').where,'');
});
test('variant drops retain their version and never become base-item drops through versionless links',()=>{
 const item=entry('i','item','Boots',{variants:[{name:'Blessed Boots'}],tables:[{title:'Documented acquisition',headers:['Location','Creature','Chance','Version'],rows:[['Cave','Bat','0.5%','Blessed Boots']]}]}),bat=entry('b','monster','Bat');
 const rows=createItemAcquisitionIndex([item,bat],[relation('i','b','drops')]).get('i');
 assert.equal(rows.length,1);assert.equal(rows[0].version,'Blessed Boots');assert.deepEqual(rows[0].details,['Chance: 0.5%']);
 assert.equal(isAcquisitionTable(item.tables[0]),true);
});
test('ambiguous aliases are not guessed; unstructured acquisition is retained without link placeholders',()=>{
 const a=entry('a','item','Stone A',{aliases:['Stone'],facts:[{label:'Acquisition',value:'Buy from Black Market for 5,000 coins.\nDrops>>>'}]}),b=entry('b','item','Stone B',{aliases:['Stone']}),area=entry('area','article','Cave',{tables:[{title:'Drops',headers:['Item','Floors'],rows:[['Stone','1']]}]});
 const rows=createItemAcquisitionIndex([a,b,area],[]);assert.equal(rows.has('b'),false);
 assert.equal(rows.get('a').length,1);assert.equal(rows.get('a')[0].method,'Vendor');assert.equal(rows.get('a')[0].details[0],'Buy from Black Market for 5,000 coins.');
});
test('the immutable library snapshot reuses its acquisition index and returns empty coverage explicitly',()=>{
 const wind=data.entries.find(e=>e.id==='item:wind-stone');
 assert.equal(itemAcquisition(wind,data.entries,data.relations),itemAcquisition(wind,data.entries,data.relations));
 assert.deepEqual(itemAcquisition(entry('missing','item','Unknown'),data.entries,data.relations),[]);
});
