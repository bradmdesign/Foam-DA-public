export const sectionInfo = [
 {id:'progression',title:'Progression',description:'Subpaths, mastery, Creants, and your next chapter.',icon:'compass'},
 {id:'quests',title:'Quests',description:'Requirements, routes, and rewards. One step at a time.',icon:'scroll'},
 {id:'items',title:'Items',description:'Equipment, materials, and where to find them.',icon:'gem'},
 {id:'abilities',title:'Skills & Spells',description:'Class abilities, learning requirements, and ranks.',icon:'sparkles'},
 {id:'world',title:'World',description:'Maps, creatures, NPCs, and hunting grounds.',icon:'map'},
 {id:'crafting',title:'Crafting',description:'Recipes, gathering, and the materials you need.',icon:'hammer'},
 {id:'knowledge',title:'Knowledge',description:'The systems and traditions of Temuair.',icon:'book'},
 {id:'events',title:'Events',description:'Seasonal journeys and past celebrations.',icon:'moon'},
 {id:'tools',title:'Tools',description:'Plan your stats, ascensions, and progression.',icon:'calculator'},
] as const;
export type SectionId=typeof sectionInfo[number]['id'];
export const navigationInfo=[{id:'classes',title:'Classes',icon:'compass'},{id:'my-skill-list',title:'My skill list',icon:'scroll'},...sectionInfo] as const;
