// Page pairs were compared as routes and facts, rather than matched by title alone.
export const pairedGuides={
 'hunting/eingrenmanor/quest.php':'quest:eingren-manor','hunting/eingrenmanor/quest_detailed.php':'quest:eingren-manor','hunting/unknowndepths/quest.php':'quest:unknown-depths',
 ...Object.fromEntries(['questchain','relationship','securedcaves','luwellashousekey','protectluwella','secretwaterfall','yowiencostume','returnedstolenbag'].map(p=>['hunting/yowienterritory/'+p+'.php','quest:yowien-territory'])),
 'events/march/luckyclover.php':'event:lucky-clovers','events/march/lockyscharms.php':'event:lockys-charms','events/march/potofgold.php':'event:catch-a-leprechaun','events/march/green_ale.php':'event:green-ale','events/march/helppatrick.php':'event:help-patrick','events/march/helppatrick_quest.php':'event:help-patrick',
 'events/april/floppyFields.php':'event:mileth-floppy-field','events/april/clean_up.php':'event:antonios-cluttered-bank','events/april/redemption.php':'quest:adoration','events/may/adoration.php':'quest:adoration','events/may/capture_the_fowl.php':'event:capture-the-fowls',
 'events/halloween/beggarsoutfit.php':'event:beggars-outfit','events/halloween/petghost.php':'event:pet-ghost','events/halloween/thehorseman.php':'event:headless-horseman','events/halloween/theredmoon.php':'event:red-moon','events/halloween/zombieslayer.php':'event:zombie-slayer','events/halloween/mummyattack.php':'event:mummy-attack','events/halloween/trytoscareme.php':'event:try-to-scare-me','events/halloween/trytoscareme_2011.php':'event:try-to-scare-me','events/halloween/trytoscareme_2012.php':'event:try-to-scare-me',
 'events/mtmerry/info.php':'event:the-merriest-of-dreams','events/mtmerry/saverudy.php':'event:rudolph-escaped','events/mtmerry/penguins.php':'event:penguin-slayer','events/mtmerry/noses.php':'event:red-noses','events/mtmerry/losterbies.php':'event:lost-erbies','events/mtmerry/filthyerbies.php':'event:filthy-erbies','events/mtmerry/frosty.php':'event:frostys-gift','events/mtmerry/yetiarmybattle.php':'event:battle-for-mount-merry',
 ...Object.fromEntries(['info','mileth','oren','hwarone','asilon'].map(p=>['events/insecttournament/'+p+'.php','event:insect-catching-tournament'])),
 'events/lawfragment/info.php':'event:laws-fragment',
};
export const supplementalNotes=[
 ['quest:yowien-territory','hunting/yowienterritory/luwellashousekey.php','Wanderlust recommends leaving the Crystal Caverns by recall after obtaining the Crystal Orb: spike traps can break it. Gather twenty Yowien Fishes in areas 12–14 before returning to Luwella.'],
 ['quest:yowien-territory','hunting/yowienterritory/secretwaterfall.php','The side exits from Yowien Territory 14 lead north to area 27 for Yellow Vines and east to area 26 for Blue Vines. Wanderlust lists one or two vines per tree.'],
 ['event:rudolph-escaped','events/mtmerry/saverudy.php','Rudolph disappears from inventory on disconnect. Return to the workshop promptly, then claim the gift at the Christmas Tree with “Merry Christmas”.'],
 ['event:help-patrick','events/march/helppatrick_quest.php','Wanderlust lists three bosses with dark offense and defense, light attacks, self-healing, facing reversal, and ambush movement. Form the complete group before entry; entering a boss room alone leaves that character fighting alone.'],
 ['event:insect-catching-tournament','events/insecttournament/info.php','Wanderlust explicitly dates the reduction from 500 insects to 200 to June 15, 2012. Ten of each of twenty types gives the full set. The net has 1,000 durability.'],
];
