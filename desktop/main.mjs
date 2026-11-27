import {app,BrowserWindow,ipcMain,screen,shell} from 'electron';
import {execFile} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
const home=path.dirname(fileURLToPath(import.meta.url));
let window,walkTimer;
let handoff = null, handoffShown = false;
const handoffInput = process.env.MYSTICEGO_AVATAR_HANDOFF === '1' ? new Promise(resolve=>{
  let input='';
  const finish=value=>{clearTimeout(timer);process.stdin.removeListener('data',data);process.stdin.removeListener('end',end);resolve(value)};
  const data=chunk=>{input+=chunk.toString();if(input.length>12000)finish(null)};
  const end=()=>{try{
    const value=JSON.parse(input);
    if(typeof value.token!=='string'||!value.token||value.token.length>8192||!['walk','perch'].includes(value.motion)||!Number.isFinite(value.horizontal)||value.horizontal<0||value.horizontal>1)throw Error();
    finish(value);
  }catch{finish(null)}};
  const timer=setTimeout(()=>finish(null),4000);
  process.stdin.on('data',data);process.stdin.once('end',end);process.stdin.resume();
}) : Promise.resolve(null);
const allowedHosts=['127.0.0.1','localhost'];
const configuredApi = process.env.MYSTICEGO_DESKTOP_API_URL || '';
function isLocal(value){try{const u=new URL(value);return ['http:','https:'].includes(u.protocol)&&allowedHosts.includes(u.hostname)}catch{return false}}
function create(){
 window=new BrowserWindow({width:340,height:440,transparent:true,frame:false,alwaysOnTop:true,skipTaskbar:true,resizable:true,show:false,
  webPreferences:{preload:path.join(home,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true,webSecurity:true}});
 window.setVisibleOnAllWorkspaces(true,{visibleOnFullScreen:false});
 window.webContents.setWindowOpenHandler(({url})=>{if(isLocal(url))shell.openExternal(url);return {action:'deny'}});
 window.webContents.on('will-navigate',(event,url)=>{if(!url.startsWith('file:')&&!isLocal(url))event.preventDefault()});
 const url=process.env.MYSTICEGO_DESKTOP_DEV_URL;
 if(url&&isLocal(url))window.loadURL(url);else window.loadFile(path.join(home,'dist','index.html'));
 window.once('ready-to-show',()=>{if(!handoffShown){dockTaskbar();if(process.env.MYSTICEGO_AVATAR_HANDOFF!=='1')window.show()}});
}
function stopWalk(){if(walkTimer){clearInterval(walkTimer);walkTimer=null}}
function dockTaskbar(){stopWalk();const display=screen.getDisplayMatching(window.getBounds());const w=window.getBounds().width,h=window.getBounds().height;window.setPosition(display.workArea.x+display.workArea.width-w-10,display.workArea.y+display.workArea.height-h)}
const geoBinary=process.env.MYSTICEGO_GEOMETRY_PATH||path.join(home,'native','published','MysticEgo.DesktopGeometry'+(process.platform==='win32'?'.exe':''));
function geometry(){return new Promise((resolve,reject)=>{execFile(process.env.MYSTICEGO_DOTNET||geoBinary,process.env.MYSTICEGO_DOTNET?[path.join(home,'..','runtime','app','bin','MysticEgo.DesktopGeometry.dll'),'--json']:['--json'],{timeout:3500,maxBuffer:262144},(error,stdout)=>{if(error)return reject(error);try{resolve(JSON.parse(stdout))}catch(e){reject(e)}})})}
ipcMain.handle('avatar:login',async(_event,login,password)=>{
 if(typeof login!=='string'||typeof password!=='string'||login.length>160||password.length>256)throw new Error('Некорректные данные входа');
 const auth=process.env.MYSTICEGO_DESKTOP_AUTH_URL||'http://127.0.0.1:5400';
 if(!isLocal(auth))throw new Error('Требуется локальный API');
 const response=await fetch(auth.replace(/\/$/,'')+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({login,password}),redirect:'error',signal:AbortSignal.timeout(10000)});
 if(!response.ok)throw new Error('Ошибка входа: '+response.status);
 return (await response.json()).accessToken;
});
ipcMain.handle('avatar:request',async(event,params)=>{
 if(event.sender!==window.webContents)throw new Error('Недопустимый источник');
 const {api,path:route,token='',guest='',method='GET',body}=params||{};
 if(!isLocal(api)||typeof route!=='string'||!['GET','POST'].includes(method)||
    typeof token!=='string'||token.length>8192||typeof guest!=='string'||guest.length>64)throw new Error('Запрос не разрешён');
 const endpoint=new URL(api);
 if(!['5300','5350'].includes(endpoint.port)&&endpoint.origin!==configuredApi)throw new Error('Недопустимый порт');
 const url=new URL(route,endpoint);
 if(url.origin!==endpoint.origin||url.search||url.hash)throw new Error('Недопустимый маршрут');
 const routes=/^\/api\/v1\/(avatars\/((personal|account)\/(profile|asset\/[a-f0-9]{32})|theme(\/assets\/[a-f0-9]{32})?)|assistant\/(persona|workbench\/(chat|proposals\/[0-9a-f-]{36}\/confirm))|guest\/(login|assistant|logout))$/i;
 if(!routes.test(url.pathname)&&url.pathname!=='/bundled/mysticego.vrm')throw new Error('Недопустимый маршрут');
 const headers={};
 if(token)headers.Authorization='Bearer '+token;
 if(guest)headers['X-Guest-Session']=guest;
 if(method==='POST')headers['Content-Type']='application/json';
 const response=await fetch(url,{method,headers,body:method==='POST'?String(body||'{}').slice(0,32768):undefined,signal:AbortSignal.timeout(155000),redirect:'error'});
 const bytes=await response.arrayBuffer();if(bytes.byteLength>34*1024*1024)throw new Error('Ответ слишком велик');
 return {status:response.status,ok:response.ok,data:Buffer.from(bytes)};
});
ipcMain.handle('avatar:walk',(event,enabled)=>{
 if(event.sender!==window.webContents)throw new Error('Недопустимый источник');
 stopWalk();if(enabled!==true)return true;
 let direction=-1;
 walkTimer=setInterval(()=>{
  if(!window||window.isDestroyed()){stopWalk();return}
  const {x,y,width}=window.getBounds();
  const work=screen.getDisplayMatching(window.getBounds()).workArea;
  const left=work.x+6,right=Math.max(left,work.x+work.width-width-6);
  let position=x+direction*4;
  if(position<=left||position>=right){direction*=-1;position=Math.max(left,Math.min(right,position));window.webContents.send('avatar:edge')}
  window.setPosition(position,y);
 },55);
 return true;
});
ipcMain.handle('avatar:approved-link',(event,raw)=>{
 if(event.sender!==window.webContents)throw new Error('Недопустимый источник');
 const url=new URL(raw);
 if(url.protocol!=='https:'||!['www.google.com','www.youtube.com'].includes(url.host)||url.username||url.password)throw new Error('Адрес не разрешён');
 return shell.openExternal(url.href);
});
ipcMain.handle('avatar:context',async event=>{
 if(event.sender!==window.webContents)throw Error('Недопустимый источник');
 if(!handoffShown)handoff=await handoffInput;
 return {api:configuredApi,scope:process.env.MYSTICEGO_DESKTOP_SCOPE||'audit',handoff};
});
ipcMain.handle('avatar:handoff-ready',event=>{
 if(event.sender!==window.webContents||!handoff||handoffShown) return false;
 const bounds=window.getBounds(), area=screen.getDisplayMatching(bounds).workArea;
 window.setPosition(area.x+Math.round(Math.max(0,area.width-bounds.width)*handoff.horizontal),area.y+Math.max(0,area.height-bounds.height));
 window.showInactive();
 handoffShown=true;
 handoff=null;
 console.log('MYSTICEGO_AVATAR_HANDOFF_READY');
 return true;
});
ipcMain.handle('avatar:dock-taskbar',()=>{dockTaskbar();return true});
ipcMain.handle('avatar:windows',async()=>{try{return await geometry()}catch{return []}});
ipcMain.handle('avatar:anchor',async(_event,title)=>{
 stopWalk();
 if(typeof title!=='string'||title.length>160)throw new Error('Bad title');
 const windows=await geometry().catch(()=>[]);
 const match=windows.find(x=>x.title===title&&x.width>100&&x.height>100);
 if(!match)return false;
 const display=screen.getDisplayMatching({x:match.x,y:match.y,width:match.width,height:match.height});
 const bounds=display.workArea;const w=window.getBounds().width,h=window.getBounds().height;
 window.setPosition(Math.max(bounds.x,Math.min(bounds.x+bounds.width-w,match.x+match.width-w)),Math.max(bounds.y,Math.min(bounds.y+bounds.height-h,match.y-h+52)));
 return true;
});
ipcMain.handle('avatar:click-through',(_event,ignore)=>{window.setIgnoreMouseEvents(Boolean(ignore),{forward:true});return true});
ipcMain.handle('avatar:close',()=>{window.close();return true});
app.whenReady().then(()=>{create();app.on('activate',()=>{if(BrowserWindow.getAllWindows().length===0)create()})});
app.on('window-all-closed',()=>{stopWalk();app.quit()});
