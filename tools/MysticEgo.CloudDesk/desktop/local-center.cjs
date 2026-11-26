'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),net=require('node:net');
const {spawn}=require('node:child_process'),{createInterface}=require('node:readline');
const {randomUUID}=require('node:crypto');
function legacyData(home,env=process.env){return path.join(env.LOCALAPPDATA||env.XDG_DATA_HOME||path.join(home,'.local','share'),'MysticEgo','BETA7-Lab','center')}
function validSettings(value){
 const host=String(value?.host??'').trim(),lan=value?.lan===true;
 if(host&&(host.length>253||!(net.isIP(host)===4||host.split('.').every(x=>/^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/i.test(x)))))throw Error('Введите DNS-имя или IPv4.');
 if(host&&!lan&&host!=='127.0.0.1'&&host.toLowerCase()!=='localhost')throw Error('Для сетевого адреса явно разрешите тестовый HTTP в локальной сети.');
 return {host:lan?host:'',lan,localModel:value?.localModel===true};
}
async function probe(port,route,field,value){try{const r=await fetch('http://127.0.0.1:'+port+route,{redirect:'error',signal:AbortSignal.timeout(1500)});return r.ok&&(await r.json())[field]===value}catch{return false}}
async function occupied(port){return new Promise(resolve=>{const s=net.createConnection({host:'127.0.0.1',port});const end=v=>{s.destroy();resolve(v)};s.setTimeout(800,()=>end(true));s.once('connect',()=>end(true));s.once('error',e=>end(e.code!=='ECONNREFUSED'))})}
async function regular(file){try{return (await fs.lstat(file)).isFile()}catch{return false}}
function launch(exe,args,root,log,env){
 const child=spawn(exe,args,{cwd:root,env,windowsHide:true,stdio:['pipe','pipe','pipe']});
 child.stdin.on('error',()=>{});
 for(const stream of [child.stdout,child.stderr])createInterface({input:stream,crlfDelay:Infinity}).on('line',line=>log(line.slice(0,1600)));
 const done=new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',(code,signal)=>code===0?resolve():reject(Error('Локальный процесс завершился: '+(code??signal))))});
 return {child,done};
}
function createLocalCenter({root,dotnet,home,data=legacyData(home),env=process.env,openExternal,openPath,copy,probeService=probe,isOccupied=occupied,spawnService=launch}){
 let proc=null,phase='idle',error='',closing=false,queue=Promise.resolve(),secrets=new Set(),settings={host:'',lan:false,localModel:false};
 const logs=[],config=path.join(data,'launch-settings.json'),installer=path.join(root,'runtime','app','bin','MysticEgo.Installer.dll');
 function log(line){let s=String(line);for(const secret of secrets)if(secret)s=s.split(secret).join('[скрыто]');logs.push(new Date().toLocaleTimeString('ru-RU')+'  '+s);if(logs.length>180)logs.shift()}
 const run=fn=>{const next=queue.then(fn);queue=next.catch(()=>{});return next};
 async function health(){const [auth,orchestra]=await Promise.all([probeService(5400,'/health','service','MysticEgo.Authorization.Service'),probeService(5500,'/health/live','kind','Orchestra')]);return {auth,orchestra}}
 let ready;
 function initialize(){return ready??=(async()=>{
  try{if(!(await fs.lstat(data)).isDirectory())throw Error('Небезопасная папка локальных данных.')}catch(e){if(e.code!=='ENOENT')throw e;await fs.mkdir(data,{recursive:true,mode:0o700})}
  if(process.platform!=='win32')await fs.chmod(data,0o700);
  if(await regular(config))try{const old=JSON.parse(await fs.readFile(config,'utf8'));settings=validSettings({...old,host:old.lan===true?old.host:''})}catch(e){log('Настройки не прочитаны: '+e.message)}
 })()}
 async function available(){const bin=path.dirname(installer);return (await Promise.all([installer,path.join(bin,'Api.Auth.dll'),path.join(bin,'Api.Orchestra.dll'),path.join(root,'runtime/app/web/orchestra/index.html'),path.join(root,'runtime/app/web/audit/index.html')].map(regular))).every(Boolean)}
 async function snapshot(){await initialize();const [h,installed]=await Promise.all([health(),available()]);return {health:h,installed,settings:{...settings},phase,owned:!!proc,data,logs:[...logs],error,credentialsAvailable:await regular(path.join(data,'owner-password.txt'))}}
 function start(){return run(async()=>{
  await initialize();if(closing)throw Error('CloudDesk завершает работу.');if(proc)return;error='';
  const h=await health();if(h.auth&&h.orchestra){log('Подключено к внешнему локальному центру.');return}
  if(h.auth||h.orchestra||(await Promise.all([5400,5500].map(isOccupied))).some(Boolean))throw Error('Порты 5400/5500 заняты. CloudDesk не останавливает чужие процессы.');
  if(!await available())throw Error('Для локального центра нужен полный комплект CloudDesk из релиза (runtime/app).');
  const passwordPath=path.join(data,'owner-password.txt');if(await regular(passwordPath))secrets.add((await fs.readFile(passwordPath,'utf8')).trim());
  const childEnv={...env,MYSTICEGO_DOTNET:dotnet,MYSTICEGO_GUI_SESSION:'1',MYSTICEGO_CENTER_PUBLIC_HOST:settings.lan?settings.host:'',MYSTICEGO_ALLOW_LAN_HTTP:String(settings.lan)};
  delete childEnv.MYSTICEGO_CENTER_AUTH_URL;delete childEnv.MYSTICEGO_CENTER_ORCHESTRA_URL;
  const args=[installer,'--lab','--no-browser','--parent-stdin','--root',root,'--output',data];if(!settings.localModel)args.push('--no-lm');
  phase='starting';
  try{const current=spawnService(dotnet,args,root,log,childEnv);proc=current;current.done.then(()=>log('Локальный центр остановлен.'),e=>{error=e.message;log(e.message)}).finally(()=>{if(proc===current){proc=null;phase='idle'}});log('Запущен штатный установщик локального центра.')}catch(e){phase='idle';error=e.message;throw e}
 })}
 function stop(){return run(async()=>{const current=proc;if(!current)return;phase='stopping';current.child.stdin.end('stop\n');let timer;try{await Promise.race([current.done.catch(()=>{}),new Promise((_,reject)=>{timer=setTimeout(()=>reject(Error('Локальный центр ещё завершает работу. Повторите остановку.')),30000)})])}finally{clearTimeout(timer);if(proc===current&&current.child.exitCode!=null){proc=null;phase='idle'}}})}
 function save(value){return run(async()=>{await initialize();const h=await health();if(proc||h.auth||h.orchestra)throw Error('Остановите локальный центр перед изменением параметров.');const next=validSettings(value),temp=config+'.'+randomUUID()+'.tmp';try{await fs.writeFile(temp,JSON.stringify(next,null,2),{mode:0o600,flag:'wx'});await fs.rename(temp,config);settings=next}finally{await fs.rm(temp,{force:true})}log('Параметры сохранены.')})}
 async function credentials(mode){await initialize();if(mode==='login'){copy('developer');return 'Логин скопирован'}if(mode!=='show'&&mode!=='password')throw Error('Неизвестное действие.');const file=path.join(data,'owner-password.txt');if(!await regular(file))throw Error('Пароль ещё не создан. Сначала запустите локальный центр.');const secret=(await fs.readFile(file,'utf8')).trim();if(!secret)throw Error('Файл пароля пуст.');secrets.add(secret);if(mode==='show')return secret;copy(secret);return 'Пароль скопирован'}
 async function link(label){await initialize();if(label==='folder'){const err=await openPath(data);if(err)throw Error(err);return}if(!['orchestra','audit'].includes(label))throw Error('Неизвестная панель.');if(!(await health()).orchestra)throw Error('Локальная Orchestra ещё не доступна.');return openExternal('http://127.0.0.1:5500'+(label==='audit'?'/audit-portal/':'/'))}
 async function close(){closing=true;try{await stop()}catch(e){closing=false;throw e}}
 return {snapshot,start,stop,save,credentials,link,close};
}
module.exports={createLocalCenter,legacyData,validSettings};
