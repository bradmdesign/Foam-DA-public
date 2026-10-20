// Mechanical facts and independently written instructions, separated from flavor text.
const special={
 'Rest Cloak':'While stationary, restores 200 HP each second. Activation costs 1 mana.',
 'Blackstar Rest Cloak':'While stationary, restores 5,000 HP each second.',
 'Mantid Scent':'Magic resistance +20% for 45 minutes.',
 'Potent Mantid Scent':'Magic resistance +30% for one hour.',
 'Fungus Beetle Extract':'Clear poison from the party. Activation costs 2 mana.',
 'Assassin Scroll':'Clear blindness from the party. Activation costs 2 mana.',
 'Beetle Aid':'Revive the player directly ahead. Works in any area; cooldown is two minutes.',
 'Grime Scent':'Clear Dark Seal from yourself. Cost: 60 mana. Cooldown: ten seconds.',
 'Monster Call':'Use Howl to attract creatures. Cost: 860 mana. Cooldown: two seconds.',
 'Muscle Stimulant':'Party Damage +2 for thirty minutes. Cost: 20,000 mana.',
 'Dark Ring Spell':'Requires Dark Ring. Drain 50% of the target’s health, consuming all mana. Cooldown: thirty minutes.',
 'Dragon Fire':'Party Damage +4 and Hit +10 for ten minutes. Cost: 20,000 mana. Burns 200 HP each second while active.',
 'Dragon Scale':'Improve party armor class by 10 for thirty minutes. Cost: 20,000 mana.',
 'Cursed Notes':'Attempt an area ard cradh effect. The source reports low success. Cost: 40,000 mana. Cooldown: five minutes.',
 'Wake Scroll':'Clear party pramh. Cost: 1,000 mana.',
 '3 Move Combo':'Pure Druid combination of three skills. Cost: 1,000 mana. Cooldown: two minutes.',
 '2 Move Combo':'Subpath Druid combination of two skills. Cost: 1,500 mana. Cooldown: two minutes.',
 'Damage Scroll':'Health-based attack usable below 2% current health. Cooldown: twenty-eight seconds.',
 'Sprint Potion':'Charge forward up to four tiles. Cooldown: fifteen seconds.',
 'Vanishing Elixir':'Hide the party for nine minutes. Cost: 1,000 mana.',
 'Glowing Stone':'Attempt eight seconds of invulnerability. The effect can fail. Cost: 1,000 mana.',
 'Nerve Stimulant':'Party Hit +20 for forty-five minutes. Cost: 20,000 mana.',
 'Bowl of Locky’s Charms':'Permanently adds 1,000 base health and 1,000 base mana. The documented use limit is once per year.',
 "Bowl of Locky's Charms":'Permanently adds 1,000 base health and 1,000 base mana. The documented use limit is once per year.',
 'Water Barrel':'For Green Ale, fill the barrel at Mileth (40,61). Return to Franchesco and say “I have your Water Barrel”.',
 'Barley':'For Green Ale, collect ten Barley in the Suomi fields. Follow alternating even-numbered rows, such as y70, y72, and y74, to avoid ejection. Tell Franchesco “I have your Barley”.',
 'Dried Hops':'Ask Qaeli in the Shinewood Forest 17 hut for “Dried Hops”. Return to Franchesco with “I have your Dried Hops”.',
 'Ale Yeast':'Ask Lia at Piet Tavern for “Ale Yeast”. Return to Franchesco with “I have your Ale Yeast”.',
 'Green Algae':'Collect five Green Algae along the Karlopos Island beach for Green Ale. Tell Franchesco “I have your Green Algae”.',
 'Yowien Braided Vine':'Provides access through the Yowien Territory waterfall to area 15 onward. Only the character who made the vine can use it.',
 'Ash Diamond':'Fire Drake drop in Chaos 30–33. The group needs at least one of every class, and a Rogue must land the killing blow.',
 'Chamlil':'Fire Drake drop in Chaos. The group needs at least one of every class, and a Priest or Warrior must land the killing blow.',
 'Light Orb':'Fire Drake drop in Chaos 30–33. The group needs at least one of every class, and a Wizard must land the killing blow.',
 'Hydraco Horn':'The two-headed Fire Hydraco in Chaos 24–33 requires a group containing every class for this drop. The source also lists ordinary drops from the single-headed Fire Hydraco.',
 'Hydraco Scale':'Drop from the two-headed Fire Hydraco in Chaos 24–33. The group must include at least one of every class.',
 'Mysterious Egg':'Turn in ten eggs to Areini for Pet Hen, then another ten for Pet Rooster during the Floppy event. Say “I found a Mysterious Egg”.',
 'Soccer Ball':'Move the ball by double-clicking it; its weight prevents normal pickup.',
 'Mortair Map':'Reading consumes the map. Wanderlust says reading it is optional for the associated quest.',
 'Silver Wolf Leather':'Protects against the Giant Mantis instant-death attack.',
 'Gold Bar':'Stored currency value: 50,000,000 coins.',
 'Dark Ring':'Equipment prerequisite for the Dark Ring Spell trinket.',
 'Heart Puppet':'Wearable by either gender. The source describes a limited availability period without a current schedule.',
 'Crux':'Material for empowering a Master weapon.',
 "Demon Lord's Notes":'Used to locate Dark Artifacts.',
 'Empty Ice Bottle':'Fill at the Desert Dunes Oasis to obtain an Ice Bottle.',
 'Lady Phoenix Rings':'Required for summoning the Lady Phoenix Creant.',
 'Note to Baltasar':'Optional hand-in to Baltasar during the Necklace quest.',
 'Parchment Scroll':'Wizardry Research material for creating Arcane Songs.',
 'Pet Egg':'Hatches into a Pet Penguin.',
 'Sand Glass':'Required for Monk mastery.',
 'Temuairian Botany Book':'A reference to flowers found in Temuair and Medenia.',
 'Undine Mushroom':'Material for Shrink Potions, which provide access to Muisir Village.',
 "Bully's Seaweed":'Hand in 25. The source example at Ability 80 awards 1,100,000 XP and 35,000 AP; other levels are not established.',
 'Scavenger Item':'Objective item for the Oren Ball scavenger hunt.',
 'Moss Mantid':'Entry for the quarterly Insect Tournament.',
 "pieydagnelle's eggs?":'Objective item for the Suomi Troupe egg hunt.',
 'Smokey Sphere':'Blinds opponents. The source does not establish the duration or whether its range covers the screen or the whole map.',
 'Spring Bouquet':'Sold by the Piet and Rucesion innkeepers for 100,000 coins.',
 'Useless Boot':'A fishing catch.',
 'Male Jester Shoes':'One of the possible contents of the Kruna item Joker Gift Box.',
};
export function itemEffects(name,raw){
 const facts=[],notes=[],tables=[];let handled=false;
 const add=(k,v)=>{if(v&&!facts.some(f=>f[0]===k&&f[1]===v))facts.push([k,String(v)]);handled=true};
 if(special[name]){notes.push(special[name]);handled=true}
 const text=raw.trim();let m;
 if(/Required to summon the Lady Phoenix Creant/.test(text))add('Summoning material','Lady Phoenix Creant');
 if(/Quarterly Insect Tournement/.test(text))add('Event use','Quarterly Insect Tournament');
 if(/Obtained from Joker Gift Box Kruna item/.test(text))add('Acquisition','Joker Gift Box (Kruna)');
 if(/^\?+$/.test(text))return {facts,notes,tables,handled:true};
 if((m=text.match(/(?:Teleports? (?:you|the user)|Allows the user to teleport) to (.+?)(?:\.|$)/i)))add('Travel destination',m[1].replace(/^one of the /,'').replace(/^the /,''));
 if((m=text.match(/(\d+)\s*(hour|minute|second)s? cooldown/i)))add('Cooldown',m[1]+' '+m[2]+(m[1]==='1'?'':'s'));
 if(/Chaos 25.*Andor 80/.test(text))add('Additional travel destinations','Chaos 25; Andor 80');
 if(/last spot you died/i.test(text))notes.push('Recall includes deaths during ascension or in an arena.');
 if((m=text.match(/(?:Transforms? (?:the user|you|yourself) into|Change into) (?:a |an )?(.+?)(?:\.|\n|$)/i)))add('Transformation',m[1]);
 if(/Faster walk speed|increasing movement speed|increases movement speed|Movement speed .*increased/i.test(text))add('Movement effect',/Slightly/i.test(text)?'Small speed increase':'Faster movement');
 if(/Allows entrance to Hostile Grounds/.test(text))add('Access requirement','Hostile Grounds');
 if(/Removes blind effects/.test(text))add('Cleanses','Blindness; self and party');
 if((m=text.match(/Costs ([\d,]+) mana per use/i)))add('Activation mana',m[1]);
 if(/Pure Druids may use 3/.test(text))add('Combo','Three skills; pure Druid');
 if(/Subpath Druids may use 2/.test(text))add('Combo','Two skills; subpath Druid');
 if(/choice of 6 notes/.test(text))add('Music options','Six notes; two wedding songs');
 if((m=text.match(/Equip (?:required (?:for|to)|to gain entry to) (.+?)(?:\.|$)/i)))add('Equipment use',m[1]);
 if((m=text.match(/Shine a (\w+) light in (.+?)\./i)))add('Light',m[1]+'; '+m[2]);
 for(const bonus of text.matchAll(/([+-]\d+)\s*(HP|MP|AC)\b/g))add({HP:'Health',MP:'Mana',AC:'Armor class'}[bonus[2]],bonus[1]);
 if(/Harvest or absorb fior potions/i.test(text))add('Use','Harvest or absorb elemental fior potions');
 if(/^On the Arts\.?$/.test(text))add('Quest use','On the Arts');
 if((m=text.match(/Damage health by (\d+)/i)))add('Health lost on use',m[1]);
 if((m=text.match(/Nutrition ([+-]?\d+)/i))){add('Nutrition',m[1]);if(/does not heal/i.test(text))add('Healing','None');}
 if(/Weather Protection/.test(text)){add('Protection','Desert Dunes HP and MP drain');add('Access requirement','Pyramid');}
 if(/Drains you to 1 hp/i.test(text))add('Remaining HP after use','1');
 if(/Tagor Citizen.*wake/i.test(text)){add('Quest use','Wake a Tagor Citizen in Lost Souls Pass');notes.push('The source is uncertain whether Bardo affects four nearby targets.');}
 if(/Gemstones.*spell[\s\S]*not sure/i.test(text)){notes.push('Applies Gemstones to up to four nearby targets for one or two seconds. Its mechanical effect is unknown in the source. Used during Light vs Dark War.');handled=true;}
 if(/animation/i.test(text)){const animation=text.split('\n')[0].replace(/\b(?:Displays|Massive|type|on self)\b/gi,'').trim();add('Visual effect',animation.replace(/^['"]|['"]$/g,''));}
 if(/express their love/i.test(text))add('Use','Love expression from the faced target');
 if(/Must be tiny to enter Muisir/i.test(text))add('Use','Become tiny to enter Muisir Village');
 if(/Rare chance.*Light Necklace/.test(text)){add('Possible reward','Light Necklace');notes.push('The source does not establish an exact drop probability.');}
 if(/map you are on snow/i.test(text))add('Map effect','Snow and Jingle Bells music');if(/stop snowing/i.test(text))add('Map effect','Stop snowfall');
 if(/Change your gender/i.test(text))add('Use','Change character gender');
 if(/Deerstalker Hat\/Armor\/Shoes equip required/i.test(text))add('Mount requirements','Deerstalker Hat, Armor, and Shoes');
 if((m=text.match(/^Crafting (.+?)\.?$/i)))add('Crafting use',m[1]);if(/^Crafting\.?$/i.test(text))add('Use','Crafting material');
 if((m=text.match(/^Give to (.+?) for (.+?)\.$/i)))add('Exchange',m[1]+' → '+m[2]);
 if((m=text.match(/^Used to create (?:a |an )?(.+?)\.$/i)))add('Crafting output',m[1]);
 if(/Break out of Pirate Jail/i.test(text))add('Use','Escape Pirate Jail');
 if((m=text.match(/Gain access to shortcut to (.+?)\.$/i)))add('Shortcut',m[1]);
 if(/Cooking at Oren Ball/.test(text)){add('Use','Oren Ball cooking');notes.push('This Cauldron version cannot be used for mastery. Same-name Cauldrons in other records are not interchangeable.');}
 if(/Required to make Gold Sand for Monk Mastering/.test(text))add('Mastery material','Monk mastery; Gold Sand recipe');
 if((m=text.match(/^Trade for (.+?) at (.+?)\.$/i)))add('Exchange',m[2]+' → '+m[1]);
 if(/Pure Only/i.test(text))add('Path restriction','Pure');
 if(/Sub Class Only/i.test(text))add('Path restriction','Subpath');
 if(/Both genders/i.test(text))add('Gender','Any');else if(/Female Only/i.test(text))add('Gender','Female');else if(/Male Only/i.test(text))add('Gender','Male');
 if(/Dyeable/i.test(text))add('Dyeable','Yes');if(/Smithable/i.test(text))add('Smithable','Yes');
 for(const found of text.matchAll(/(Earth|Fire|Wind|Water|Sea|Nature|Metal|Dark|Light|Holy) (Defense|Offense)/gi))add(found[2].toLowerCase()==='defense'?'Defense element':'Offense element',found[1]);
 if(/^No Element\.?$/i.test(text))add('Element','None');
 if((m=text.match(/All (.+?) become (instant|\d+ lines?)/i)))add('Casting modifier',m[1]+' → '+m[2]);
 if((m=text.match(/(-\d+) lines? to (.+?)(?:\n|$)/i)))add('Casting reduction',m[1]+' lines; '+m[2]);
 if((m=text.match(/^(.+?) Ability\.?$/)))add('Enabled ability',m[1]);
 if((m=text.match(/Requires (\d+) Noble Marks/i)))add('Noble marks required',m[1]);
 if((m=text.match(/^(Pirate|Ant) Stealth$/)))add('Stealth effect',m[1]);
 if((m=text.match(/Disables:\s*([\s\S]+)/)))add('Disabled abilities',m[1].split('\n').join(' · '));
 if((m=text.match(/(?:Heals?|Restore[sd]?|Regain[sd]?)\s+([\d,]+)\s*(?:HP|Health)/i)))add('Health restored',m[1]);
 if((m=text.match(/(?:Restore[sd]?|Regain[sd]?)\s+([\d,]+)\s*(?:MP|Mana)/i)))add('Mana restored',m[1]);
 if(/(?:full|max(?:imum)?) health|100% (?:HP|health)/i.test(text))add('Health restoration','Full');
 if(/(?:full|max(?:imum)?) mana|100% (?:MP|mana)/i.test(text))add('Mana restoration','Full');
 if(/Cures? poison/i.test(text))add('Cleanses','Poison');if(/Resurrects?|revives?/i.test(text))add('Use','Revival');
 if((m=text.match(/(?:Lasts?|Duration:?|for)\s+(\d+(?:\.\d+)?)\s*(seconds?|minutes?|hours?)/i)))add('Effect duration',m[1]+' '+m[2]);
 if((m=text.match(/(?:increas\w+|bonus)[\s\S]{0,100}?(\d+)%/i))&&/Experience|Ability/i.test(text))add('Reward bonus',m[1]+'% '+(/Experience.*Ability|Ability.*Experience|Exp.?Ap/i.test(text)?'XP and AP':/Ability/i.test(text)?'AP':'XP'));
 if(/DOUBLE/i.test(text)&&/experience.*ability|exp.?ap/i.test(text))add('Reward bonus','100% XP and AP');
 if((m=text.match(/\+([\d]+) all stats/i)))add('All-stat bonus','+'+m[1]);
 if((m=text.match(/Polish to (\w+)/i)))add('Polishing result grade',m[1]);
 if(/Skills\/Spells/i.test(text))add('Training material','Skills and spells');
 if((m=text.match(/^([\d,]+(?:\s*-\s*[\d,]+)?) Gold\.?$/)))add('Currency amount',m[1]);
 if((m=text.match(/Give to oren jeweler for ([\d,]+) Exp/i)))add('Oren jeweler reward',m[1]+' XP');
 if(/Changes an Aislings face/i.test(text))add('Use','Face appearance change');
 if((m=text.match(/^A pet (.+?)\.?$/i)))add('Pet appearance',m[1]);
 if((m=text.match(/^(?:Used to (?:make|craft)|Used for (?:making|crafting)) (.+?)(?:\.|$)/i)))add('Crafting output',m[1]);
 if((m=text.match(/^Used (?:in|for) (.+?)(?:\.|$)/i)))add('Documented use',m[1]);
 if((m=text.match(/^Required (?:to enter|for access to) (.+?)(?:\.|$)/i)))add('Access requirement',m[1]);
 for(const line of text.split('\n')){if(/^[\w’'/ -]+ Quest(?:[ .(]|$)/.test(line))add('Quest use',line.replace(/\.$/,''));else if(/^[\w’' -]+ Event\.?$/.test(line))add('Event use',line.replace(/\.$/,''));}
 if((m=text.match(/^Summon (.+?) with Necromancy\./i)))add('Necromancy summon',m[1]);
 if((m=text.match(/^Gain entr(?:y|ance) to (.+?)\./i)))add('Access',m[1]);
 if((m=text.match(/^Required to make (.+?)\./i)))add('Crafting output',m[1]);
 if((m=text.match(/^Used to open (.+?)\./i)))add('Opens',m[1]);
 if((m=text.match(/^(.+?) Crafting\./i)))add('Crafting use',m[1]);
 if((m=text.match(/^Give to (.+?) for reward\./i)))add('Hand-in NPC',m[1]);
 if(/NOT BOUND during events/i.test(text))notes.push('The source reports that this bundle was unbound during its event. Current binding behavior is not verified.');
 if(name==='Map of Ant Tunnels'||name==='Map of Ant Guardian Tunnels'){const destinations=text.split('\n').filter(l=>l.trim()&&!/teleport|following|location|destination/i.test(l));if(destinations.length){tables.push({title:'Ant tunnel destinations',headers:['Destination'],rows:destinations.map(l=>[l.replace(/\.$/,'')])});handled=true;}}
 if(name==='Pot of Gold'){const outcomes=[...text.matchAll(/([\d,]+) Exp\s*\+\s*([\d,]+) Gold/gi)];if(outcomes.length){tables.push({title:'Reward pairs reported by this source',headers:['XP','Coins'],rows:outcomes.map(m=>[m[1],m[2]])});add('Additional listed rewards','Leprechaun Friend; Lumen Amulet');notes.push('Reward lists differ between sources. In particular, Wanderlust lists 1,000,000 XP alongside 1,500,000 coins; this is preserved as reported rather than silently corrected.');}}
 const rewardRows=text.split('\n').filter(l=>/^(?:Lvl?\.?|Level|Ab|Ability|All levels)\s*\d*[-+\d ]*:/i.test(l)||/^[\d,]+\s*(?:-\s*[\d,]+)?\s*(Exp|Ap|Gold)\.?$/i.test(l));if(rewardRows.length){tables.push({title:'Reward values reported by the source',headers:['Level range / outcome'],rows:rewardRows.map(l=>[l])});handled=true}
 const contentsHeader=/Contains(?: 1 of the following)?:|Item Option|Loot:|1 Random:|Guaranteed:|Garunteed:/i;
 if(contentsHeader.test(text)||/Bundle$/.test(name)||name==='Kelberoth Costume'||/\(x5\)$/.test(name)){const lines=text.split('\n');const start=lines.findIndex(l=>contentsHeader.test(l));const options=lines.slice(start+1).map(l=>l.trim().replace(/\.$/,'')).filter(l=>l&&!/[.!?*]/.test(l)&&l.length<65&&!/Quest|Event|chance|appears|received|obtained|reverted/i.test(l));if(options.length){tables.push({title:/Guaranteed|Garunteed/i.test(text)?'Guaranteed contents':'Listed contents or possible outcomes',headers:['Item / amount'],rows:options.map(l=>[l])});handled=true}}
 return {facts,notes,tables,handled};
}
