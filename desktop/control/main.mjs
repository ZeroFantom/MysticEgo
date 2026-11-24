import {app,BrowserWindow,clipboard,dialog,ipcMain,Menu,shell} from 'electron';
import {fileURLToPath,pathToFileURL} from 'node:url';
import fs from 'node:fs/promises';
import path from 'node:path';
import {launch,json} from './center.mjs';
import {getDotnet} from '../launch-runtime.mjs';

const home=path.dirname(fileURLToPath(import.meta.url));
const root=path.resolve(home,'../..');
const data=path.join((process.env.LOCALAPPDATA||process.env.XDG_DATA_HOME||path.join(app.getPath('home'),'.local','share')),'MysticEgo','BETA7-Lab','center');
const file=path.join(home,'index.html');
const page=pathToFileURL(file).href;
const installer=path.join(root,'runtime','app','bin','MysticEgo.Installer.dll');
const logs=[];
let program,window,closing=false,owned=false,started=false;
let settings={host:process.env.MYSTICEGO_CENTER_PUBLIC_HOST??'',lan:process.env.MYSTICEGO_ALLOW_LAN_HTTP==='true'};

async function health(){
  const check=async(port,route,field,value)=>{
    try{return (await json('http://127.0.0.1:'+port+route,{signal:AbortSignal.timeout(1500)}))[field]===value}
    catch{return false}
  };
  const [auth,orchestra]=await Promise.all([
    check(5400,'/health','service','MysticEgo.Authorization.Service'),
    check(5500,'/health/live','kind','Orchestra')
  ]);
  return {auth,orchestra};
}
function log(line){logs.push(new Date().toLocaleTimeString('ru-RU')+'  '+line);if(logs.length>180)logs.shift()}
async function start(){
  if(program||started)return;
  started=true;
  try{
    const status=await health();
    if(status.auth&&status.orchestra){log('Подключено к работающему центру.');return}
    if(status.auth||status.orchestra)throw Error('Работает только часть центра. Освободите занятый порт перед запуском.');
    const dotnet=getDotnet(root);
    program=launch(dotnet,[installer,'--lab','--no-browser','--parent-stdin','--root',root,'--output',data],root,log,{
      MYSTICEGO_DOTNET:dotnet,MYSTICEGO_GUI_SESSION:'1',MYSTICEGO_CENTER_PUBLIC_HOST:settings.host,
      MYSTICEGO_ALLOW_LAN_HTTP:String(settings.lan)
    });
    owned=true;
    program.done.catch(error=>log(error.message)).finally(()=>{program=null;owned=false});
  }finally{started=false}
}
async function stop(){
  if(!program)return;
  program.child.stdin.end('stop\n');
  await program.done.catch(()=>{});
}
function handle(name,action){
  ipcMain.handle('center:'+name,(event,...args)=>{
    if(event.sender!==window.webContents||event.senderFrame?.url!==page)throw Error('Недопустимый источник запроса.');
    return action(...args);
  });
}
async function open(){
  Menu.setApplicationMenu(null);
  await fs.mkdir(data,{recursive:true});
  const location=path.join(data,'launch-settings.json');
  try{settings={...settings,...JSON.parse(await fs.readFile(location,'utf8'))}}catch(error){if(error.code!=='ENOENT')log('Не удалось прочитать параметры запуска.')}
  window=new BrowserWindow({title:'MysticEgo · Центральный сервер',width:1000,height:760,minWidth:670,minHeight:620,backgroundColor:'#14121d',
    webPreferences:{preload:path.join(home,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true}});
  window.webContents.setWindowOpenHandler(()=>({action:'deny'}));
  window.webContents.on('will-navigate',event=>event.preventDefault());
  window.on('close',async event=>{
    if(closing)return;event.preventDefault();closing=true;
    await stop();window.close();
  });
  handle('snapshot',async()=>({health:await health(),settings,owned,running:!!program,logs:[...logs]}));
  handle('start',start);
  handle('stop',stop);
  handle('save',async incoming=>{
    if(program||(await health()).orchestra)throw Error('Остановите центр, чтобы сменить сетевой адрес.');
    const host=String(incoming?.host??'').trim();
    if(host&&!/^[a-z0-9][a-z0-9.-]{0,252}$/i.test(host))throw Error('Введите DNS-имя или IPv4 главного сервера.');
    settings={host,lan:incoming?.lan===true};
    await fs.writeFile(location,JSON.stringify(settings),{mode:0o600});
  });
  handle('credentials',async mode=>{
    if(mode==='login'){clipboard.writeText('developer');return 'Логин скопирован'}
    const secret=(await fs.readFile(path.join(data,'owner-password.txt'),'utf8')).trim();
    if(mode==='show')return secret;
    if(mode==='password'){clipboard.writeText(secret);return 'Пароль скопирован'}
    throw Error('Неизвестное действие');
  });
  handle('link',async label=>{
    if(label==='folder'){const result=await shell.openPath(data);if(result)throw Error(result);return}
    if(!['orchestra','audit'].includes(label))throw Error('Неизвестная панель');
    return shell.openExternal('http://127.0.0.1:5500'+(label==='audit'?'/audit-portal/':'/'));
  });
  await window.loadFile(file);
  start().catch(error=>log(error.message));
}
if(!app.requestSingleInstanceLock())app.quit();
else{
  app.on('second-instance',()=>window?.focus());
  app.whenReady().then(open).catch(error=>{dialog.showErrorBox('MysticEgo',error.message);app.quit()});
  app.on('window-all-closed',()=>app.quit());
}
