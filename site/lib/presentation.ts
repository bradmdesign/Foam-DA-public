import type {Entry} from './models';

// Keep provenance in the research dataset; do not turn it into outbound UI links.
export function displayText(value:string):string {
 // An NPC-list cell and the training quest refer to the actual game character.
 if(value.trim()==='Vorlof'||value.includes('trainer named Vorlof'))return value;
 return value
  .replace('The cached index confirms this item’s name. Its detailed Vorlof page was not retrieved before the host began denying access; missing properties remain unknown.','This item is listed in the index. Detailed properties are not available yet.')
  .replace(/Vorlof is the preferred value until independently verified\./g,'the displayed reward is supported by two references.')
  .replace(/Vorlof’s ability tables take precedence where they provide values\./g,'Use the ability records for documented requirements and current discrepancies.')
  .replace(/Vorlof’s quest route remains the primary guide\./g,'The route difference remains unconfirmed.')
  .replace(/These are Vorlof’s preferred values\./g,'Reward values remain disputed.')
  .replace(/, separate from Vorlof’s excluded new-player guide/g,'')
  .replace(/The full narrative remains at the linked source\./g,'This entry is a synopsis rather than the full narrative.')
  .replace(/Read each work in the credited original collection\./g,'Only the index is available here.')
  .replace(/Wanderlust learning requirements/g,'Additional learning requirements')
  .replace(/\bVorlof’s\b/g,'the reference’s').replace(/\bWanderlust’s\b/g,'another reference’s')
  .replace(/\bVorlof\b/g,'the reference').replace(/\bWanderlust\b/g,'another reference')
  .replace(/(^|[.!?]\s+)(the|another) reference/g,(_,p,word)=>p+word[0].toUpperCase()+word.slice(1)+' reference');
}
export function presentEntry<T extends Entry>(entry:T):T {
 const e=structuredClone(entry);
 const cleanFact=(f:{label:string;value:string|null;alternatives:{value:string}[]})=>{f.label=displayText(f.label);if(f.value)f.value=displayText(f.value);for(const a of f.alternatives)a.value=displayText(a.value);};
 // Vorlof is also an actual NPC. Preserve the character's name, not site branding.
 if(!(e.kind==='npc'&&e.title==='Vorlof'))e.summary=displayText(e.summary);
 e.notes=e.notes.map(displayText);e.steps=e.steps.map(s=>s.includes('trainer named Vorlof')?s:displayText(s));
 e.facts.forEach(cleanFact);
 for(const t of e.tables){t.title=displayText(t.title);t.headers=t.headers.map(displayText);t.rows=t.rows.map(r=>r.map(displayText));}
 if(e.kind==='ability')for(const r of e.ranks){r.summary=displayText(r.summary);r.facts.forEach(cleanFact);}
 if(e.kind==='item')for(const v of e.variants)v.facts.forEach(cleanFact);
 return e;
}
