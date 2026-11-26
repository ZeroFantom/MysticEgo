import {app,dialog} from 'electron';
import {spawn} from 'node:child_process';
import {existsSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import {getDotnet} from '../launch-runtime.mjs';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
app.whenReady().then(()=>{
 const desk=path.join(root,'tools','MysticEgo.CloudDesk');
 if(!existsSync(path.join(desk,'dist','index.html'))){dialog.showErrorBox('CloudDesk','Локальный центр теперь входит в CloudDesk. Скачайте его полный установщик из GitHub Releases.');app.quit();return;}
 const env={...process.env,CLOUDDESK_DOTNET:getDotnet(root),CLOUDDESK_BUNDLE_ROOT:root};delete env.ELECTRON_RUN_AS_NODE;
 const args=[...(process.platform==='linux'?['--disable-setuid-sandbox']:[]),desk];
 const child=spawn(process.execPath,args,{cwd:desk,env,stdio:'ignore',detached:true,windowsHide:true});
 child.once('error',e=>{dialog.showErrorBox('CloudDesk',e.message);app.quit();});
 child.once('spawn',()=>{child.unref();app.quit();});
});
