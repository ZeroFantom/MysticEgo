import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {chromium} from 'playwright';
const root=new URL('../../desktop/control/',import.meta.url);
const source=await readFile(new URL('desktop-update-view.js',root));
const css=Buffer.concat([await readFile(new URL('style.css',root)),await readFile(new URL('desktop-update.css',root))]);
const notes='- Длинное описание новой версии. '.repeat(120);
const tag='v1.0.0-beta.7-r3.2.42';
const html=`<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/style.css"></head><body><main><div id="updates"></div><div class="embedded-toolbar"><button id="toolbar" hidden>Обновление</button></div></main><script type="module">
import {mountDesktopUpdate,mountToolbarUpdate} from '/view.js';
window.calls=[];window.state={phase:'ready',tag:${JSON.stringify(tag)},notes:${JSON.stringify(notes)},bytes:100,total:100,canRestart:false};
const api={status:async()=>({...window.state}),action:async action=>{window.calls.push(action);window.state={...window.state,phase:action==='install'?'restart-ready':action==='restart'?'restarting':'checking'};return {...window.state}}};
mountDesktopUpdate(document.getElementById('updates'),api);
mountToolbarUpdate(document.getElementById('toolbar'),api);
</script></body></html>`;
const server=createServer((req,res)=>{
 if(req.url==='/view.js')return res.writeHead(200,{'Content-Type':'text/javascript'}).end(source);
 if(req.url==='/style.css')return res.writeHead(200,{'Content-Type':'text/css'}).end(css);
 if(req.url==='/')return res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'}).end(html);
 res.writeHead(404).end();
});
await new Promise((ok,fail)=>{server.once('error',fail);server.listen(0,'127.0.0.1',ok)});
const browser=await chromium.launch({headless:true});
try{
 for(const width of [390,680,1090]){
  const page=await browser.newPage({viewport:{width,height:720}});
  try{
   const failures=[];page.on('pageerror',e=>failures.push(e.message));
   await page.goto('http://127.0.0.1:'+server.address().port);
   const card=page.locator('.desktop-update-card');
   const install=card.getByRole('button',{name:'Установить и перезапустить'});
   await install.waitFor({state:'visible'});
   assert(await install.isDisabled());
   assert((await card.boundingBox()).height<165,'Collapsed update should be compact at '+width);
   assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
   assert.equal(await card.locator('details').getAttribute('open'),null);
   await card.locator('summary').click();
   assert(await card.locator('details').evaluate(el=>el.open));
   assert((await card.locator('pre').boundingBox()).height<190);
   await card.locator('summary').click();
   await page.evaluate(()=>window.state.canRestart=true);
   await page.waitForFunction(()=>!document.querySelector('.desktop-update-action').disabled);
   await install.click();
   const restart=card.getByRole('button',{name:'Перезапустить и открыть'});
   await restart.waitFor({state:'visible'});
   assert.deepEqual(await page.evaluate(()=>window.calls),['install']);
   await page.locator('#toolbar').getByText('Перезапустить',{exact:true}).click();
   await card.getByText('Перезапуск приложения',{exact:true}).waitFor();
   assert.deepEqual(await page.evaluate(()=>window.calls),['install','restart']);
   assert.deepEqual(failures,[]);
   console.log('PASS compact update controls and embedded toolbar at '+width+'px');
  }finally{await page.close()}
 }
}finally{await browser.close();await new Promise(ok=>server.close(ok))}
