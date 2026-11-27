const fs=require('node:fs/promises');
const {createReadStream,createWriteStream}=require('node:fs');
const path=require('node:path');
const {createHash,randomUUID}=require('node:crypto');
const {Readable,Transform}=require('node:stream');
const {pipeline}=require('node:stream/promises');
const {spawn,execFile}=require('node:child_process');
const {promisify}=require('node:util');
const run=promisify(execFile);
const repo='ZeroFantom/MysticEgo';
const products=new Set(['CloudDesk','AgentDesktop','AuditDesktop','LocalDesktop']);
const maxSize=2_000_000_000;
const automaticProducts=new Set(['AgentDesktop','LocalDesktop']);
const requiresConfirmation=product=>!automaticProducts.has(product);
const releaseNotes=value=>typeof value==='string'?value.trim().slice(0,12000):'';
const notesPath=app=>path.join(app.getPath('userData'),'release-highlights.json');
const seenPath=app=>path.join(app.getPath('userData'),'release-highlights-seen.json');
async function saveJson(file,value){
 await fs.mkdir(path.dirname(file),{recursive:true,mode:0o700});
 const temp=file+'.'+randomUUID()+'.tmp';
 try{await fs.writeFile(temp,JSON.stringify(value),{mode:0o600,flag:'wx'});await fs.rename(temp,file)}
 finally{await fs.rm(temp,{force:true}).catch(()=>{})}
}
async function readJson(file){
 try{if((await fs.stat(file)).size>65000)return null;return JSON.parse(await fs.readFile(file,'utf8'))}catch{return null}
}
function createReleaseHighlights({app,root,product}){
 let current=null;
 async function load(){
  const installed=installation(root,product,process.platform)?.installed;
  if(!installed)return;
  if((await readJson(seenPath(app)))?.tag===installed)return;
  const staged=await readJson(notesPath(app));
  const bundled=await readJson(path.join(root,'desktop','release-highlights.json'));
  const source=[staged,bundled].find(value=>value?.tag===installed&&releaseNotes(value.notes));
  if(source)current={tag:installed,notes:releaseNotes(source.notes)};
 }
 async function dismiss(tag){
  if(!current||current.tag!==tag)return false;
  await saveJson(seenPath(app),{tag});current=null;return true;
 }
 return {load,dismiss,get:()=>current};
}
const tagPattern=/^v(\d+)\.(\d+)\.(\d+)-beta\.(\d+)-r(\d+(?:\.\d+){0,7})$/;
function version(tag){
 const m=tagPattern.exec(tag??'');
 if(!m)return null;
 const values=[...m.slice(1,5),...m[5].split('.')].map(Number);
 if(values.some(v=>!Number.isSafeInteger(v)))return null;
 return values;
}
function compare(a,b){
 const x=version(a),y=version(b);
 if(!x||!y)return null;
 for(let i=0;i<Math.max(x.length,y.length);i++){
  const diff=(x[i]??0)-(y[i]??0);
  if(diff)return Math.sign(diff);
 }
 return 0;
}
function select(releases,product,platform,installed){
 if(!products.has(product)||!['linux','win32'].includes(platform)||!version(installed)||!Array.isArray(releases))return null;
 const filename='MysticEgo-'+product+(platform==='win32'?'-win-x64.exe':'-linux-x64.run');
 const found=[];
 for(const release of releases){
  if(release?.draft!==false||!release.published_at||compare(release.tag_name,installed)!==1)continue;
  // Keep the beta train explicit: never silently switch to another channel.
  if(version(release.tag_name).slice(0,4).join('.')!==version(installed).slice(0,4).join('.'))continue;
  const asset=release.assets?.find(a=>a.name===filename&&a.state==='uploaded'&&Number.isSafeInteger(a.size)&&a.size>0&&a.size<=maxSize&&/^sha256:[a-f0-9]{64}$/.test(a.digest??''));
  if(asset)found.push({tag:release.tag_name,filename,size:asset.size,digest:asset.digest.slice(7),notes:releaseNotes(release.body)});
 }
 return found.sort((a,b)=>compare(b.tag,a.tag))[0]??null;
}
const allowed=new Set(['github.com','release-assets.githubusercontent.com','objects.githubusercontent.com']);
async function downloadResponse(url,fetcher=fetch){
 for(let n=0;n<6;n++){
  const u=new URL(url);
  if(u.protocol!=='https:'||!allowed.has(u.hostname)||u.port||u.username||u.password)throw Error('Untrusted release URL');
  const r=await fetcher(u,{redirect:'manual',signal:AbortSignal.timeout(20*60_000),headers:{'User-Agent':'MysticEgo-Desktop-Updater'}});
  if([301,302,303,307,308].includes(r.status)){
   const location=r.headers.get('location');
   await r.body?.cancel();
   if(!location)throw Error('Missing release redirect');
   url=new URL(location,u).href;
   continue;
  }
  if(!r.ok||!r.body)throw Error('Release download HTTP '+r.status);
  return r;
 }
 throw Error('Too many release redirects');
}
async function verified(file,candidate){
 try{
  const info=await fs.lstat(file);
  if(!info.isFile()||info.size!==candidate.size)return false;
  const hash=createHash('sha256');
  for await(const chunk of createReadStream(file))hash.update(chunk);
  return hash.digest('hex')===candidate.digest;
 }catch{return false}
}
async function stage(candidate,folder,fetcher=fetch){
 await fs.mkdir(folder,{recursive:true,mode:0o700});
 const file=path.join(folder,candidate.tag+'-'+candidate.filename);
 if(await verified(file,candidate))return file;
 const temp=path.join(folder,'.'+randomUUID()+'.part');
 try{
  const url='https://github.com/'+repo+'/releases/download/'+encodeURIComponent(candidate.tag)+'/'+candidate.filename;
  const response=await downloadResponse(url,fetcher);
  const hash=createHash('sha256');let bytes=0;
  const limit=new Transform({transform(chunk,_encoding,done){
   bytes+=chunk.length;
   if(bytes>candidate.size)return done(Error('Release exceeds declared size'));
   hash.update(chunk);done(null,chunk);
  }});
  await pipeline(Readable.fromWeb(response.body),limit,createWriteStream(temp,{flags:'wx',mode:0o600}));
  if(bytes!==candidate.size||hash.digest('hex')!==candidate.digest)throw Error('Release checksum mismatch');
  await fs.rm(file,{force:true});
  await fs.rename(temp,file);
  return file;
 }finally{await fs.rm(temp,{force:true}).catch(()=>{})}
}
function installation(root,product,platform){
 if(!products.has(product)||!['linux','win32'].includes(platform))return null;
 const installed=path.basename(root),parent=path.dirname(root);
 if(!version(installed)||path.basename(parent)!==product)return null;
 return {installed,parent};
}
function launchAfterExit(app,target){
 const env={...process.env};
 for(const key of ['CLOUDDESK_BUNDLE_ROOT','CLOUDDESK_DOTNET','CLOUDDESK_ELECTRON','CLOUDDESK_RUNTIME_DIR','MYSTICEGO_DOTNET','ELECTRON_RUN_AS_NODE'])delete env[key];
 app.once('quit',()=>{
  let child;
  if(process.platform==='win32'){
   const quote=s=>"'"+s.replaceAll("'","''")+"'";
   const script='Wait-Process -Id '+process.pid+' -ErrorAction SilentlyContinue; Start-Process -FilePath '+quote(path.join(target,'launch.cmd'));
   child=spawn('powershell.exe',['-NoProfile','-NonInteractive','-EncodedCommand',Buffer.from(script,'utf16le').toString('base64')],{detached:true,stdio:'ignore',windowsHide:true,env});
  }else{
   child=spawn('/bin/bash',['-c','for i in {1..100}; do kill -0 "$1" 2>/dev/null || break; sleep 0.2; done; kill -0 "$1" 2>/dev/null && exit 1; exec "$2"','update',String(process.pid),path.join(target,'launch.sh')],{detached:true,stdio:'ignore',env});
  }
  child.on('error',e=>console.warn('Update restart:',e.message));child.unref();
 });
}
async function confirmUpdate({product,dialog,win,candidate}){
 if(!requiresConfirmation(product))return true;
 const changes=releaseNotes(candidate.notes).slice(0,5000)||'Описание изменений не опубликовано.';
 const result=await dialog.showMessageBox(win,{type:'info',title:'Обновление '+product,message:'Версия '+candidate.tag+' скачана и проверена.',detail:'Что нового:\n'+changes+'\n\nПрофили сохраняются. Для установки приложение будет перезапущено.',buttons:['Установить и перезапустить','Позже'],defaultId:1,cancelId:1,noLink:true});
 return result.response===0;
}
function startDesktopUpdates({app,dialog,root,product,window,canRestart=()=>true}){
 if(process.arch!=='x64')return;
 const platform=process.platform,local=installation(root,product,platform);
 if(!local)return; // source checkout / legacy ZIP: no silent installation.
 let busy=false,offered=null;
 const folder=path.join(app.getPath('userData'),'verified-updates',product);
 async function check(){
  if(busy)return;busy=true;
  try{
   const response=await fetch('https://api.github.com/repos/'+repo+'/releases?per_page=30',{redirect:'error',signal:AbortSignal.timeout(15000),headers:{'User-Agent':'MysticEgo-Desktop-Updater','Accept':'application/vnd.github+json'}});
   if(!response.ok)return;
   const candidate=select(await response.json(),product,platform,local.installed);
   if(!candidate||offered===candidate.tag)return;
   const file=await stage(candidate,folder);
   const win=window?.();
   if(!win||win.isDestroyed())return;
   if(!canRestart())return;
   if(requiresConfirmation(product))offered=candidate.tag;
   if(!await confirmUpdate({product,dialog,win,candidate}))return;
   if(!await verified(file,candidate))throw Error('Installer verification failed');
   if(platform==='win32')await run(file,['/S'],{timeout:20*60_000,windowsHide:true});
   else await run('/bin/bash',[file,'--install-only'],{timeout:20*60_000});
   const target=path.join(local.parent,candidate.tag);
   const launcher=path.join(target,platform==='win32'?'launch.cmd':'launch.sh');
   if(!(await fs.lstat(launcher)).isFile())throw Error('New installation launcher missing');
   await saveJson(notesPath(app),{tag:candidate.tag,notes:candidate.notes});
   if(!canRestart()){offered=null;return;}
   await fs.rm(file,{force:true});
   launchAfterExit(app,target);
   app.quit();
  }catch(error){console.warn('Desktop update:',String(error?.message??error));}
  finally{busy=false}
 }
 const first=setTimeout(()=>void check(),5000+Math.random()*5000);
 const timer=setInterval(()=>void check(),30*60_000);
 app.once('before-quit',()=>{clearTimeout(first);clearInterval(timer)});
}
module.exports={version,compare,select,downloadResponse,verified,stage,installation,requiresConfirmation,releaseNotes,createReleaseHighlights,confirmUpdate,startDesktopUpdates};
