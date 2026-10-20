export const classFamilies = [
  {id:'warrior',name:'Warrior',description:'Weapon skills and close-range combat.',medenia:{id:'gladiator',name:'Gladiator'}},
  {id:'wizard',name:'Wizard',description:'Elemental spells and mana-based abilities.',medenia:{id:'summoner',name:'Summoner'}},
  {id:'priest',name:'Priest',description:'Healing, protection, and support spells.',medenia:{id:'bard',name:'Bard'}},
  {id:'monk',name:'Monk',description:'Unarmed techniques and combat forms.',medenia:{id:'druid',name:'Druid'}},
  {id:'rogue',name:'Rogue',description:'Traps, precision attacks, and ranged weapons.',medenia:{id:'archer',name:'Archer'}},
  {id:'peasant',name:'Peasant',description:'Starting abilities and Peasant-tagged equipment.',medenia:null},
] as const;

export function familyForTag(tag:string){return classFamilies.find(c=>c.name.toLowerCase()===tag.toLowerCase()||c.medenia?.name.toLowerCase()===tag.toLowerCase())}
export function classTagLabel(tag:string){const family=familyForTag(tag);return family?.medenia?.name===tag?`${family.name} / ${tag}`:tag}
export function matchesClass(tags:readonly string[],selection:string){
  if(!selection)return true;
  if(selection.startsWith('base:'))return tags.includes(selection.slice(5));
  const family=classFamilies.find(c=>c.name===selection);
  return family?tags.includes(family.name)||!!(family.medenia&&tags.includes(family.medenia.name)):tags.includes(selection);
}
export function classHref(tag:string){const family=familyForTag(tag);return family?'/classes/'+family.id+'/'+(family.medenia?.name===tag?'#'+family.medenia.id:''):null}

// Reuse catalog class semantics in full-text search, including Medenian paths.
export function searchClassFilters(tags:readonly string[]){
 const choices=classFamilies.flatMap(c=>c.medenia?[c.name,'base:'+c.name,c.medenia.name]:[c.name]);
 return choices.filter(choice=>matchesClass(tags,choice));
}
