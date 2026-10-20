import fs from 'node:fs';
import crypto from 'node:crypto';
import {spawnSync} from 'node:child_process';
const read=p=>fs.existsSync(p)?JSON.parse(fs.readFileSync(p,'utf8')):null;
const before=read('content/generated/library.json'),sourcesBefore=read('research/coverage.json')||{};
const run=(script,args=[])=>{const r=spawnSync(process.execPath,[script,...args],{stdio:'inherit'});if(r.status!==0)throw new Error(script+' exited '+r.status)};
run('scripts/content-crawl.mjs',process.argv.slice(2));run('scripts/research-extract.mjs');run('scripts/content-normalize.mjs');
const after=read('content/generated/library.json'),sourcesAfter=read('research/coverage.json');
const snapshot=e=>JSON.parse(JSON.stringify(e,(key,value)=>key==='retrievedAt'?undefined:value));
const digest=e=>crypto.createHash('sha256').update(JSON.stringify(snapshot(e))).digest('hex');
const previous=new Map((before?.entries||[]).map(e=>[e.id,e])),current=new Map(after.entries.map(e=>[e.id,e]));
const changed=[];for(const e of after.entries){const old=previous.get(e.id);if(old&&digest(old)!==digest(e)){changed.push({id:e.id,title:e.title,before:snapshot(old),after:snapshot(e),guideChanged:JSON.stringify([old.summary,old.steps,old.notes])!==JSON.stringify([e.summary,e.steps,e.notes])})}}
const report={createdAt:new Date().toISOString(),added:after.entries.filter(e=>!previous.has(e.id)).map(e=>({id:e.id,title:e.title})),removed:(before?.entries||[]).filter(e=>!current.has(e.id)).map(e=>({id:e.id,title:e.title})),changed,sourceChanges:Object.values(sourcesAfter).filter(e=>e.sha256!==sourcesBefore[e.url]?.sha256||e.status!==sourcesBefore[e.url]?.status).map(e=>({url:e.url,before:sourcesBefore[e.url]?.status||'undiscovered',after:e.status,contentChanged:!!e.sha256&&e.sha256!==sourcesBefore[e.url]?.sha256})),reviewIssues:read('research/review-issues.json'),coverage:after.coverage};
fs.mkdirSync('research/changes',{recursive:true});const file='research/changes/'+report.createdAt.replace(/[:.]/g,'-')+'.json';fs.writeFileSync(file,JSON.stringify(report,null,2));console.log('Review changes: '+file);
// Preserve the review report even when a newly discovered record fails validation.
run('scripts/summarize-research.mjs');run('scripts/content-validate.mjs');
