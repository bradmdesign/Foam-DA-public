import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {classFamilies,matchesClass,classHref,classTagLabel,searchClassFilters} from '../lib/class-taxonomy.ts';
import {catalogRecord,compareCatalog,compareRequirements,isEquipment} from '../lib/catalog-records.ts';
import {learningRanks,learningRank,materialKey,materialRows} from '../lib/learning-requirements.ts';
const entries=JSON.parse(fs.readFileSync('content/generated/library.json','utf8')).entries;

test('learning costs stay attached to the correct rank and include items, EXP and ability EXP',()=>{
 const archery=entries.find(e=>e.id==='ability:archery-archer-skill');const ranks=learningRanks(archery);
 assert.equal(ranks[0].level,'Ability 1');assert.ok(ranks[0].rows.find(r=>r.label==='Items').values.includes('Archery Book'));
 const third=ranks.find(r=>r.rank===3);assert.equal(third.level,'Ability 15');assert.ok(third.rows.find(r=>r.label==='EXP').values.includes('25,000,000'));assert.ok(third.rows.find(r=>r.label==='Ability EXP').values.includes('1,000'));
 assert.ok(third.rows.find(r=>r.label==='Items').values.some(v=>/Royal Bow/.test(v)));assert.ok(third.rows.find(r=>r.label==='Prerequisites').values[0].includes('Archery 2'));
 assert.ok(!ranks[0].rows.some(r=>r.label==='Ability EXP'));
});
test('material comparison normalizes quantities and spelling of currency, excluding prerequisites',()=>{
 assert.equal(materialKey('5 Wooden Bow\n3,000,000 Coins\n10,000,000 Experience'),materialKey('Archery 1 (0)\nWooden Bow (5)\n3,000,000 Gold\n10,000,000 Exp','Archery 1\n(0/100)'));
 assert.notEqual(materialKey('5 Wooden Bow'),materialKey('4 Wooden Bow'));
});
test('missing learning costs are unknown, explicit zero stays zero, and cast mana is excluded',()=>{
 const req={insight:1,ability:null,mastery:'unknown',path:'unknown',classes:[],text:''};
 const missing=learningRank({name:'Test',rank:null,requirements:req,facts:[{label:'Lines / mana cost',value:'2\n500',alternatives:[]}]});
 assert.deepEqual(missing.rows,[{label:'Learning costs',values:['Not recorded']}]);
 assert.deepEqual(materialRows('0 Gold\n0 Exp\n0 Ability'),[{label:'Gold',values:['0']},{label:'EXP',values:['0']},{label:'Ability EXP',values:['0']}]);
});

test('search class facets agree with catalog filters for every ability',()=>{
 const choices=classFamilies.flatMap(c=>c.medenia?[c.name,'base:'+c.name,c.medenia.name]:[c.name]);
 for(const e of entries.filter(e=>e.kind==='ability'))for(const choice of choices)
  assert.equal(searchClassFilters(e.tags).includes(choice),matchesClass(e.tags,choice),e.id+' '+choice);
});

test('parent filters include the Medenian path while core and path filters stay distinct',()=>{
  for(const c of classFamilies.filter(c=>c.medenia)){
    assert.equal(matchesClass([c.medenia.name],c.name),true);
    assert.equal(matchesClass([c.name],'base:'+c.name),true);
    assert.equal(matchesClass([c.medenia.name],'base:'+c.name),false);
    assert.equal(matchesClass([c.name],c.medenia.name),false);
    assert.equal(classHref(c.medenia.name),'/classes/'+c.id+'/#'+c.medenia.id);
    assert.equal(classTagLabel(c.medenia.name),c.name+' / '+c.medenia.name);
    const other=classFamilies.find(o=>o.name!==c.name&&o.medenia);
    assert.equal(matchesClass([other.medenia.name],c.name),false);
  }
});
test('level ordering separates insight, mastery, ability, and unknown requirements',()=>{
  const record=(title,level,levelType)=>({id:title,title,level,levelType});
  const sorted=[record('Unknown',null,'Unknown'),record('AB 10',10,'Ability'),record('Insight 99',99,'Insight'),record('AB 1',1,'Ability'),record('Master',null,'Mastery'),record('Insight 2',2,'Insight')].sort(compareCatalog);
  assert.deepEqual(sorted.map(e=>e.title),['Insight 2','Insight 99','Master','AB 1','AB 10','Unknown']);
  assert.ok(compareCatalog(record('Skill 2',1,'Insight'),record('Skill 10',1,'Insight'))<0);
});
test('ability catalogs use the earliest known rank requirement and ranks remain ascending',()=>{
  const abilities=entries.filter(e=>e.kind==='ability');
  assert.ok(abilities.length>100);
  let known=0;
  for(const e of abilities){
    const expected=[...e.ranks].sort((a,b)=>compareRequirements(a.requirements,b.requirements))[0];
    const record=catalogRecord(e);
    if(expected?.requirements.ability){assert.equal(record.levelType,'Ability');assert.equal(record.level,expected.requirements.ability);known++;}
    else if(expected?.requirements.insight){assert.equal(record.level,expected.requirements.insight);known++;}
    for(let i=1;i<e.ranks.length;i++)assert.ok(compareRequirements(e.ranks[i-1].requirements,e.ranks[i].requirements)<=0,e.id);
  }
  assert.ok(known>100);
});
test('every main class and Medenian path has actual ability and equipment records',()=>{
  for(const c of classFamilies.filter(c=>c.medenia))for(const tag of [c.name,c.medenia.name]){
    assert.ok(entries.some(e=>e.kind==='ability'&&e.tags.includes(tag)),tag+' abilities');
    assert.ok(entries.some(e=>isEquipment(e)&&e.tags.includes(tag)),tag+' equipment');
  }
  assert.ok(entries.find(e=>e.id==='article:mastery')?.tables.some(t=>t.headers[0]==='Class'));
});
