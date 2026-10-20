import fs from 'node:fs';
// Parameters below are the reviewed defaults in Wanderlust's armortabs,
// weapontabs and equiptabs scripts, not a guessed range of database IDs.
const origin='https://www.wanderlustdb.com/';
const seeds=[];
const add=(endpoint,query,script,document)=>{
 const u=new URL(endpoint,origin);for(const [k,v] of Object.entries(query))if(v!==undefined&&v!=='')u.searchParams.set(k,String(v));u.searchParams.sort();
 seeds.push({url:u.href,discoveredFrom:origin+'js/'+script+'.js',documentUrl:origin+document+'.php'});
};
const classes=[['Peasant',null],['Monk','Druid'],['Priest','Bard'],['Rogue','Archer'],['Warrior','Gladiator'],['Wizard','Summoner']];
for(const [base,advanced] of classes){
 for(const [cls,master] of [[base,'0'],[base,'1'],...(advanced?[[advanced,'']]:[])]){
  for(const slot of advanced===cls?['Body','Head']:['Body','Head','bodyOver','headOver'])for(const gender of ['both','F','M'])
   add('armor/browse.php',{class:cls,master,slot,gender},'armortabs','armor');
  add('weapons/browse.php',{type:'all',class:cls,master,ablev:base==='Peasant'?'no':''},'weapontabs','weapons');
 }
}
add('weapons/browse.php',{type:'all',class:'Peasant',ablev:'yes'},'weapontabs','weapons');
const elems=['Earth','Wind','Fire','Sea','Metal','Nature','Dark','Light'].map(x=>'elem_'+x);
const filters={Belt:['none',...elems],Boots:['base','master','medenian','cosmetic'],Earrings:['base','master','medenian','class_Priest'],Greaves:['base','master','medenian','class_Wizard'],Hands:['base','master','medenian','hands_monk','hands_druid','class_Rogue'],Necklace:['none',...elems,'class_Priest','class_Wizard','religious'],Ring:['base','master','medenian','class_Wizard'],Shield:['base','master','medenian','class_Warrior','class_Wizard'],Accessory:['quest','event','kruna']};
for(const [slot,values] of Object.entries(filters))for(const f of values)for(const gender of slot==='Boots'?['both','F','M']:[''])add('equipment/browse.php',{slot,f,gender},'equiptabs','equipment');
fs.writeFileSync('research/dynamic-seeds.json',JSON.stringify(seeds,null,2));
console.log(`Recorded ${seeds.length} source-defined dynamic catalog endpoints.`);
