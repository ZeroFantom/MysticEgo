// Local, opt-in MCP for existing Electron windows. No arbitrary script or input.
const http=require('node:http');
const crypto=require('node:crypto');
const {dialog,clipboard,Menu}=require('electron');
const wait=ms=>new Promise(r=>setTimeout(r,ms));
const safe=/^[a-z][a-z0-9-]{0,39}$/;
function installInterfaceMcp(win,product,port=0){
 let server,token='',expires=0,busy=false,epoch=0,enabled=false,timeout;
 const stop=()=>{enabled=false;epoch++;token='';clearTimeout(timeout);server?.close();server=undefined;};
 function live(){return enabled&&Date.now()<expires&&!win.isDestroyed()&&win.isVisible()&&!win.isMinimized();}
 const inspectScript=`(()=>Array.from(document.querySelectorAll('[data-interface-id]')).filter(e=>!e.disabled&&!e.closest('[hidden],[inert]')&&e.getClientRects().length).map(e=>({id:e.dataset.interfaceId,label:e.getAttribute('aria-label')||e.textContent.trim().slice(0,80),active:e.classList.contains('active')||e.getAttribute('aria-pressed')==='true'||e.parentElement?.open===true})))()`;
 async function inspect(){if(!live())throw Error('Enable MCP in the visible application window.');return {product,targets:(await win.webContents.executeJavaScript(inspectScript)).filter(t=>safe.test(t.id)).slice(0,40)};}
 function tool(name,description,click=false){return {name,description,inputSchema:{type:'object',properties:click?{target:{type:'string',description:'Exact ID returned by desktop_inspect'}}:{},required:click?['target']:[],additionalProperties:false},annotations:{readOnlyHint:!click,destructiveHint:false}}}
 async function click(target){
  if(!safe.test(target)||!live()||busy)throw Error('Unavailable or busy.');
  busy=true;const generation=epoch;
  try{
   if(!(await inspect()).targets.some(x=>x.id===target))throw Error('Target is not an enabled navigation control.');
   const script=`(()=>{const e=Array.from(document.querySelectorAll('[data-interface-id]')).find(e=>e.dataset.interfaceId===${JSON.stringify(target)});if(!e||e.disabled||e.closest('[hidden],[inert]')||!e.getClientRects().length)return null;e.scrollIntoView({block:'center',behavior:'instant'});const r=e.getBoundingClientRect();const x=Math.round(r.left+r.width/2),y=Math.round(r.top+r.height/2);if(!e.contains(document.elementFromPoint(x,y)))return null;return {x,y}})()`;
   win.focus();const point=await win.webContents.executeJavaScript(script);
   if(!point||!live()||generation!==epoch)throw Error('Target is not visible.');
   const {x,y}=point;
   // Visible indicator is generated from fixed code and bounded numeric coordinates.
   await win.webContents.executeJavaScript(`(()=>{const e=document.createElement('div');e.textContent='➤';e.setAttribute('aria-hidden','true');e.style.cssText='position:fixed;pointer-events:none;z-index:2147483647;color:#a78bfa;font-size:28px;text-shadow:0 0 5px white;transition:left .28s,top .28s;left:50%;top:50%';document.body.append(e);requestAnimationFrame(()=>requestAnimationFrame(()=>{e.style.left=${x}+'px';e.style.top=${y}+'px'}));setTimeout(()=>e.remove(),1100)})()`);
   await wait(350);
   if(!live()||generation!==epoch||!win.isFocused())throw Error('Stopped or focus lost.');
   // Re-resolve the actual element before dispatching native Chromium input.
   const next=await win.webContents.executeJavaScript(script);
   if(!next||next.x!==x||next.y!==y||!live()||generation!==epoch)throw Error('Target moved.');
   win.webContents.sendInputEvent({type:'mouseMove',x,y});
   win.webContents.sendInputEvent({type:'mouseDown',x,y,button:'left',clickCount:1});
   win.webContents.sendInputEvent({type:'mouseUp',x,y,button:'left',clickCount:1});
   await wait(350);
   return {clicked:target,observed:await inspect()};
  }finally{busy=false;}
 }
 function authorized(req){
  const supplied=String(req.headers.authorization||'');
  const expected='Bearer '+token;
  return live()&&supplied.length===expected.length&&crypto.timingSafeEqual(Buffer.from(supplied),Buffer.from(expected));
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
   if(q.jsonrpc!=='2.0'){error(-32600,'Invalid request');return;}
   if(q.method?.startsWith('notifications/')&&q.id===undefined){reply(202);return;}
   if(q.method==='initialize'){result({protocolVersion:'2025-03-26',capabilities:{tools:{}},serverInfo:{name:'mysticego-desktop',version:'1.0.0'}});return;}
   if(q.method==='ping'){result({});return;}
   if(q.method==='tools/list'){result({tools:[tool('desktop_inspect','Inspect only marked navigation controls'),tool('desktop_click','Click an enabled navigation control in the visible window',true)]});return;}
   if(q.method!=='tools/call'){error(-32601,'Method not found');return;}
   try{
    const name=q.params?.name;
    const data=name==='desktop_inspect'?await inspect():name==='desktop_click'&&typeof q.params?.arguments?.target==='string'?await click(q.params.arguments.target):null;
    if(!data){error(-32602,'Unknown tool or invalid target');return;}
    result({content:[{type:'text',text:JSON.stringify(data)}],isError:false});
   }catch(e){result({content:[{type:'text',text:String(e.message).slice(0,160)}],isError:true});}
  }catch{reply(400,{error:'Invalid request'});}
 }
 async function start(){
  if(enabled)return;
  const confirm=await dialog.showMessageBox(win,{type:'question',buttons:['Разрешить на 10 минут','Отмена'],defaultId:1,cancelId:1,message:'Разрешить MCP управлять навигацией '+product+'?',detail:'Только отмеченные элементы интерфейса. Escape отключает управление.'});
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
