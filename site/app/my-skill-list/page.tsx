import {MySkillList} from '@/components/my-skill-list';
import {library} from '@/lib/library';
import {buildSkillChoices} from '@/lib/skill-list';
export const metadata={title:'My skill list',description:'Choose skills and spells, list their learning materials and total your requirements.'};
export default function MySkillListPage(){const data=library();return <main id="main" className="page-container skill-list-page"><header><h1>My skill list</h1><p>Plan what to learn, what to bring, and where to go.</p><p className="muted no-print">Your list stays on this device. Export a sheet to open it in Excel, or print your requirements.</p></header><MySkillList choices={buildSkillChoices(data.entries,data.relations)}/></main>}
