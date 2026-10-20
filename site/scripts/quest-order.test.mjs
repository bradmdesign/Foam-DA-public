import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {questOrder,questLevel,inQuestRealm} from '../lib/quest-order.ts';
const r=(title,level,levelType)=>({id:title,title,level,levelType});
test('quests use level 1-99, then AB 1-99, with unknown requirements last',()=>{
 const rows=[r('Unknown',null,'Unknown'),r('AB99',99,'Ability'),r('Lv99',99,'Insight'),r('AB1',1,'Ability'),r('Lv1',1,'Insight'),r('Lv20',20,'Insight')];
 assert.deepEqual(rows.sort(questOrder).map(r=>r.title),['Lv1','Lv20','Lv99','AB1','AB99','Unknown']);
});
test('mastery with a known insight uses that level; missing levels never become zero',()=>{
 const rows=[r('Unknown mastery',null,'Mastery'),r('Master',99,'Mastery'),r('AB1',1,'Ability'),r('Lv98',98,'Insight')];
 assert.deepEqual(rows.sort(questOrder).map(r=>r.title),['Lv98','Master','AB1','Unknown mastery']);
 assert.equal(questLevel(rows[1]),'Lv 99');assert.equal(questLevel(rows[2]),'AB 1');assert.equal(questLevel(rows[3]),'Level unspecified');
});
test('ties are deterministic across pagination',()=>{
 const rows=[r('Zebra',10,'Insight'),r('Alpha 10',10,'Insight'),r('Alpha 2',10,'Insight')];
 assert.deepEqual(rows.sort(questOrder).map(r=>r.title),['Alpha 2','Alpha 10','Zebra']);
});
test('Temuair and Medenia browsing retain their respective quest records',()=>{
 const level={...r('Village',1,'Insight'),summary:'A Mileth task',tags:[]};
 const ab={...r('Aman',1,'Ability'),summary:'An AB quest',tags:[]};
 const medenian={...r('Journey',99,'Insight'),summary:'Travel to Asilon',tags:[]};
 const unknown={...r('Mystery',null,'Unknown'),summary:'Unplaced quest',tags:[]};
 assert.deepEqual([level,ab,medenian,unknown].filter(q=>inQuestRealm(q,'temuair')).map(q=>q.title),['Village','Journey']);
 assert.deepEqual([level,ab,medenian,unknown].filter(q=>inQuestRealm(q,'medenia')).map(q=>q.title),['Aman','Journey']);
 assert.equal([level,ab,medenian,unknown].filter(q=>inQuestRealm(q,'all')).length,4);
});
test('the published catalog retains Medenian participation found in quest steps',()=>{
 const quests=JSON.parse(fs.readFileSync('public/data/quests.json','utf8'));
 for(const title of ['Helped Mephit','Helped Morwin','Mysterious Fruit','Unknown Depths']){
  const quest=quests.find(q=>q.title===title);
  assert.ok(quest&&inQuestRealm(quest,'medenia')&&inQuestRealm(quest,'temuair'),title);
 }
});
