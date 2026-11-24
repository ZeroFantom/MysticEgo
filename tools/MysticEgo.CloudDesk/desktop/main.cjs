const {app,BrowserWindow,ipcMain,shell,dialog}=require('electron');
const {spawn}=require('node:child_process'); const path=require('node:path');const readline=require('node:readline');const crypto=require('node:crypto');
let backend,win; const pending=new Map();
const allowed=new Set(['github.status','github.release','github.enable','github.disconnect','timeweb.status','timeweb.connect','timeweb.restore','timeweb.disconnect','servers','catalog','key.info','key.register','server.create','ssh.prepare','ssh.status','ssh.trust','ssh.cloudKey','ssh.passwordKey','ssh.terminal','ssh.test','ssh.command','deploy','deploy.inspectOrchestra','deploy.restoreOrchestra','deploy.reuploadGlobal','deploy.preflight','domains.list','dns.check','dns.prepare','topology.poll','topology.developerPassword','topology.login','topology.logout','topology.discover','dns.suggest','dns.inventory','dns.delete','deploy.reserve','dns.reconcile']);
function check(e){if(!win||e.sender!==win.webContents||e.senderFrame!==win.webContents.mainFrame)throw Error('IPC denied');}
function request(action,args){return new Promise((resolve,reject)=>{if(!backend||!backend.stdin.writable)return reject(Error('C# backend недоступен'));const id=crypto.randomUUID();pending.set(id,{resolve,reject,action});backend.stdin.write(JSON.stringify({id,action,args})+'\n',err=>{if(err){pending.delete(id);reject(err);}});});}
if(!app.requestSingleInstanceLock()){app.quit();}else{
app.on('second-instance',()=>{if(win){if(win.isMinimized())win.restore();win.focus();}});
app.whenReady().then(()=>{
 const root=path.join(__dirname,'..');backend=spawn(process.env.CLOUDDESK_DOTNET||'dotnet',[path.join(root,'out','CloudDesk.dll')],{stdio:['pipe','pipe','pipe'],windowsHide:true});
 const fail=()=>{for(const p of pending.values())p.reject(Error('Backend остановлен'));pending.clear();};backend.on('error',fail);backend.on('exit',fail);backend.stderr.resume();
 readline.createInterface({input:backend.stdout}).on('line',line=>{try{const r=JSON.parse(line);const p=pending.get(r.id);if(p){
   if(typeof r.progress==='string'){
     if(['deploy','deploy.restoreOrchestra'].includes(p.action)&&win&&!win.isDestroyed())win.webContents.send('deploy-progress',r.progress);
     return;
   }
   pending.delete(r.id);r.ok?p.resolve(r.result):p.reject(Error(r.error));
 }}catch{}});
 ipcMain.handle('rpc',(e,action,args)=>{check(e);if(!allowed.has(action))throw Error('Unknown action');if(action==='ssh.terminal')return request(action,args).then(r=>new Promise((resolve,reject)=>{const p=spawn('konsole',r.args,{stdio:'ignore',detached:true});p.once('error',reject);p.once('spawn',()=>{p.unref();resolve({opened:true});});}));return request(action,args);});
 ipcMain.handle('login',e=>{check(e);return new Promise((resolve,reject)=>{const env={...process.env};delete env.GH_PROMPT_DISABLED;const p=spawn('konsole',['-e','gh','auth','login','--hostname','github.com','--git-protocol','https','--web'],{stdio:'ignore',detached:true,env});p.once('error',reject);p.once('spawn',()=>{p.unref();resolve();});});});
 ipcMain.handle('orchestraPanel',e=>{check(e);return shell.openExternal('https://orchestra.isdarkline.ru/');});
 ipcMain.handle('cloudConsole',e=>{check(e);return shell.openExternal('https://timeweb.cloud/my/');});
 ipcMain.handle('portal',e=>{check(e);return shell.openExternal('https://timeweb.cloud/my/api-keys');});
 ipcMain.handle('script',async e=>{check(e);const r=await dialog.showOpenDialog(win,{properties:['openFile'],filters:[{name:'Bash',extensions:['sh']}]});return r.canceled?'':r.filePaths[0];});
 win=new BrowserWindow({width:1320,height:900,minWidth:900,minHeight:650,backgroundColor:'#101016',icon:path.join(__dirname,'icon.png'),webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true}});
 win.webContents.setWindowOpenHandler(()=>({action:'deny'}));win.webContents.on('will-navigate',e=>e.preventDefault());win.loadFile(path.join(root,'dist','index.html'));
});app.on('window-all-closed',()=>app.quit());app.on('before-quit',()=>{backend?.stdin.end();});

}
