import {spawnSync} from 'node:child_process';
const run=(file,args=[])=>{const r=spawnSync(process.execPath,[file,...args],{stdio:'inherit',env:{...process.env,NODE_OPTIONS:'--max-old-space-size=8192',NO_COLOR:'1'}});if(r.status!==0)process.exit(r.status||1)};
run('scripts/content-validate.mjs');run('scripts/publish-reports.mjs');run('scripts/search-index.mjs');run('node_modules/vinext/dist/cli.js',['build','--prerender-concurrency',process.env.DA_LIBRARY_BUILD_CONCURRENCY||'2']);run('scripts/compress-local.mjs');
