const {test}=require('node:test');
const assert=require('node:assert/strict');
const {EventEmitter}=require('node:events');
const Module=require('node:module');
const fs=require('node:fs');
const path=require('node:path');
test('local MCP tools require lease and native update approval; no caller-controlled installer',async()=>{
 const callbacks=[];let config,approve=false,phase='ready',actions=[],focused=true,mode='ssh',resolutions=0;const input=[];
 const electron={dialog:{showMessageBox:async(_win,opts)=>opts.message.startsWith('MCP запрашивает')?{response:approve?1:0}:{response:0},showErrorBox:()=>{}},clipboard:{writeText:t=>{config=JSON.parse(t)}},Menu:{buildFromTemplate:t=>t,setApplicationMenu:t=>callbacks.push(t[0].submenu[0].click)}};
 const load=Module._load;Module._load=function(id,...args){return id==='electron'?electron:load.call(this,id,...args)};
 let bridge;try{bridge=require('./interface-mcp.cjs')}finally{Module._load=load}
 const win=new EventEmitter();Object.assign(win,{isDestroyed:()=>false,isVisible:()=>true,isMinimized:()=>false,isFocused:()=>focused,focus:()=>{focused=true},setAutoHideMenuBar(){},setMenuBarVisibility(){}});
 win.webContents=new EventEmitter();win.webContents.executeJavaScript=async(script)=>{
  if(mode==='revoked-inspection'){handle.stop();return [{id:'safe-nav',label:'private marker'}];}
  if(mode==='focus-race'){
   if(script.includes('document.querySelectorAll')&&script.includes('.map(e=>'))return [{id:'safe-nav',label:'Safe navigation'}];
   if(script.includes('document.elementFromPoint')){if(++resolutions===2)focused=false;return {x:10,y:10};}
   return null;
  }
  return {stage:'access',state:'checking'};
 };win.webContents.sendInputEvent=event=>input.push(event);
 const updater={status:()=>({phase,installed:'old',tag:'new',bytes:1,total:1,canRestart:true,automatic:false}),action:async action=>{actions.push(action);phase=action==='install'?'installing':phase}};
 const handle=bridge.installInterfaceMcp(win,'CloudDesk',0,{updates:()=>updater});
 try{
  callbacks[0]();for(let i=0;i<100&&!config;i++)await new Promise(r=>setTimeout(r,10));assert.ok(config);
  const {url,headers}=config.mcpServers['mysticego-clouddesk'];
  const rpc=async(method,params,auth=headers)=>{const r=await fetch(url,{method:'POST',headers:{...auth,'Content-Type':'application/json'},body:JSON.stringify({jsonrpc:'2.0',id:1,method,params})});return {status:r.status,json:await r.json()}};
  const call=async(name,args={})=>(await rpc('tools/call',{name,arguments:args})).json;
  assert.equal((await rpc('tools/list',{},{})).status,403);
  const list=(await rpc('tools/list',{})).json.result.tools;
  assert.ok(list.some(t=>t.name==='desktop_update_apply'&&!t.annotations.readOnlyHint));
  assert.ok(list.some(t=>t.name==='clouddesk_ssh_status'));
  assert.equal(JSON.parse((await call('clouddesk_ssh_status')).result.content[0].text).stage,'access');
  assert.equal(JSON.parse((await call('desktop_update_status')).result.content[0].text).phase,'ready');
  assert.equal((await call('desktop_update_apply',{url:'https://example.invalid'})).error.code,-32602);
  assert.equal((await call('desktop_update_apply')).result.isError,true);assert.deepEqual(actions,[]);
  const notification=await fetch(url,{method:'POST',headers:{...headers,'Content-Type':'application/json'},body:JSON.stringify({jsonrpc:'2.0',method:'tools/call',params:{name:'desktop_update_apply',arguments:{}}})});
  assert.equal((await notification.json()).error.code,-32600);assert.deepEqual(actions,[]);
  mode='focus-race';const click=await call('desktop_click',{target:'safe-nav'});assert.equal(click.result.isError,true);assert.equal(resolutions,2);assert.deepEqual(input,[]);mode='ssh';
  approve=true;const receipt=await call('desktop_update_apply');assert.equal(JSON.parse(receipt.result.content[0].text).approved,true);
  assert.deepEqual(actions,['install']);
  assert.equal((await call('desktop_update_apply')).result.isError,true);assert.deepEqual(actions,['install']);
  assert.equal(fs.readFileSync(path.join(__dirname,'interface-mcp.cjs'),'utf8'),fs.readFileSync(path.join(__dirname,'../tools/MysticEgo.CloudDesk/desktop/interface-mcp.cjs'),'utf8'));
  mode='revoked-inspection';const revoked=await call('desktop_inspect');assert.equal(revoked.result.isError,true);assert.ok(!JSON.stringify(revoked).includes('private marker'));
 }finally{handle.stop()}
});
