import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {buildSkillChoices,eligibleSkill,freshSkillList,learningPlaces,parseSkillCost,restoreSkillList,skillSheetCsv,skillTotals} from '../lib/skill-list.ts';
const data=JSON.parse(fs.readFileSync(new URL('../content/generated/library.json',import.meta.url),'utf8'));
const choices=buildSkillChoices(data.entries,data.relations);
const byId=id=>{const c=choices.find(c=>c.id===id);assert.ok(c,id);return c;};

test('selected rank costs remain separate; totals count shared items and currencies once per selected rank',()=>{
 const one=byId('ability:archery-archer-skill~0'),two=byId('ability:archery-archer-skill~1');
 const result=skillTotals([one,two,two]);
 assert.equal(result.items.find(c=>c.unit==='Gold').quantity,4000000);
 assert.equal(result.items.find(c=>c.name==='Wooden Bow').quantity,5);
 assert.equal(result.items.find(c=>c.name==='Archery Book').quantity,1);
 assert.equal(result.items.find(c=>c.unit==='EXP').quantity,10000000);
 const stone=byId('ability:stone-skin-archer-spell~0');
 assert.equal(stone.costs.filter(c=>c.name==='Gold Bar').length,1);
 assert.equal(stone.costs.find(c=>c.name==='Gold Bar').quantity,1);
 assert.ok(!stone.costs.some(c=>c.name==='MP'||c.quantity===1200));
 assert.equal(skillTotals([stone,byId('ability:stone-skin-bard-spell~0')]).items.find(c=>c.name==='Gold Bar').quantity,2);
});
test('item quantity formats are normalized without treating choices as guaranteed materials',()=>{
 for(const v of ['5 Wooden Bow','5x Wooden Bow','Wooden Bow (5)','Wooden Bow x5'])assert.deepEqual(parseSkillCost(v,'Items'),{name:'Wooden Bow',quantity:5,unit:'Items'});
 assert.equal(parseSkillCost('3,000,000','Gold').quantity,3000000);
 assert.equal(parseSkillCost('0','EXP').quantity,0);
 assert.equal(parseSkillCost('Red Potion or Blue Potion','Items').quantity,null);
 assert.equal(parseSkillCost('Unknown amount','Gold').quantity,null);
});
test('class family, subclass and pure/subpath filters respect recorded eligibility',()=>{
 const archer=byId('ability:archery-archer-skill~0');
 assert.equal(eligibleSkill(archer,{classFilter:'Rogue',subclass:'',pathFilter:''}),true);
 assert.equal(eligibleSkill(archer,{classFilter:'Rogue',subclass:'base:Rogue',pathFilter:''}),false);
 assert.equal(eligibleSkill(archer,{classFilter:'Rogue',subclass:'Archer',pathFilter:''}),true);
 assert.equal(eligibleSkill(archer,{classFilter:'Wizard',subclass:'',pathFilter:''}),false);
 const pure=choices.find(c=>c.path==='pure');assert.ok(pure);
 assert.equal(eligibleSkill(pure,{classFilter:'',subclass:'',pathFilter:'subpath'}),false);
});
test('saved lists discard stale keys and invalid filters but retain valid selections',()=>{
 const c=choices[0];const state=restoreSkillList({schema:1,selected:[c.id,c.id,'ability:not-a-real-entry~0'],classFilter:'Rogue',subclass:'Summoner',pathFilter:'unexpected'},choices);
 assert.deepEqual(state.selected,[c.id]);assert.equal(state.subclass,'');assert.equal(state.pathFilter,'');
 assert.deepEqual(restoreSkillList({schema:2},choices),freshSkillList());
});
test('learning places show Stone Skin location and actual trainer coordinates, with ranks kept distinct',()=>{
 assert.ok(byId('ability:stone-skin-archer-spell~0').places.some(p=>p.label==='Aman Jungle Village'));
 const assail=byId('ability:assail-warrior-skill~0');
 assert.ok(assail.places.some(p=>p.trainer==='Keallach'&&p.coordinates==='(5,5)'));
 const entry=data.entries.find(e=>e.id==='ability:archery-archer-skill');
 const altered=[...data.relations,{from:entry.id,to:'npc:keallach-301-5-5',type:'trained-by',detail:'Archery 1'}];
 assert.ok(learningPlaces(entry,entry.ranks[0],data.entries,altered).some(p=>p.trainer==='Keallach'));
 assert.ok(!learningPlaces(entry,entry.ranks[1],data.entries,altered).some(p=>p.trainer==='Keallach'));
});
test('unknown and disputed requirements stay flagged and CSV protects formula-shaped values',()=>{
 const c=byId('ability:stone-skin-archer-spell~0');
 assert.equal(skillTotals([{...c,missing:true}]).incomplete,true);
 assert.equal(skillTotals([{...c,disputed:true}]).incomplete,true);
 const unresolved={...c,id:c.id+'x',costs:[{name:'Choice',unit:'Items',quantity:null}]};
 assert.equal(skillTotals([unresolved]).items.length,0);assert.equal(skillTotals([unresolved]).incomplete,true);
 const csv=skillSheetCsv([{...c,title:'=1+1'}]);assert.ok(csv.includes('"\'=1+1"'));assert.ok(csv.includes('Combined requirements'));assert.ok(csv.includes('Aman Jungle Village'));
});

test('retired abilities are excluded and rank names sort numerically',()=>{
 assert.ok(!choices.some(c=>c.abilityId==='ability:lumberjack-peasant-skill'));
 const arrows=choices.filter(c=>c.abilityId==='ability:arrow-shot-archer-skill');
 assert.ok(arrows.findIndex(c=>c.title==='Arrow Shot 2')<arrows.findIndex(c=>c.title==='Arrow Shot 10'));
});
