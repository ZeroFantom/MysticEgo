// Local, opt-in MCP for existing Electron windows. No arbitrary script or input.
const http=require('node:http');
const crypto=require('node:crypto');
const {dialog,clipboard,Menu}=require('electron');
const wait=ms=>new Promise(r=>setTimeout(r,ms));
const safe=/^[a-z][a-z0-9-]{0,39}$/;
function installInterfaceMcp(win,product,port=0,options={}){
 let server,token='',expires=0,busy=false,epoch=0,enabled=false,timeout;
 const stop=()=>{enabled=false;epoch++;token='';clearTimeout(timeout);server?.close();server=undefined;};
 function live(){return enabled&&Date.now()<expires&&!win.isDestroyed()&&win.isVisible()&&!win.isMinimized();}
 const inspectScript=`(()=>Array.from(document.querySelectorAll('[data-interface-id]')).filter(e=>!e.disabled&&!e.closest('[hidden],[inert]')&&getComputedStyle(e).visibility==='visible'&&e.getClientRects().length).map(e=>({id:e.dataset.interfaceId,label:e.getAttribute('aria-label')||e.textContent.trim().slice(0,80),active:e.classList.contains('active')||e.getAttribute('aria-pressed')==='true'||e.parentElement?.open===true})))()`;
 function requireLease(generation){if(!live()||generation!==epoch)throw Error('MCP session closed.');}
 async function inspect(){const generation=epoch;requireLease(generation);const targets=await win.webContents.executeJavaScript(inspectScript);requireLease(generation);return {product,targets:targets.filter(t=>safe.test(t.id)).slice(0,80)};}
 function tool(name,description,click=false,readOnly=!click){return {name,description,inputSchema:{type:'object',properties:click?{target:{type:'string',description:'Exact ID returned by desktop_inspect'}}:{},required:click?['target']:[],additionalProperties:false},annotations:{readOnlyHint:readOnly,destructiveHint:false}}}
 async function click(target){
  if(!safe.test(target)||!live()||busy)throw Error('Unavailable or busy.');
  busy=true;const generation=epoch;
  try{
   if(!(await inspect()).targets.some(x=>x.id===target))throw Error('Target is not an enabled navigation control.');
   const script=`(()=>{const e=Array.from(document.querySelectorAll('[data-interface-id]')).find(e=>e.dataset.interfaceId===${JSON.stringify(target)}&&!e.disabled&&!e.closest('[hidden],[inert]')&&getComputedStyle(e).visibility==='visible'&&e.getClientRects().length);if(!e)return null;e.scrollIntoView({block:'center',behavior:'instant'});const r=e.getBoundingClientRect();const x=Math.round(r.left+r.width/2),y=Math.round(r.top+r.height/2);if(!e.contains(document.elementFromPoint(x,y)))return null;return {x,y}})()`;
   requireLease(generation);
   win.focus();const point=await win.webContents.executeJavaScript(script);
   if(!point||!live()||generation!==epoch||!win.isFocused()||!Number.isSafeInteger(point.x)||!Number.isSafeInteger(point.y)||point.x<0||point.y<0||point.x>32767||point.y>32767)throw Error('Target is not visible.');
   const {x,y}=point;
   // Visible indicator is generated from fixed code and bounded numeric coordinates.
   await win.webContents.executeJavaScript(`(()=>{const e=document.createElement('div');e.textContent='➤';e.setAttribute('aria-hidden','true');e.style.cssText='position:fixed;pointer-events:none;z-index:2147483647;color:#a78bfa;font-size:28px;text-shadow:0 0 5px white;transition:left .28s,top .28s;left:50%;top:50%';document.body.append(e);requestAnimationFrame(()=>requestAnimationFrame(()=>{e.style.left=${x}+'px';e.style.top=${y}+'px'}));setTimeout(()=>e.remove(),1100)})()`);
   await wait(350);
   if(!live()||generation!==epoch||!win.isFocused())throw Error('Stopped or focus lost.');
   // Re-resolve the actual element before dispatching native Chromium input.
   const next=await win.webContents.executeJavaScript(script);
   if(!next||next.x!==x||next.y!==y||!live()||generation!==epoch||!win.isFocused())throw Error('Target moved or focus lost.');
   win.webContents.sendInputEvent({type:'mouseMove',x,y});
   win.webContents.sendInputEvent({type:'mouseDown',x,y,button:'left',clickCount:1});
   win.webContents.sendInputEvent({type:'mouseUp',x,y,button:'left',clickCount:1});
   await wait(350);
   requireLease(generation);
   const observed=await inspect();requireLease(generation);
   return {clicked:target,observed};
  }finally{busy=false;}
 }
 // These actions operate only on the verified updater owned by this Electron main
 // process. No URLs, commands, passwords, local paths or arbitrary payloads.
 function updateStatus(){
  if(!live())throw Error('Enable MCP in the visible application window.');
  const c=options.updates?.();if(!c)throw Error('Updater unavailable.');
  const {phase,installed,tag,bytes,total,canRestart,automatic}=c.status();
  return {phase,installed,tag,bytes,total,canRestart,automatic};
 }
 async function checkUpdate(){
  if(!live()||busy)throw Error('Unavailable or busy.');
  const c=options.updates?.();if(!c)throw Error('Updater unavailable.');
  const before=updateStatus();
  if(!['idle','error','checking','downloading','ready'].includes(before.phase))throw Error('Current update phase does not allow a check.');
  void c.action('check').catch(()=>{});
  return {requested:true,observed:updateStatus()};
 }
 async function approveUpdate(){
  if(!live()||busy)throw Error('Unavailable or busy.');
  const c=options.updates?.();if(!c)throw Error('Updater unavailable.');
  const before=updateStatus(),generation=epoch;
  if(!before.canRestart||!before.tag||!['ready','restart-ready'].includes(before.phase))throw Error('Verified update is not ready or an operation is running.');
  busy=true;
  try{
   const answer=await dialog.showMessageBox(win,{type:'warning',buttons:['Отмена','Установить / перезапустить'],defaultId:0,cancelId:0,message:'MCP запрашивает обновление '+product,detail:'Версия '+before.tag+'. Приложение завершит работу и запустит новую версию. Профиль сохраняется.'});
   if(answer.response!==1||!live()||generation!==epoch)throw Error('Update not approved.');
   const after=updateStatus();
   if(after.tag!==before.tag||after.phase!==before.phase||!after.canRestart)throw Error('Update state changed. Inspect again.');
   // Return an approval receipt, not a claim that installation or restart succeeded.
   return {receipt:{approved:true,tag:after.tag,phase:after.phase},execute:()=>{if(live()&&generation===epoch&&c.status().tag===after.tag&&c.status().phase===after.phase&&c.status().canRestart)void c.action(after.phase==='ready'?'install':'restart').catch(()=>{});}};
  }finally{busy=false}
 }
 async function sshStatus(){
  if(!live()||product!=='CloudDesk')throw Error('CloudDesk session required.');
  const generation=epoch;
  const state=await win.webContents.executeJavaScript(`(()=>{const modal=document.querySelector('[data-mcp-ssh-stage]');const value=document.querySelector('[data-mcp-ssh-status]');return {stage:modal?.dataset.mcpSshStage??null,state:modal?.dataset.mcpSshState??value?.dataset.mcpSshStatus??null}})()`);
  requireLease(generation);
  return {stage:['identity','access','ready'].includes(state.stage)?state.stage:null,state:['checking','ready','auth','trust','changed','offline','setup','error'].includes(state.state)?state.state:null};
 }
 function authorized(req){
  const supplied=String(req.headers.authorization||'');
  const expected='Bearer '+token;
  const a=Buffer.from(supplied),b=Buffer.from(expected);
  return live()&&a.length===b.length&&crypto.timingSafeEqual(a,b);
 }
 async function rpc(req,res){
  res.setHeader('Cache-Control','no-store');
  const reply=(status,value)=>{if(!res.destroyed){res.writeHead(status,{'Content-Type':'application/json'});res.end(value===undefined?undefined:JSON.stringify(value));}};
  if(req.url!=='/mcp'||req.method!=='POST'){reply(405,{error:'POST /mcp only'});return;}
  // Do not accept browser origins or DNS rebinding host names.
  if(req.headers.origin||!/^127\.0\.0\.1:\d+$/.test(req.headers.host||'')||!authorized(req)){reply(403,{error:'Forbidden'});return;}
  let size=0;const chunks=[];
  try{
   for await(const chunk of req){size+=chunk.length;if(size>16384){reply(413,{error:'Too large'});return;}chunks.push(chunk);}
   const q=JSON.parse(Buffer.concat(chunks).toString('utf8'));
   if(!authorized(req)){reply(403,{error:'Session closed'});return;}
   const result=value=>reply(200,{jsonrpc:'2.0',id:q.id??null,result:value});
   const error=(code,message)=>reply(200,{jsonrpc:'2.0',id:q.id??null,error:{code,message}});
   if(!q||typeof q!=='object'||Array.isArray(q)||q.jsonrpc!=='2.0'||typeof q.method!=='string'){reply(400,{jsonrpc:'2.0',id:null,error:{code:-32600,message:'Invalid request'}});return;}
   if(q.method?.startsWith('notifications/')&&q.id===undefined){reply(202);return;}
   if(q.method==='initialize'){result({protocolVersion:'2025-03-26',capabilities:{tools:{}},serverInfo:{name:'mysticego-desktop',version:'1.0.0'}});return;}
   if(q.method==='ping'){result({});return;}
   if(q.method==='tools/list'){
    const tools=[tool('desktop_inspect','Inspect only marked safe controls'),tool('desktop_click','Click an enabled marked control in the visible window',true)];
    if(options.updates?.())tools.push(tool('desktop_update_status','Read verified updater phase, version and progress'),tool('desktop_update_check','Request a release check and verified download; inspect status afterwards',false,false),tool('desktop_update_apply','Request local user approval to install or restart a verified update; receipt is not completion',false,false));
    if(product==='CloudDesk')tools.push(tool('clouddesk_ssh_status','Read the current SSH connection stage and result without addresses or credentials'));
    result({tools});return;
   }
   if(q.method!=='tools/call'){error(-32601,'Method not found');return;}
   // Side effects require a request/response exchange, not a notification.
   if(typeof q.id!=='string'&&!(typeof q.id==='number'&&Number.isFinite(q.id))){error(-32600,'Tool call requires a request ID');return;}
   try{
    const name=q.params?.name;
    const args=q.params?.arguments??{};
    if(typeof args!=='object'||Array.isArray(args)||Object.keys(args).some(k=>name==='desktop_click'?k!=='target':true)){error(-32602,'Invalid arguments');return;}
    if(name==='desktop_update_apply'){
     const approved=await approveUpdate();
     res.once('finish',approved.execute);
     result({content:[{type:'text',text:JSON.stringify(approved.receipt)}],isError:false});return;
    }
    const data=name==='desktop_inspect'?await inspect():name==='desktop_click'&&typeof args.target==='string'?await click(args.target):
      name==='desktop_update_status'?updateStatus():name==='desktop_update_check'?await checkUpdate():name==='clouddesk_ssh_status'?await sshStatus():null;
    if(!data){error(-32602,'Unknown tool or invalid target');return;}
    result({content:[{type:'text',text:JSON.stringify(data)}],isError:false});
   }catch(e){result({content:[{type:'text',text:String(e.message).slice(0,160)}],isError:true});}
  }catch{reply(400,{error:'Invalid request'});}
 }
 async function start(){
  if(enabled)return;
  const confirm=await dialog.showMessageBox(win,{type:'question',buttons:['Разрешить на 10 минут','Отмена'],defaultId:1,cancelId:1,message:'Разрешить MCP управлять навигацией '+product+'?',detail:'Только отмеченные безопасные элементы, состояние и проверка обновлений. Установка и перезапуск требуют отдельного подтверждения. Escape отключает управление.'});
  if(confirm.response!==0||win.isDestroyed()||enabled)return;
  token=crypto.randomBytes(32).toString('hex');expires=Date.now()+600000;enabled=true;
  server=http.createServer({requestTimeout:5000,headersTimeout:5000},(req,res)=>void rpc(req,res));
  server.maxConnections=8;
  try{await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(port,'127.0.0.1',resolve);});}
  catch{stop();dialog.showErrorBox('MCP','Не удалось открыть локальный MCP.');return;}
  timeout=setTimeout(stop,600000);
  const config=JSON.stringify({mcpServers:{['mysticego-'+product.toLowerCase()]:{url:'http://127.0.0.1:'+server.address().port+'/mcp',headers:{Authorization:'Bearer '+token}}}},null,2);
  const r=await dialog.showMessageBox(win,{type:'info',message:'MCP активен до 10 минут',detail:'Скопируйте конфигурацию в клиент MCP. Не публикуйте временный токен.',buttons:['Копировать конфигурацию','Закрыть'],defaultId:0});
  if(r.response===0&&live())clipboard.writeText(config);
 }
 win.webContents.on('before-input-event',(_e,input)=>{if(input.key==='Escape'&&enabled)stop()});
 win.on('hide',stop);win.on('minimize',stop);win.on('closed',stop);
 // Never expose the bridge to embedded untrusted remote content.
 win.webContents.on('did-start-navigation',(_e,_url,isInPlace,isMainFrame)=>{if(isMainFrame&&!isInPlace)stop()});
 Menu.setApplicationMenu(Menu.buildFromTemplate([{label:'MCP',submenu:[{label:'Разрешить управление интерфейсом',click:()=>void start()},{label:'Остановить управление',click:stop}]}]));
 win.setAutoHideMenuBar(false);win.setMenuBarVisibility(true);
 return {stop};
}
module.exports={installInterfaceMcp};
