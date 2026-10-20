export const toolInfo=[
 {id:'ascension',title:'Ascension cost',description:'Exact experience costs for health and mana purchases.'},
 {id:'mastery-subpath',title:'Mastery & subpath costs',description:'Compare your base HP and MP with each path’s requirements.'},
 {id:'stat-planner',title:'Stat planner',description:'Allocate base stats and check points and class caps.'},
 {id:'level-growth',title:'Level-up growth',description:'Estimate health and mana with Constitution and Wisdom.'},
 {id:'ability-progress',title:'Ability progression',description:'Plan ability points and time to your next level.'},
 {id:'weight',title:'Carry weight',description:'Inventory and equipped-weight limits from STR and insight.'},
 {id:'creature-ac',title:'Creature armor class',description:'Estimate AC from a Howl hit, INT, and defense element.'},
 {id:'time',title:'Temuair time',description:'Convert game durations to real time.'},
 {id:'date-comparison',title:'Date comparison',description:'Compare calendar dates or dates recorded in a legend mark.'},
 {id:'level-simulator',title:'Level-up simulator',description:'Spend two points at each insight and compare growth over a complete plan.'},
 {id:'total-experience',title:'Ascension investment',description:'Recreate the historical total-experience estimate from base health and mana.'},
 {id:'legend-planner',title:'Legend layout',description:'Arrange your own legend marks with accessible ordering and color controls.'},
 {id:'typing-practice',title:'Command practice',description:'Practice Monk return commands and Unknown Depths terminology.'},
] as const;
export const recordTools:Record<string,string[]>={'article:calculators':['ascension','mastery-subpath','stat-planner','ability-progress','weight'],'article:date-comparison':['date-comparison'],'article:temuair-time-calculator':['time','date-comparison'],'article:level-up-simulator':['level-simulator'],'article:level-up-gains':['level-growth'],'article:total-exp-hunted':['total-experience'],'article:statement-reordering-tool':['legend-planner'],'article:give-my-back-monk-version':['typing-practice'],'article:give-my-back-ud-version':['typing-practice'],'article:generic-stat-plans':['stat-planner','level-simulator']};
