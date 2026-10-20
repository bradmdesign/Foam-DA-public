import test from 'node:test';
import assert from 'node:assert/strict';
import {describeAbility} from '../content/ability-effects.mjs';

// Values in Vorlof's source rows must survive normalization intact. A greedy
// prefix previously consumed all but the last digit, turning every rank into 0%.
for(const [name,percentage] of [['beag fas nadur',120],['fas nadur',150],['mor fas nadur',200],['ard fas nadur',300]]){
 test(`${name} preserves its elemental amplification`,()=>{
  const raw=`Amplifies elemental effects on the target by ${percentage}%`;
  assert.equal(describeAbility(name,raw),`Elemental amplification: ${percentage}% as listed by the source.`);
 });
}
test('amplification retains decimal precision and ignores later duration numbers',()=>{
 assert.equal(describeAbility('example','Amplifies elemental effects on the target by 125.5% for 30 seconds.'),'Elemental amplification: 125.5% as listed by the source.');
});
