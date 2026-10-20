// An isolated browser fixture; never touches the user's client or browser.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const {chromium}=await import('playwright');
const root=fileURLToPath(new URL('../',import.meta.url));
const output=path.resolve(root,'../prototype/widescreen/build/item-acquisition-browser');fs.mkdirSync(output,{recursive:true});
const base='http://127.0.0.1:'+(process.env.PORT||4173);
const browser=await chromium.launch({headless:true,channel:'msedge'});
try{
 const context=await browser.newContext({viewport:{width:1200,height:900}});
 const page=await context.newPage(),errors=[],external=[];
 page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(/^https?:/.test(r.url())&&!r.url().startsWith(base+'/'))external.push(r.url());});
 await page.goto(base+'/items/wind-stone/',{waitUntil:'networkidle'});
 const section=page.locator('#where-to-find');await section.waitFor();
 assert.match(await section.innerText(),/Area drop\s+Andor/);assert.ok(!(await section.innerText()).includes('Desert Dunes'));
 const floors=(await section.locator('tbody tr td').nth(2).innerText()).replace(/^Floors:\s*/,'').split(',').map(s=>s.trim());
 assert.equal(floors.length,50);assert.equal(floors[0],'8');assert.equal(floors.at(-1),'113');
 const andor=section.getByRole('link',{name:'Andor',exact:true});assert.equal(await andor.getAttribute('href'),'/world/andor-bestiary/');
 await page.screenshot({path:path.join(output,'desktop.png'),fullPage:true});
 const skin=fs.readFileSync(path.resolve(root,'../prototype/widescreen/theme/assets.css'),'utf8')+fs.readFileSync(path.resolve(root,'../prototype/widescreen/host-dock.css'),'utf8');
 await page.addStyleTag({content:skin});await page.setViewportSize({width:480,height:720});
 await page.screenshot({path:path.join(output,'compact.png'),fullPage:true});
 assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Compact item page overflowed horizontally.');
 await andor.click();await page.waitForURL(url=>url.pathname.replace(/\/$/,'')==='/world/andor-bestiary');assert.match(await page.locator('h1').innerText(),/Andor/);
 await page.goto(base+'/items/cordovan-boots/',{waitUntil:'networkidle'});
 const boots=page.locator('#where-to-find');assert.match(await boots.locator('thead').first().innerText(),/Version/);
 assert.equal(await boots.locator('tbody tr:visible').count(),12);await boots.getByText(/Show \d+ more sources/).click();
 assert.ok(await boots.locator('tbody tr:visible').count()>12);assert.match(await boots.innerText(),/Blessed Cordovan Boots/);
 await page.addStyleTag({content:skin});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Compact equipment page overflowed horizontally.');
 await boots.screenshot({path:path.join(output,'variants.png')});
 assert.equal(await page.locator('.article-section h2').filter({hasText:/^Documented acquisition$/}).count(),0,'Raw acquisition table was duplicated below the new section.');
 await page.goto(base+'/items/gold-bar/',{waitUntil:'networkidle'});assert.match(await page.locator('#where-to-find').innerText(),/Buy from Bankers for 50m gold \+ 500k fee/);
 await page.goto(base+'/items/15-percent-exp-ap-bonus/',{waitUntil:'networkidle'});assert.match(await page.locator('#where-to-find').innerText(),/No acquisition locations are recorded/);
 assert.deepEqual(errors,[]);assert.deepEqual(external,[]);
 fs.writeFileSync(path.join(output,'results.txt'),'PASS: Wind Stone has all 50 unique Andor floors and a working offline area link; quest inputs excluded; compact item/equipment layouts; expandable variant sources; no duplicated acquisition table; vendor instructions; explicit missing coverage; no browser errors or external requests.\n');
 console.log('Item-acquisition browser checks passed.');await context.close();
}finally{await browser.close();}
