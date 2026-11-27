const {test}=require('node:test');
const assert=require('node:assert/strict');
const os=require('node:os');
const path=require('node:path');
const fs=require('node:fs/promises');
const crypto=require('node:crypto');
const {select,compare,stage,verified,downloadResponse,installation,confirmUpdate,createReleaseHighlights}=require('./desktop-updates.cjs');
const current='v1.0.0-beta.7-r3.2.39';
const tag='v1.0.0-beta.7-r3.2.40';
const data=Buffer.from('verified installer');
const digest=crypto.createHash('sha256').update(data).digest('hex');
const asset=(product,os)=>({name:'MysticEgo-'+product+'-'+os,state:'uploaded',size:data.length,digest:'sha256:'+digest});
const release=(tag,assets,draft=false)=>({tag_name:tag,published_at:'2026-11-27T00:00:00Z',assets,draft});
test('anonymous exact-product version and digest selection',()=>{
 for(const product of ['CloudDesk','AgentDesktop','AuditDesktop','LocalDesktop']){
  for(const [platform,suffix] of [['linux','linux-x64.run'],['win32','win-x64.exe']]){
   const releases=[release('v1.0.0-beta.8-r3.2.41',[asset(product,suffix)]),release('v1.0.0-beta.7-r3.2.42',[asset(product,suffix)],true),release(tag,[asset(product,suffix)]),release(current,[asset(product,suffix)])];
   assert.deepEqual(select(releases,product,platform,current),{tag,filename:asset(product,suffix).name,size:data.length,digest,notes:''});
  }
 }
 assert.equal(compare('v1.0.0-beta.7-r3.2.100',tag),1);
 assert.equal(select([release(tag,[{...asset('CloudDesk','linux-x64.run'),digest:null}])],'CloudDesk','linux',current),null);
 assert.equal(installation(path.join('apps','CloudDesk',current),'CloudDesk','linux').installed,current);
 assert.equal(installation('development','CloudDesk','linux'),null);
});
test('stages only verified bytes and uses cached artifact without login',async()=>{
 const folder=await fs.mkdtemp(path.join(os.tmpdir(),'desktop-updater-'));
 try{
  const candidate=select([release(tag,[asset('CloudDesk','linux-x64.run')])],'CloudDesk','linux',current);
  const file=await stage(candidate,folder,async(url,options)=>{
   assert.equal(url.hostname,'github.com');assert.equal(options.headers.Authorization,undefined);
   return new Response(data);
  });
  assert.equal(await verified(file,candidate),true);
  assert.equal(await stage(candidate,folder,()=>{throw Error('cache not used')}),file);
  await fs.writeFile(file,'wrong');
  await assert.rejects(stage(candidate,folder,async()=>new Response('bad')),/checksum/);
  assert.equal(await verified(file,candidate),false);
  assert.equal((await fs.readdir(folder)).some(f=>f.endsWith('.part')),false);
  await assert.rejects(stage(candidate,folder,async()=>new Response(Buffer.alloc(data.length+1))),/declared size/);
 }finally{await fs.rm(folder,{recursive:true,force:true})}
});
test('rejects redirects outside trusted release hosts',async()=>{
 await assert.rejects(downloadResponse('https://github.com/file',async()=>new Response(null,{status:302,headers:{location:'https://evil.example/file'}})),/Untrusted/);
 const r=await downloadResponse('https://github.com/file',async url=>url.hostname==='github.com'?new Response(null,{status:302,headers:{location:'https://release-assets.githubusercontent.com/file'}}):new Response(data));
 assert.equal(await r.text(),data.toString());
});

test('Agent and Local never prompt; Audit and CloudDesk display the exact changes before confirmation',async()=>{
 const candidate={tag,notes:'- Новый интерфейс\n- Исправления'};
 let calls=0;
 const dialog={showMessageBox:async(_win,options)=>{calls++;assert.ok(options.detail.includes(candidate.notes));return {response:1}}};
 for(const product of ['AgentDesktop','LocalDesktop'])assert.equal(await confirmUpdate({product,dialog,candidate}),true);
 assert.equal(calls,0);
 for(const product of ['AuditDesktop','CloudDesk'])assert.equal(await confirmUpdate({product,dialog,candidate}),false);
 assert.equal(calls,2);
 dialog.showMessageBox=async()=>({response:0});
 assert.equal(await confirmUpdate({product:'CloudDesk',dialog,candidate}),true);
});
test('highlights are version-bound, available offline and dismissed persistently',async()=>{
 const folder=await fs.mkdtemp(path.join(os.tmpdir(),'desktop-highlights-'));
 try{
  const root=path.join(folder,'AgentDesktop',tag),userData=path.join(folder,'profile');
  await fs.mkdir(path.join(root,'desktop'),{recursive:true});
  await fs.mkdir(userData);
  await fs.writeFile(path.join(root,'desktop','release-highlights.json'),JSON.stringify({tag,notes:'Bundled notes'}));
  await fs.writeFile(path.join(userData,'release-highlights.json'),JSON.stringify({tag:current,notes:'Old version'}));
  const app={getPath:()=>userData};
  const state=createReleaseHighlights({app,root,product:'AgentDesktop'});
  await state.load();assert.deepEqual(state.get(),{tag,notes:'Bundled notes'});
  assert.equal(await state.dismiss(current),false);
  assert.equal(await state.dismiss(tag),true);
  const restarted=createReleaseHighlights({app,root,product:'AgentDesktop'});
  await restarted.load();assert.equal(restarted.get(),null);
 }finally{await fs.rm(folder,{recursive:true,force:true})}
});
