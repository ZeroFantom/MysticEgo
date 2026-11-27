const {app,BrowserWindow,ipcMain,shell,dialog,clipboard}=require('electron');
const {createLocalCenter}=require('./local-center.cjs');
const {installInterfaceMcp}=require('./interface-mcp.cjs');
const {spawn}=require('node:child_process'); const fs=require('node:fs'); const path=require('node:path');const readline=require('node:readline');const crypto=require('node:crypto');
const windows=process.platform==='win32';
const root=path.join(__dirname,'..');
function windowsTool(command){
  if(!windows)return command;
  if(command==='gh'){
    for(const candidate of [path.join(root,'windows-tools','github','gh.exe'),path.join(process.env.ProgramFiles||'C:\\Program Files','GitHub CLI','gh.exe')])if(fs.existsSync(candidate))return candidate;
  }
  return command+'.exe';
}
function processEnvironment(){
  const env={...process.env};
  if(windows){
    const system=path.join(process.env.SystemRoot||'C:\\Windows','System32','OpenSSH');
    const names=['ssh.exe','scp.exe','ssh-keygen.exe','ssh-keyscan.exe'];
    const ssh=names.every(name=>fs.existsSync(path.join(system,name)))?system:path.join(root,'windows-tools','openssh');
    const gh=path.dirname(windowsTool('gh'));
    const key=Object.keys(env).find(k=>k.toLowerCase()==='path')||'Path';
    env[key]=[ssh,gh==='.'?'':gh,env[key]||''].filter(Boolean).join(path.delimiter);
  }
  return env;
}
// Windows Start-Process opens a real interactive console, not redirected SSH stdin.
// The outer PowerShell receives only Base64 and all native arguments are quoted.
const psLiteral=value=>"'"+String(value).replaceAll("'","''")+"'";
function winQuote(value){const s=String(value);return '"'+s.replace(/(\\*)"/g,'$1$1\\"').replace(/(\\+)$/,'$1$1')+'"';}
function openTerminal(command,args,env=processEnvironment()){
  return new Promise((resolve,reject)=>{
    let child;
    if(windows){
      const script='Start-Process -FilePath '+psLiteral(command)+' -ArgumentList '+psLiteral(args.map(winQuote).join(' '))+' -ErrorAction Stop';
      child=spawn('powershell.exe',['-NoProfile','-NonInteractive','-EncodedCommand',Buffer.from(script,'utf16le').toString('base64')],{env,windowsHide:true,stdio:'ignore'});
      child.once('error',reject);child.once('exit',code=>code===0?resolve({opened:true}):reject(Error('Не удалось открыть консоль Windows.')));
    }else{
      const variants=[['konsole',['-e',command,...args]],['gnome-terminal',['--',command,...args]],['x-terminal-emulator',['-e',command,...args]]];
      const next=()=>{const item=variants.shift();if(!item)return reject(Error('Не найден поддерживаемый терминал Linux.'));
        child=spawn(item[0],item[1],{env,stdio:'ignore',detached:true});child.once('error',next);child.once('spawn',()=>{child.unref();resolve({opened:true});});};next();
    }
  });
}
let backend,win,center,updatesController,localMutations=0,quitting=false,quitReady=false; const pending=new Map();
const allowed=new Set(['github.status','github.release','github.enable','github.disconnect','timeweb.status','timeweb.connect','timeweb.restore','timeweb.disconnect','servers','catalog','key.info','key.register','server.create','ssh.prepare','ssh.status','ssh.trust','ssh.cloudKey','ssh.passwordKey','ssh.terminal','ssh.test','ssh.command','deploy','deploy.inspectOrchestra','deploy.resetPreview','deploy.factoryReset','deploy.maintenancePlan','deploy.maintenanceReport','deploy.executeMaintenance','deploy.restoreOrchestra','deploy.recoverMaintenance','deploy.auditPortal','deploy.infrastructure','deploy.reuploadGlobal','deploy.preflight','domains.list','dns.check','dns.prepare','topology.poll','topology.developerPassword','topology.login','topology.logout','topology.discover','dns.suggest','dns.inventory','dns.delete','deploy.reserve','dns.reconcile']);
function check(e){if(!win||e.sender!==win.webContents||e.senderFrame!==win.webContents.mainFrame)throw Error('IPC denied');}
function request(action,args){return new Promise((resolve,reject)=>{if(!backend||!backend.stdin.writable)return reject(Error('C# backend недоступен'));const id=crypto.randomUUID();pending.set(id,{resolve,reject,action,flowId:typeof args?.flowId==='string'&&/^[0-9]+$/.test(args.flowId)?args.flowId:null});backend.stdin.write(JSON.stringify({id,action,args})+'\n',err=>{if(err){pending.delete(id);reject(err);}});});}
if(!app.requestSingleInstanceLock()){app.quit();}else{
app.on('second-instance',()=>{if(win){if(win.isMinimized())win.restore();win.focus();}});
app.whenReady().then(()=>{
 const dotnet=process.env.CLOUDDESK_DOTNET||'dotnet';
 const bundle=process.env.CLOUDDESK_BUNDLE_ROOT||path.resolve(root,'../..');
 center=createLocalCenter({root:bundle,dotnet,home:app.getPath('home'),env:processEnvironment(),openExternal:url=>shell.openExternal(url),openPath:p=>shell.openPath(p),copy:value=>clipboard.writeText(value)});
 for(const action of ['snapshot','start','stop','save','credentials','link'])ipcMain.handle('local-center:'+action,async(e,...args)=>{check(e);if(action==='snapshot')return center[action](...args);if(updatesController?.restarting())throw Error('Приложение перезапускается.');localMutations++;try{return await center[action](...args)}finally{localMutations--}});
 backend=spawn(process.env.CLOUDDESK_DOTNET||'dotnet',[path.join(root,'out','CloudDesk.dll')],{stdio:['pipe','pipe','pipe'],windowsHide:true,env:processEnvironment()});
 const fail=()=>{for(const p of pending.values())p.reject(Error('Backend остановлен'));pending.clear();};backend.on('error',fail);backend.on('exit',fail);backend.stderr.resume();
 readline.createInterface({input:backend.stdout}).on('line',line=>{try{const r=JSON.parse(line);const p=pending.get(r.id);if(p){
   if(typeof r.progress==='string'){
     if(p.flowId&&['ssh.prepare','ssh.trust','ssh.cloudKey','ssh.passwordKey'].includes(p.action)&&['identity','access','ready'].includes(r.progress)&&win&&!win.isDestroyed())win.webContents.send('ssh-progress',{flowId:p.flowId,stage:r.progress});
     if(['deploy','deploy.factoryReset','deploy.executeMaintenance','deploy.restoreOrchestra','deploy.recoverMaintenance','deploy.auditPortal','deploy.infrastructure'].includes(p.action)&&win&&!win.isDestroyed())win.webContents.send('deploy-progress',r.progress);
     return;
   }
   pending.delete(r.id);r.ok?p.resolve(r.result):p.reject(Error(r.error));
 }}catch{}});
 ipcMain.handle('rpc',async(e,action,args)=>{check(e);if(!allowed.has(action))throw Error('Unknown action');if(updatesController?.restarting())throw Error('Приложение перезапускается.');if(action==='deploy.factoryReset'){const target=String(args?.domain||''),tag=String(args?.releaseTag||'');if(!/^[a-zA-Z0-9.-]{1,253}$/.test(target)||!/^v1\.0\.0-beta\.7-r\d+\.\d+\.\d+$/.test(tag)||args.confirmation!=='DELETE '+target)throw Error('Invalid reset confirmation');const r=await dialog.showMessageBox(win,{type:'warning',buttons:['Отмена','Удалить данные и переустановить'],defaultId:0,cancelId:0,message:'Необратимый сброс '+target,detail:'Будут удалены локальные базы Auth и Orchestra, аккаунты, настройки, история и резервные версии. Новая версия: '+tag+'. VPS, DNS и SSH сохранятся.'});if(r.response!==1)throw Error('Полная переустановка отменена.');check(e);}if(action==='ssh.terminal')return request(action,args).then(r=>openTerminal(r.command,r.args));return request(action,args);});
 ipcMain.handle('login',e=>{check(e);const env=processEnvironment();delete env.GH_PROMPT_DISABLED;return openTerminal(windowsTool('gh'),['auth','login','--hostname','github.com','--git-protocol','https','--web'],env);});
 ipcMain.handle('orchestraPanel',e=>{check(e);return shell.openExternal('https://orchestra.isdarkline.ru/');});
 ipcMain.handle('cloudConsole',e=>{check(e);return shell.openExternal('https://timeweb.cloud/my/');});
 ipcMain.handle('portal',e=>{check(e);return shell.openExternal('https://timeweb.cloud/my/api-keys');});
 ipcMain.handle('script',async e=>{check(e);const r=await dialog.showOpenDialog(win,{properties:['openFile'],filters:[{name:'Bash',extensions:['sh']}]});return r.canceled?'':r.filePaths[0];});
 win=new BrowserWindow({width:1320,height:900,minWidth:900,minHeight:650,backgroundColor:'#101016',icon:path.join(__dirname,'icon.png'),webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true}});
 const pairingPath=path.join(bundle,'desktop','browser-pairing.cjs');
 if(fs.existsSync(pairingPath)){
   const pairing=require(pairingPath);
   pairing.installBrowserPairing({product:'CloudDesk',window:()=>win,
     getSession:async()=>{const s=await request('browser.pairingStatus',{});return s.available?s:null;},
     approve:async(session,challenge)=>(await request('browser.pairingApprove',{origin:session.origin,identity:session.identity,challenge})).code});
 }
 win.on('close',event=>{if(!quitReady){event.preventDefault();app.quit();}});
 win.webContents.setWindowOpenHandler(()=>({action:'deny'}));win.webContents.on('will-navigate',e=>e.preventDefault());win.loadFile(path.join(root,'dist','index.html'));installInterfaceMcp(win,'CloudDesk',0,{updates:()=>updatesController});
 const updates=path.join(bundle,'desktop','desktop-updates.cjs');
 if(fs.existsSync(updates)){
   const updater=require(updates);
   const readOnly=new Set(['github.status','github.release','timeweb.status','servers','catalog','ssh.status','topology.poll','topology.discover','dns.suggest','dns.inventory','deploy.maintenancePlan','deploy.preflight','domains.list']);
   updatesController=updater.startDesktopUpdates({app,root:bundle,product:'CloudDesk',window:()=>win,canRestart:()=>!quitting&&!localMutations&&![...pending.values()].some(p=>!readOnly.has(p.action))});
   updater.registerUpdateIpc(ipcMain,()=>win,updatesController);
 }
});app.on('window-all-closed',()=>app.quit());app.on('before-quit',event=>{
 if(quitReady)return;
 event.preventDefault();if(quitting)return;quitting=true;
 Promise.resolve(center?.close()).then(()=>{backend?.stdin.end();quitReady=true;app.quit();}).catch(error=>{quitting=false;if(updatesController?.restarting())updatesController.cancelRestart(error);dialog.showErrorBox('CloudDesk',error.message);});
});

}
