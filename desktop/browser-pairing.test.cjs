const {test}=require('node:test');
const assert=require('node:assert/strict');
const Module=require('node:module');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const http=require('node:http');
const {EventEmitter}=require('node:events');
test('desktop pairing checks exact origin, opt-in and native approval; never returns refresh token',async()=>{
 const dir=fs.mkdtempSync(path.join(os.tmpdir(),'mystic-pair-'));
 const app=new EventEmitter();app.getPath=()=>dir;
 let answer=0,grants=0,prompts=0;
 const fake={app,dialog:{showMessageBox:async()=>{prompts++;return {response:answer}}},Menu:{getApplicationMenu:()=>null,buildFromTemplate:items=>({items}),setApplicationMenu:()=>{}}};
 const old=Module._load;let pairing;
 try{Module._load=function(name,...args){return name==='electron'?fake:old.call(this,name,...args)};pairing=require('./browser-pairing.cjs')}finally{Module._load=old}
 const controls=pairing.installBrowserPairing({product:'AuditDesktop',window:()=>({isDestroyed:()=>false}),getSession:()=>({origin:'https://example.test',identity:'actor',refreshToken:'never-return',login:'test'}),approve:async()=>{grants++;return 'a'.repeat(64)}});
 const request=(url,origin,body)=>new Promise((resolve,reject)=>{
  const req=http.request({host:'127.0.0.1',port:19471,path:url,method:body?'POST':'GET',headers:{Origin:origin,...(body?{'Content-Type':'application/json'}:{})},timeout:2000},res=>{let raw='';res.on('data',c=>raw+=c);res.on('end',()=>resolve({status:res.statusCode,headers:res.headers,body:raw}))});
  req.on('error',reject);req.on('timeout',()=>req.destroy());req.end(body?JSON.stringify(body):undefined);
 });
 try{
  await assert.rejects(request('/availability','https://example.test'));
  controls.set(true);
  let good;for(let i=0;i<10;i++){try{good=await request('/availability','https://example.test');break}catch{await new Promise(r=>setTimeout(r,50))}}
  assert.equal(good?.status,200);assert.deepEqual(JSON.parse(good.body),{available:true,product:'AuditDesktop'});
  assert.equal(good.headers['access-control-allow-origin'],'https://example.test');
  const wrong=await request('/availability','https://other.example');assert.equal(wrong.status,404);assert.equal(wrong.headers['access-control-allow-origin'],undefined);
  assert.equal((await request('/approve','https://example.test',{challenge:'bad'})).status,400);
  assert.equal((await request('/approve','https://example.test',{challenge:'b'.repeat(64)})).status,403);
  assert.equal(grants,0);assert.equal(prompts,1);
  // A second immediate request must not show another confirmation.
  answer=1;assert.equal((await request('/approve','https://example.test',{challenge:'b'.repeat(64)})).status,409);
  assert.equal(prompts,1);
  controls.set(false);
 }finally{controls.stop();fs.rmSync(dir,{recursive:true,force:true})}
});
