import fs from 'node:fs';
const source='dist/client/'+(process.argv[2]||'progression/mastery.html'),target='dist/client/__text-size-check.html';
let html=fs.readFileSync(source,'utf8').replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi,'');
html=html.replace('</head>','<style id="text-size-check">html{font-size:200%!important}</style></head>');
fs.writeFileSync(target,html);console.log('Static 200% root-text fixture: http://127.0.0.1:4173/__text-size-check.html');
