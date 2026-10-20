// Isolated browser fixture. Never controls the user's open client or browser.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const {chromium}=await import('playwright');
const siteRoot=fileURLToPath(new URL('../',import.meta.url));
const output=path.resolve(siteRoot,'../prototype/widescreen/build/skill-list-browser');fs.mkdirSync(output,{recursive:true});
const base='http://127.0.0.1:'+(process.env.PORT||4173);
const browser=await chromium.launch({headless:true,channel:'msedge'});
try{
 const context=await browser.newContext({viewport:{width:1500,height:1050},acceptDownloads:true});
 const page=await context.newPage();const errors=[],external=[];page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(/^https?:/.test(r.url())&&!r.url().startsWith(base+'/'))external.push(r.url());});
 await page.goto(base+'/my-skill-list/',{waitUntil:'networkidle'});
 await page.screenshot({path:path.join(output,'initial.png'),fullPage:true});
 await page.getByLabel('Class',{exact:true}).selectOption('Rogue');await page.getByLabel('Subclass',{exact:true}).selectOption('Archer');await page.getByLabel('Find an ability',{exact:true}).fill('Archery');
 await page.getByRole('checkbox',{name:'Select Archery 1 · Archer',exact:true}).check();await page.getByRole('checkbox',{name:'Select Archery 2 · Archer',exact:true}).check();
 assert.match(await page.locator('.skill-total-table').innerText(),/4,000,000 Gold/);assert.match(await page.locator('.skill-total-table').innerText(),/Wooden Bow\s+5/);
 const downloading=page.waitForEvent('download');await page.getByRole('button',{name:'Export sheet (CSV)',exact:true}).click();const download=await downloading;await download.saveAs(path.join(output,'My-skill-list.csv'));
 const csv=fs.readFileSync(path.join(output,'My-skill-list.csv'),'utf8');assert.match(csv,/Combined requirements/);assert.match(csv,/4000000/);
 await page.reload({waitUntil:'networkidle'});assert.equal(await page.locator('.skill-sheet-ability').count(),2);
 await page.screenshot({path:path.join(output,'desktop.png'),fullPage:true});
 await page.goto(base+'/abilities/stone-skin-archer-spell/',{waitUntil:'networkidle'});assert.match(await page.locator('.learning-place').innerText(),/Aman Jungle Village/);
 await page.getByRole('link',{name:'Add to my skill list',exact:true}).click();await page.waitForURL(url=>url.pathname.replace(/\/$/,'')==='/my-skill-list');await page.locator('.skill-sheet-ability').filter({hasText:'Stone Skin'}).waitFor();assert.equal(await page.locator('.skill-sheet-ability').count(),3);
 const skin=fs.readFileSync(path.resolve(siteRoot,'../prototype/widescreen/theme/assets.css'),'utf8')+fs.readFileSync(path.resolve(siteRoot,'../prototype/widescreen/host-dock.css'),'utf8');await page.addStyleTag({content:skin});await page.setViewportSize({width:480,height:720});await page.screenshot({path:path.join(output,'compact.png'),fullPage:true});
 assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'Compact sheet overflowed horizontally.');
 await page.locator('.skill-sheet-ability').filter({hasText:'Stone Skin'}).getByRole('button',{name:/Remove/}).click();await page.reload({waitUntil:'networkidle'});assert.equal(await page.locator('.skill-sheet-ability').count(),2,'An add query re-added a removed ability.');
 await page.emulateMedia({media:'print'});assert.equal(await page.locator('.skill-picker').isVisible(),false);assert.equal(await page.locator('.skill-sheet').isVisible(),true);await page.emulateMedia({media:'screen'});
 assert.deepEqual(external,[]);assert.deepEqual(errors,[]);await context.close();
 let saved={schema:1,selected:['ability:stone-skin-archer-spell~0'],classFilter:'Rogue',subclass:'Archer',pathFilter:''},exported='';
 const native=await browser.newContext({viewport:{width:480,height:720}});
 await native.exposeFunction('__skillHost',message=>{if(message.type==='da-skill-list-load')return saved;if(message.type==='da-skill-list-save')saved=message.state;if(message.type==='da-skill-list-export')exported=message.csv;});
 await native.addInitScript(()=>{
  window.daSkillListHost=true;const listeners=new Set();window.chrome??={};window.chrome.webview={addEventListener:(_type,fn)=>listeners.add(fn),removeEventListener:(_type,fn)=>listeners.delete(fn),postMessage:async message=>{const state=await window.__skillHost(message);if(message.type==='da-skill-list-load')listeners.forEach(fn=>fn({data:{type:'da-skill-list-state',state}}));}};
 });
 const pane=await native.newPage();await pane.goto(base+'/my-skill-list/',{waitUntil:'networkidle'});await pane.locator('.skill-sheet-ability').waitFor();assert.match(await pane.locator('.skill-sheet-ability').innerText(),/Stone Skin/);
 await pane.getByRole('button',{name:'Export sheet (CSV)',exact:true}).click();assert.match(exported,/Gold Bar/);
 await pane.getByRole('button',{name:'Clear list',exact:true}).click();await pane.waitForTimeout(100);assert.deepEqual(saved.selected,[]);await pane.reload({waitUntil:'networkidle'});assert.equal(await pane.locator('.skill-sheet-ability').count(),0);
 await native.close();fs.writeFileSync(path.join(output,'results.txt'),'PASS: class/subclass filters, multi-rank selection, combined totals, CSV download, local restore, ability learning location, add/remove navigation, compact layout, print layout, native persistence/export bridge and no external requests. Isolated browser only.');
 console.log('Skill-list browser checks passed.');
}finally{await browser.close();}
