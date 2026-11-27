import {app,BrowserWindow,ipcMain,shell,dialog} from 'electron';
import pairing from '../browser-pairing.cjs';
import updater from '../desktop-updates.cjs';
import {launch,json} from '../control/center.mjs';
import {getDotnet} from '../launch-runtime.mjs';
import {mkdirSync} from 'node:fs';
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';

const here=path.dirname(fileURLToPath(import.meta.url));
const root=path.resolve(here,'../..');
const home=path.join(process.env.LOCALAPPDATA||process.env.XDG_DATA_HOME||path.join(app.getPath('home'),'.local','share'),'MysticEgo','LocalDesktop');
const profileFile=path.join(home,'site.json');
const view=pathToFileURL(path.join(here,'index.html')).href;
mkdirSync(home,{recursive:true,mode:0o700});
app.setName('LocalDesktop');
app.setPath('userData',path.join(home,'electron'));
const highlights=updater.createReleaseHighlights({app,root,product:'LocalDesktop'});

const guid=/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i;
let window,connection,profile,runner,closing=false,busy=false,online=false;
const logs=[];
function log(line){
  logs.push(String(line).slice(0,500));
  if(logs.length>50)logs.shift();
}
function origin(value,https=false){
  const url=new URL(value);
  if(url.username||url.password||url.search||url.hash||url.pathname!=='/'||
    !(url.protocol==='https:'||!https&&url.protocol==='http:'))throw Error('Введите адрес узла без пути.');
  return url.origin;
}
function validate(entry,site,global){
  const credential=entry?.credentials?.[0];
  const links=entry?.endpoints;
  if(entry?.credentials?.length!==1||credential.kind!=='local'||!guid.test(credential.identityId)||
    links?.mode!=='native'||!guid.test(links.globalId)||links.globalId!==links.parentId)
    throw Error('Получен неподходящий профиль Local.');
  const upstream=origin(links.globalUrl,true);
  const publicUrl=origin(links.publicUrl);
  if(site&&(credential.identityId!==site.id||links.globalId!==site.parentId||upstream!==global||publicUrl!==origin(site.publicUrl)))
    throw Error('Профиль не совпадает с выбранной площадкой Global.');
  return entry;
}
function localUrl(){
  const url=new URL(profile.endpoints.publicUrl);
  return 'http://127.0.0.1:'+(url.protocol==='http:'?url.port||80:5200);
}
async function health(){
  if(!profile)return false;
  try{return (await json(localUrl()+'/health/live',{signal:AbortSignal.timeout(1200)})).node===profile.credentials[0].identityId}
  catch{return false}
}
function start(){
  if(closing||runner||!profile)return;
  const exe=getDotnet(root);
  const id=profile.credentials[0].identityId;
  const installer=path.join(root,'runtime','app','bin','MysticEgo.Installer.dll');
  const process=launch(exe,[installer,'--join',profileFile,'--parent-stdin','--no-lm','--root',root,'--output',path.join(home,id)],root,log,{
    MYSTICEGO_DOTNET:exe,MYSTICEGO_GUI_SESSION:'1'
  });
  runner=process;
  process.done.catch(error=>log(error.message)).finally(()=>{if(runner===process)runner=null});
}
async function stop(){
  if(runner){runner.child.stdin.end('stop\n');await runner.done.catch(()=>{});}
}
async function connect({global,login,password}){
  connection=undefined;
  const base=origin(global,true);
  const auth=await json(base+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},
    body:JSON.stringify({login,password})});
  const headers={Authorization:'Bearer '+auth.accessToken};
  const [runtime,rows]=await Promise.all([json(base+'/api/v1/public/runtime'),json(base+'/api/v1/managed/topology',{headers})]);
  if(!rows.some(({node})=>node.kind==='global'&&node.id===runtime.nodeId&&node.enabled))
    throw Error('Укажите зарегистрированный Global.');
  const sites=rows.map(row=>row.node).filter(node=>node.kind==='local'&&node.enabled&&node.mode==='native'&&node.parentId===runtime.nodeId);
  connection={base,headers,sites,refreshToken:auth.refreshToken,expiresAt:Date.parse(auth.expiresAt),login:auth.user?.login||login};
  return sites.map(({id,name,publicUrl})=>({id,name,publicUrl}));
}
async function provision(id){
  if(runner||await health())throw Error('Остановите текущий Local перед сменой площадки.');
  const site=connection?.sites.find(item=>item.id===id);
  if(!site||!guid.test(id))throw Error('Выберите площадку из списка Global.');
  const {base,headers}=connection;
  const entry=validate(await json(base+'/api/v1/managed/nodes/'+id+'/profile',{headers}),site,base);
  if(closing)return;
  await fs.writeFile(profileFile+'.tmp',JSON.stringify(entry),{mode:0o600});
  await fs.rename(profileFile+'.tmp',profileFile);
  profile=entry;
  start();
}
function handle(name,action,mutating=true){
  ipcMain.handle('local:'+name,async(event,...args)=>{
    if(closing||event.sender!==window.webContents||event.senderFrame?.url!==view)throw Error('Запрос отклонён.');
    if(!mutating)return action(...args);
    if(busy)throw Error('Дождитесь завершения текущей операции.');
    busy=true;
    try{return await action(...args)}finally{busy=false}
  });
}
async function open(){
  await highlights.load();
  try{profile=validate(JSON.parse(await fs.readFile(profileFile,'utf8')))}catch(error){if(error.code!=='ENOENT')log(error.message)}
  window=new BrowserWindow({title:'LocalDesktop · MysticEgo',icon:path.join(here,'local-icon.png'),width:900,height:700,autoHideMenuBar:true,
    webPreferences:{preload:path.join(here,'preload.cjs'),sandbox:true,contextIsolation:true,nodeIntegration:false}});
  window.webContents.setWindowOpenHandler(()=>({action:'deny'}));
  window.webContents.on('will-navigate',event=>event.preventDefault());
  pairing.installBrowserPairing({product:'LocalDesktop',window:()=>window,
    getSession:()=>connection?.refreshToken&&Date.now()<connection.expiresAt?{origin:connection.base,identity:connection.headers.Authorization,login:connection.login,refreshToken:connection.refreshToken}:null,
    approve:pairing.approveSession});
  handle('connect',connect);
  handle('provision',provision);
  handle('release-highlights-dismiss',tag=>highlights.dismiss(tag),false);
  handle('status',async()=>{
    online=await health();
    return {releaseHighlights:highlights.get(),online,running:!!runner,name:profile?.name,global:profile?.endpoints.globalUrl,address:profile?.endpoints.publicUrl,logs};
  },false);
  handle('start',async()=>{if(!await health())start()});
  handle('stop',stop);
  handle('panel',async()=>{if(await health())await shell.openExternal(localUrl())});
  window.on('close',event=>{
    if(closing)return;
    event.preventDefault();closing=true;
    void stop().finally(()=>window.close());
  });
  await window.loadURL(view);
  if(!await health())start();
}
if(!app.requestSingleInstanceLock())app.quit();
else{
  app.on('second-instance',()=>window?.focus());
  app.on('window-all-closed',()=>app.quit());
  app.whenReady().then(async()=>{await open();updater.startDesktopUpdates({app,dialog,root,product:'LocalDesktop',window:()=>window,canRestart:()=>!closing&&!busy})}).catch(error=>{console.error(error);app.quit()});
}
