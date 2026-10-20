import {library} from './library';
import {createReferenceIndex} from './record-references';
let cached:ReturnType<typeof createReferenceIndex>|undefined;
export function referenceIndex(){return cached??=createReferenceIndex(library().entries);}
