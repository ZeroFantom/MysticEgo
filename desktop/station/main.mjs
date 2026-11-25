import {app,BrowserWindow,WebContentsView,dialog,ipcMain,Menu,shell} from 'electron';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {launch,json} from '../control/center.mjs';
import {developmentTeam} from './development-team.mjs';
import {getDotnet} from '../launch-runtime.mjs';

const here=path.dirname(fileURLToPath(import.meta.url));
const root=path.resolve(here,'../..');
const home=path.join((process.env.LOCALAPPDATA||process.env.XDG_DATA_HOME||path.join(app.getPath('home'),'.local','share')),'MysticEgo','AuditDesktop');
const runtime=path.join(root,'runtime','app','bin');
const installer=path.join(runtime,'MysticEgo.Installer.dll');
const layout=path.join(here,'index.html');
const launcher=pathToFileURL(layout).href;
const roles={audit:'Audit',agent:'Agent',global:'Global',local:'Local'};
const ports={audit:5350,agent:5300,global:5100,local:5200};
const dotnet=getDotnet(root);
const execute=promisify(execFile);
const logs=[];
const development=developmentTeam(json);
let teamStatus=null,teamChecked=0,teamRefresh,accountGeneration=0;
async function refreshTeam(force=false){
  if(!actor)return;
  if(teamRefresh||!force&&Date.now()-teamChecked<10000)return teamRefresh;
  const generation=accountGeneration;
  teamChecked=Date.now();
  teamRefresh=authorized().then(development.status).then(info=>{
    if(actor&&generation===accountGeneration)teamStatus=info;
  }).catch(()=>{
    if(actor&&generation===accountGeneration)teamStatus={error:true};
  }).finally(()=>{teamRefresh=null;});
  return teamRefresh;
}
let window,embedded,recording,runner,companion,closing=false,profile,profileFile,assignment,actor,refreshing,discovering=false;
let personalProfile, personalFile, personalRunner, openingAudit;
let embeddedRole='';
let beacon='',clubAvailable=false,playerGlobals=[],features={allow:true,processes:'notepad, mspaint, calc',apps:{}};

function log(line){logs.push(new Date().toLocaleTimeString('ru-RU')+'  '+String(line).slice(0,1200));if(logs.length>150)logs.shift()}
function current(entry=profile){
  const credential=entry?.credentials[0];
  if(!credential)return null;
  const address=new URL(entry.endpoints.publicUrl);
  const port=address.protocol==='http:'?Number(address.port||80):ports[credential.kind];
  return {kind:credential.kind,id:credential.identityId,name:entry.name,url:'http://127.0.0.1:'+port,central:entry.endpoints.orchestraUrl};
}
function address(value,lan=false){
  const url=new URL(value);
  if(url.username||url.password||url.search||url.hash||url.pathname!=='/'||
    !(url.protocol==='https:'||url.protocol==='http:'&&(lan||['127.0.0.1','localhost','[::1]'].includes(url.hostname))))
    throw Error('Введите HTTPS-адрес или включите HTTP для доверенной локальной сети.');
  return url.origin;
}
function parseProfile(entry){
  const credentials=entry?.credentials;
  const links=entry?.endpoints;
  if(!Array.isArray(credentials)||credentials.length!==1||!Object.hasOwn(roles,credentials[0]?.kind)
    ||!/^[0-9a-f-]{36}$/i.test(credentials[0]?.identityId)||!links||links.mode!=='native')
    throw Error('Конфигурация должна содержать одну нативную роль.');
  for(const field of ['authUrl','orchestraUrl','publicUrl'])address(links[field],links.allowLanHttp===true);
  return entry;
}
async function readProfile(file){
  const stat=await fs.stat(file);
  if(!stat.isFile()||stat.size>65536)throw Error('Выберите JSON-конфигурацию до 64 КиБ.');
  return parseProfile(JSON.parse(await fs.readFile(file,'utf8')));
}
async function storeProfile(entry,file){
  const text=JSON.stringify(entry);
  await fs.writeFile(file+'.tmp',text,{mode:0o600});
  await fs.rename(file+'.tmp',file);
}
async function fingerprint(){
  const {stdout}=await execute(dotnet,[installer,'--host-fingerprint'],{windowsHide:true,timeout:8000});
  const id=stdout.trim();
  if(!/^[a-f0-9]{64}$/.test(id))throw Error('Не удалось определить компьютер.');
  return id;
}
async function health(node=current()){
  if(!node)return false;
  try{return (await json(node.url+'/health/live',{signal:AbortSignal.timeout(1500)})).node===node.id}
  catch{return false}
}
async function readAuth(){
  if(!actor)return null;
  if(Date.now()<actor.expiration-45000)return actor;
  if(!refreshing){
    const previous=actor;
    const generation=accountGeneration;
    refreshing=async()=>{
      const data=await json(previous.central+'/api/auth/refresh',{method:'POST',headers:{'Content-Type':'application/json'},
        body:JSON.stringify({refreshToken:previous.refreshToken})});
      if(generation!==accountGeneration)return actor;
      actor={...previous,token:data.accessToken,refreshToken:data.refreshToken,expiration:Date.parse(data.expiresAt)};
      return actor;
    };
    refreshing=refreshing().catch(()=>{if(generation===accountGeneration){actor=null;playerGlobals=[];}return actor}).finally(()=>{refreshing=null});
  }
  return refreshing;
}
async function authorized(){
  const state=await readAuth();
  if(!state)throw Error('Сначала войдите в учётную запись.');
  return state;
}
async function topology(){
  if(!actor)return [];
  const generation=accountGeneration;
  const {token,central}=await authorized();
  const rows=await json(central+'/api/v1/managed/topology',{headers:{Authorization:'Bearer '+token}});
  if(generation!==accountGeneration)return [];
  playerGlobals=rows.filter(row=>row.node.kind==='global'&&row.node.publicUrl&&row.report?.healthy?.includes('global')).map(row=>row.node);
  return playerGlobals;
}
let lastTopology=0;
async function userSession(input){
  const base=address(input?.central, input?.lan===true);
  const login=String(input?.login??'').trim(),password=String(input?.password??'');
  if(login.length<2||login.length>100||password.length<1||password.length>128)throw Error('Проверьте логин и пароль.');
  if(input.register===true){
    const nickname=String(input?.nickname??'').trim();
    await json(base+'/api/auth/audit/register',{method:'POST',headers:{'Content-Type':'application/json'},
      body:JSON.stringify({login,password,nickname})});
  }
  const data=await json(base+'/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},
    body:JSON.stringify({login,password})});
  accountGeneration++;
  actor={login:data.user?.login||login,token:data.accessToken,refreshToken:data.refreshToken,expiration:Date.parse(data.expiresAt),central:base,lan:input?.lan===true};
  await topology();
  teamStatus=null;teamChecked=0;
  void refreshTeam();
  await fs.writeFile(path.join(home,'connection.json'),JSON.stringify({central:base,lan:actor.lan}),{mode:0o600});
  return 'Вход выполнен';
}
async function start(options={},passive=false){
  if(runner)throw Error('Узел уже запущен в этом окне.');
  const node=current();if(!node)throw Error('Выберите конфигурацию узла.');
  if(!passive)await authorized();
  if(passive&&node.kind!=='agent')throw Error('Автоматический запуск доступен для клубного Agent.');
  if(await health())return;
  const input=String(options?.processes??features.processes).split(/[;,]/).map(s=>s.trim()).filter(Boolean);
  if(input.length>25||input.some(item=>!/^[a-z0-9_.-]{2,80}$/i.test(item)))throw Error('Введите имена процессов через запятую, например notepad, paint.');
  features={...features,allow:options?.allow??features.allow,processes:input.join(', ')};
  if(!passive)await fs.writeFile(path.join(home,'preferences.json'),JSON.stringify(features),{mode:0o600});
  runner=launchStation(node,profileFile,{sharedModel:!!personalRunner});
  const started=runner;
  started.done.catch(error=>log(error.message)).finally(()=>{if(runner===started)runner=null});
}
function launchStation(node,file,{sharedModel=false}={}){
  const argv=[installer,'--join',file,'--parent-stdin','--root',root,'--output',path.join(home,node.id)];
  if(sharedModel)argv.push('--no-lm');
  return launch(dotnet,argv,root,line=>log(node.kind+': '+line),{
    MYSTICEGO_DOTNET:dotnet,MYSTICEGO_GUI_SESSION:'1',
    MYSTICEGO_ALLOW_AUTOMATION:String(features.allow&&process.platform==='win32'&&['agent','audit'].includes(node.kind)),
    MYSTICEGO_AUTOMATION_PROCESSES:features.processes.split(',').map(item=>item.trim()).join(';'),
    MYSTICEGO_LAUNCH_ALLOWLIST_JSON:JSON.stringify(features.apps||{})
  });
}
function stop(){return stopRunner(runner)}
async function stopRunner(process){
  if(!process)return;
  process.child.stdin.end('stop\n');await process.done.catch(()=>{});
}
function fitPanel(){
  if(!embedded)return;
  const [width,height]=window.getContentSize();
  embedded.setBounds({x:0,y:57,width,height:Math.max(0,height-57)});
}
function closePanel(){
  const old=embedded;if(!old)return;
  embedded=null;
  embeddedRole='';
  window.contentView.removeChildView(old);
  old.webContents.close();
}
async function openPanel(url,role=''){
  const target=new URL(url);
  closePanel();
  const view=new WebContentsView({webPreferences:{sandbox:true,contextIsolation:true,nodeIntegration:false}});
  embedded=view;
  embeddedRole=role;
  window.contentView.addChildView(view);
  fitPanel();
  view.webContents.setWindowOpenHandler(({url:next})=>{
    try{
      const value=new URL(next);
      const known=playerGlobals.some(node=>new URL(node.publicUrl).origin===value.origin);
      if(known)void openPanel(value.href);
      else if(['https:','http:'].includes(value.protocol)&&!value.username&&!value.password)void shell.openExternal(value.href);
    }catch{}
    return {action:'deny'};
  });
  view.webContents.on('will-navigate',(event,next)=>{if(new URL(next).origin!==target.origin)event.preventDefault()});
  try{await view.webContents.loadURL(target.href)}
  catch(error){if(embedded===view)closePanel();throw error}
}
function activateHome(){
  if(openingAudit)return openingAudit;
  openingAudit=openPersonal().finally(()=>{openingAudit=null});
  return openingAudit;
}
async function openPersonal(){
  const session=await authorized();
  const generation=accountGeneration;
  if(personalProfile?.endpoints.ownerLogin!==session.login||personalProfile.endpoints.orchestraUrl!==session.central){
    await stopRunner(personalRunner);
    const added=parseProfile(await json(session.central+'/api/v1/managed/home/connect',{method:'POST',headers:{'Content-Type':'application/json',
      Authorization:'Bearer '+session.token},body:JSON.stringify({machine:await fingerprint()})}));
    if(added.credentials[0].kind!=='audit'||added.endpoints.ownerLogin!==session.login||added.endpoints.orchestraUrl!==session.central)
      throw Error('Orchestra вернула неподходящую конфигурацию Audit.');
    if(generation!==accountGeneration||closing)throw Error('Учётная запись изменилась.');
    personalFile=path.join(home,'personal-'+added.credentials[0].identityId+'.json');
    await storeProfile(added,personalFile);
    personalProfile=added;
  }
  const node=current(personalProfile);
  const ready=await health(node);
  if(generation!==accountGeneration||closing)throw Error('Учётная запись изменилась.');
  if(!personalRunner&&!ready){
    const started=launchStation(node,personalFile,{sharedModel:!!runner});
    personalRunner=started;
    started.done.catch(error=>log(error.message)).finally(()=>{if(personalRunner===started)personalRunner=null});
  }
  const deadline=Date.now()+16000;
  while(!await health(node)){
    if(generation!==accountGeneration||closing)throw Error('Учётная запись изменилась.');
    if(Date.now()>deadline||!personalRunner)throw Error('Audit пока запускается. Откройте панель после готовности.');
    await new Promise(resolve=>setTimeout(resolve,500));
  }
  if(generation!==accountGeneration||closing)throw Error('Учётная запись изменилась.');
  return openPanel(node.url,'audit');
}
async function followClub(){
  if(discovering||closing)return;
  discovering=true;
  try{
    if(current()?.kind==='agent'){
      if(!runner&&!await health())await start({},true);
      try{
        const status=await json(current().url+'/api/v1/guest/availability',{signal:AbortSignal.timeout(2000)});
        clubAvailable=status.available===true;
        beacon=clubAvailable?'Подключён к клубу':'Local или Global временно недоступен';
      }catch{clubAvailable=false;beacon=runner?'Запускает Agent':'Ожидает клубную сеть'}
      return;
    }
    if(!assignment||current()||runner)return;
    const local=address(assignment.localUrl,assignment.allowLanHttp===true);
    const info=await json(local+'/api/v1/public/club',{signal:AbortSignal.timeout(2000)});
    if(info.localId!==assignment.localId||info.globalId!==assignment.globalId)throw Error('Адрес отвечает за другую площадку.');
    beacon='Обнаружен Local · '+info.name;
    const entry=parseProfile(await json(local+'/api/v1/public/club/enroll',{method:'POST',headers:{'Content-Type':'application/json'},
      body:JSON.stringify({invitationId:assignment.invitationId,code:assignment.code,machine:await fingerprint(),name:os.hostname()})}));
    if(entry.credentials[0].kind!=='agent'||entry.endpoints.localId!==assignment.localId||entry.endpoints.globalId!==assignment.globalId)
      throw Error('Ответ площадки относится к другой конфигурации.');
    const file=path.join(home,'station.json');await storeProfile(entry,file);profile=entry;profileFile=file;
    await start({},true);log('Agent зарегистрирован · '+info.name);
  }catch(error){clubAvailable=false;beacon=String(error.message).includes('HTTP')?error.message:'Ожидает Local клуба';}
  finally{discovering=false}
}
function handle(name,action){
  ipcMain.handle('station:'+name,(event,...args)=>{
    if(event.sender!==window.webContents||event.senderFrame?.url!==launcher)throw Error('Недопустимый источник запроса.');
    return action(...args);
  });
}
async function open(){
  Menu.setApplicationMenu(null);
  await fs.mkdir(home,{recursive:true,mode:0o700});
  const local=path.join(home,'station.json');
  try{profile=await readProfile(local);profileFile=local}catch(error){if(error.code!=='ENOENT')log(error.message)}
  try{features={...features,...JSON.parse(await fs.readFile(path.join(home,'preferences.json'),'utf8'))}}catch{}
  let lastConnection={central:'',lan:false};
  try{lastConnection=JSON.parse(await fs.readFile(path.join(home,'connection.json'),'utf8'))}catch{}
  try{
    assignment=JSON.parse(await fs.readFile(path.join(root,'club-assignment.json'),'utf8'));
    beacon='Ищем Local · '+assignment.name;
  }catch{}
  window=new BrowserWindow({title:'AuditDesktop · MysticEgo',width:1090,height:795,minWidth:670,minHeight:620,backgroundColor:'#15131e',
    webPreferences:{preload:path.join(here,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true}});
  window.on('resize',fitPanel);
  window.webContents.setWindowOpenHandler(()=>({action:'deny'}));
  window.webContents.on('will-navigate',event=>event.preventDefault());
  const discovery=setInterval(()=>void followClub(),6000);
  window.on('close',async event=>{
    if(closing)return;event.preventDefault();closing=true;clearInterval(discovery);closePanel();
    if(recording)recording.child.stdin.end('\n');
    if(companion)companion.child.kill();
    await Promise.all([stop(),stopRunner(personalRunner)]);window.close();
  });
  handle('snapshot',async()=>{
    void refreshTeam();
    if(actor&&Date.now()-lastTopology>18000){lastTopology=Date.now();void topology().catch(()=>{})}
    const [online,personalOnline]=await Promise.all([health(),personalProfile?health(current(personalProfile)):false]);
    return {node:current(),online,running:!!runner,personalOnline,personalRunning:!!personalRunner,recording:!!recording,features,beacon,clubAvailable,embedded:!!embedded,
      actor:actor?.login,team:actor?teamStatus:null,globals:playerGlobals.map(({id,name})=>({id,name})),central:current()?.central||assignment?.orchestraUrl||lastConnection.central,
      lan:profile?.endpoints.allowLanHttp===true||assignment?.allowLanHttp===true||lastConnection.lan,platform:process.platform,logs:[...logs]};
  });
  handle('auth',userSession);
  handle('logout',async()=>{
    accountGeneration++;
    const old=actor;actor=null;playerGlobals=[];teamStatus=null;teamChecked=0;closePanel();
    if(recording)recording.child.stdin.end('\n');
    if(companion)companion.child.kill();
    await stopRunner(personalRunner);
    personalProfile=undefined;personalFile=undefined;
    if(current()?.kind==='audit')await stop();
    if(old)void fetch(old.central+'/api/auth/logout',{method:'POST',headers:{'Content-Type':'application/json'},
      body:JSON.stringify({refreshToken:old.refreshToken}),signal:AbortSignal.timeout(3000)}).catch(()=>{});
  });
  handle('import',async()=>{
    const account=await authorized();
    if(runner)throw Error('Остановите текущую роль перед сменой конфигурации.');
    if(assignment||current()?.kind==='agent')throw Error('Этот экземпляр AuditDesktop закреплён за ПК клуба.');
    const pick=await dialog.showOpenDialog(window,{properties:['openFile'],filters:[{name:'Конфигурация MysticEgo',extensions:['json']}]});
    if(pick.canceled)return '';
    const entry=await readProfile(pick.filePaths[0]);
    if(!['local','global'].includes(entry.credentials[0].kind)||entry.endpoints.orchestraUrl!==account.central)
      throw Error('Выберите конфигурацию Global или Local из этого Оркестра.');
    const rights=await json(account.central+'/api/v1/managed/access/'+entry.credentials[0].identityId,{
      headers:{Authorization:'Bearer '+account.token}});
    if(!rights.canManage)throw Error('Нужны полномочия владельца организации.');
    await storeProfile(entry,local);profile=entry;profileFile=local;
    return 'Конфигурация '+roles[current().kind]+' принята.';
  });
  handle('start',options=>start(options));
  handle('stop',async()=>{await authorized();return stop()});
  handle('panel',async()=>{
    const node=current();if(!node||!await health())throw Error('Дождитесь запуска выбранной роли.');
    if(node.kind!=='agent')await authorized();
    return openPanel(node.url,node.kind);
  });
  handle('home',activateHome);
  handle('audit-stop',async()=>{
    await authorized();
    if(embeddedRole==='audit')closePanel();
    return stopRunner(personalRunner);
  });
  handle('team-apply',async()=>{
    const result=await development.apply(await authorized());
    await refreshTeam(true);
    return result;
  });
  handle('orchestra',async()=>{
    const generation=accountGeneration;
    const session=await authorized();
    const target=await development.orchestra(session);
    if(!actor||accountGeneration!==generation)throw Error('Учётная запись изменилась. Повторите действие.');
    return openPanel(target);
  });
  handle('global',async id=>{
    await authorized();const org=playerGlobals.find(node=>node.id===id);
    if(!org)throw Error('Организация ещё не готова.');
    return openPanel(address(org.publicUrl,actor.lan));
  });
  handle('portal',(value,lan)=>openPanel(address(value,lan===true)+'/audit-portal/'));
  handle('back',closePanel);
  handle('companion',async role=>{
    const node=(role||embeddedRole)==='audit'?(personalProfile?current(personalProfile):null):current();
    if(!node||!['audit','agent'].includes(node.kind)||!await health(node))throw Error('Для аватара требуется Audit или Agent.');
    if(node.kind==='audit')await authorized();
    if(companion)return;
    companion=launch(process.execPath,[path.join(root,'desktop')],root,log,{
      MYSTICEGO_DOTNET:dotnet,MYSTICEGO_DESKTOP_API_URL:node.url,MYSTICEGO_DESKTOP_AUTH_URL:node.url,MYSTICEGO_DESKTOP_SCOPE:node.kind
    });
    companion.done.catch(error=>log(error.message)).finally(()=>{companion=null});
  });
  handle('app',async(name,remove)=>{
    await authorized();
    if(runner||personalRunner)throw Error('Остановите локальные роли перед изменением списка приложений.');
    name=String(name??'').trim().toLowerCase();
    if(!/^[a-z0-9_.-]{1,40}$/.test(name))throw Error('Введите псевдоним латиницей, например warp или notepad.');
    if(remove)delete features.apps[name];
    else{
      const chosen=await dialog.showOpenDialog(window,{title:'Выберите установленное приложение',properties:['openFile']});
      if(chosen.canceled)return '';
      const target=chosen.filePaths[0];
      if(process.platform==='win32'&&path.extname(target).toLowerCase()!=='.exe')throw Error('Выберите установленный файл EXE.');
      if(process.platform!=='win32'&&!((await fs.stat(target)).mode&0o111))throw Error('Выберите исполняемый файл.');
      features.apps[name]=target;
    }
    await fs.writeFile(path.join(home,'preferences.json'),JSON.stringify(features),{mode:0o600});
    return remove?'Псевдоним удалён.':'Приложение добавлено.';
  });
  handle('record',async value=>{
    const auth=await authorized();
    if(process.platform!=='win32'||recording)throw Error('Запись доступна в интерактивной сессии Windows.');
    const name=String(value??'').trim();
    if(!/^[a-zA-Z0-9_.-]{2,80}$/.test(name))throw Error('Укажите имя процесса, например notepad.');
    const file=await dialog.showSaveDialog(window,{defaultPath:name+'-automation.json',filters:[{name:'Сценарий',extensions:['json']}]});
    if(file.canceled)return '';
    recording=launch(dotnet,[path.join(runtime,'MysticEgo.AutomationRecorder.dll'),'record',name,file.filePath],root,log,{
      MYSTICEGO_RECORDER_SESSION:auth.token,MYSTICEGO_RECORDER_SERVER:auth.central
    });
    recording.done.then(()=>log('Сценарий сохранён.')).catch(error=>log(error.message)).finally(()=>{recording=null});
    return 'Запись включена. Перейдите в выбранное приложение.';
  });
  handle('finish-record',async()=>{await authorized();if(recording){recording.child.stdin.end('\n');await recording.done;return 'Запись завершена.'}});
  handle('folder',async()=>{await authorized();const result=await shell.openPath(current()?path.join(home,current().id):home);if(result)throw Error(result)});
  await window.loadFile(layout);
  void followClub();
}
if(!app.requestSingleInstanceLock())app.quit();
else{
  app.on('second-instance',()=>window?.focus());
  app.whenReady().then(open).catch(error=>{dialog.showErrorBox('AuditDesktop',error.message);app.quit()});
  app.on('window-all-closed',()=>app.quit());
}

