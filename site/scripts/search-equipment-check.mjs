// Exercise the exported Pagefind index through the actual search controls.
// This opens an isolated headless browser, never the player's browser profile.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const folder=path.dirname(fileURLToPath(import.meta.url));
const {chromium}=await import('playwright');

(async()=>{
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{
  const page=await browser.newPage({viewport:{width:448,height:230}});
  const skin=fs.readFileSync(path.join(folder,'../../prototype/widescreen/theme/assets.css'),'utf8')+fs.readFileSync(path.join(folder,'../../prototype/widescreen/host-dock.css'),'utf8');
  await page.addInitScript(css=>document.addEventListener('DOMContentLoaded',()=>{const style=document.createElement('style');style.textContent=css;document.head.appendChild(style)}),skin);
  const base='http://127.0.0.1:'+(process.env.PORT||4183);
  await page.goto(base+'/search/',{waitUntil:'networkidle'});
  const within=page.getByLabel('Search within');
  await within.selectOption('equipment');
  const status=page.locator('.search-options [role="status"]');
  await status.waitFor();
  await page.waitForFunction(()=>/\d+ results?/.test(document.querySelector('.search-options [role="status"]')?.textContent||''));
  const all=Number((await status.textContent()).replace(/[^\d]/g,''));
  assert.ok(all>1000,'Equipment must search a substantial local item catalog');
  assert.match(await page.locator('.search-results a').first().getAttribute('href'),/^\/items\//);
  assert.equal(await page.locator('.search-result-copy span').first().textContent(),'Equipment');
  const classes=page.getByLabel('Equipment class');
  assert.ok(await classes.isVisible(),'Equipment shows a second dropdown');
  const options=await classes.locator('option').allTextContents();
  const warrior=options.indexOf('Warrior'),gladiator=options.findIndex(o=>o.trim()==='Gladiator');
  assert.ok(warrior>=0&&gladiator===warrior+1,'Gladiator stays beneath Warrior');
  await classes.selectOption('Warrior');
  await page.waitForURL('**/search/?*class=Warrior*');
  await page.waitForFunction(()=>/\d+ results?/.test(document.querySelector('.search-options [role="status"]')?.textContent||''));
  const parent=Number((await status.textContent()).replace(/[^\d]/g,''));
  assert.ok(parent>0&&parent<all,'Warrior filters equipment');
  await classes.selectOption('Gladiator');
  await page.waitForURL('**/search/?*class=Gladiator*');
  await page.waitForFunction(()=>/\d+ results?/.test(document.querySelector('.search-options [role="status"]')?.textContent||''));
  const pathCount=Number((await status.textContent()).replace(/[^\d]/g,''));
  assert.ok(pathCount>0&&pathCount<parent,'Subclass narrows its parent class');
  assert.match(await page.locator('.search-results a').first().getAttribute('href'),/^\/items\//);
  assert.equal(await classes.inputValue(),'Gladiator');
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,'Equipment class dropdown must fit the companion width');
  await page.screenshot({path:path.join(folder,'../../prototype/widescreen/build/equipment-search-check.png')});
  await page.reload({waitUntil:'networkidle'});
  assert.equal(await page.getByLabel('Equipment class').inputValue(),'Gladiator','URL restores the chosen class');
  assert.equal(await within.inputValue(),'equipment','URL restores Equipment');
  await within.selectOption('abilities');
  assert.ok(await page.getByLabel('Ability class').isVisible(),'Ability class filtering still works');
  assert.equal(await page.getByLabel('Equipment class').count(),0);
  console.log(JSON.stringify({status:'passed',equipment:all,warrior:parent,gladiator:pathCount}));
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
