// Isolated browser test: no player window or browser profile is controlled.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const folder=path.dirname(fileURLToPath(import.meta.url));
const {chromium}=await import('playwright');
const browser=await chromium.launch({headless:true,channel:'msedge'});
try{
 const page=await browser.newPage({viewport:{width:1800,height:1000}});
 const skin=fs.readFileSync(path.join(folder,'../../prototype/widescreen/theme/assets.css'),'utf8')+fs.readFileSync(path.join(folder,'../../prototype/widescreen/host-dock.css'),'utf8');
 await page.addInitScript(css=>document.addEventListener('DOMContentLoaded',()=>{const style=document.createElement('style');style.textContent=css;document.head.appendChild(style)}),skin);
 await page.goto('http://127.0.0.1:'+(process.env.PORT||4173)+'/classes/rogue/#archer',{waitUntil:'networkidle'});
 const table=page.locator('#archer .class-record-list').first();
 const archery=table.locator('tr').filter({has:page.getByRole('link',{name:'Archery',exact:true})});
 await archery.waitFor();
 const initial=await archery.innerText();assert.match(initial,/Archery Book/);assert.match(initial,/Gold:\s*1,000,000/);assert.match(initial,/Rank 1/);
 const arrow=table.locator('tr').filter({has:page.getByRole('link',{name:'Arrow Shot',exact:true})});
 assert.match(await arrow.innerText(),/EXP:\s*10,000,000/);assert.match(await arrow.innerText(),/Pantica/);
 await page.locator('#archer').evaluate(node=>node.scrollIntoView({block:'start'}));
 await page.screenshot({path:path.join(folder,'../../prototype/widescreen/build/class-learning-requirements.png')});
 await archery.locator('.learning-ranks summary').click();
 const third=archery.locator('.learning-rank').filter({has:page.getByRole('heading',{name:/^Archery 3 /})});
 assert.match(await third.innerText(),/Ability 15/);assert.match(await third.innerText(),/EXP:\s*25,000,000/);assert.match(await third.innerText(),/Ability EXP:\s*1,000/);assert.match(await third.innerText(),/Royal Bow/);assert.match(await third.innerText(),/Archery 2/);
 await third.scrollIntoViewIfNeeded();
 await page.screenshot({path:path.join(folder,'../../prototype/widescreen/build/class-learning-ranks.png')});
 await page.setViewportSize({width:448,height:400});
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,'The compact class page must not overflow horizontally');
 console.log(JSON.stringify({status:'passed',firstRankCosts:true,laterRankCosts:true,compactRootOverflow:false}));
}finally{await browser.close();}
