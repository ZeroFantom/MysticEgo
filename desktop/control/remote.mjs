import fs from 'node:fs/promises';
import {createReadStream} from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import net from 'node:net';
import {createHash, randomUUID} from 'node:crypto';
import {createRequire} from 'node:module';
import {pipeline} from 'node:stream/promises';
import {launch} from './center.mjs';
const {Client}=createRequire(import.meta.url)('../../runtime/control-deps/ssh.cjs');
const ports={auth:5400,orchestra:5500,global:5100,local:5200};
const upstream=20000;
const presets=new Set(['agent','site','global','home']);
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
function valid(target){
  if(!target || typeof target.host!=='string' || !/^[a-zA-Z0-9][a-zA-Z0-9.:-]{0,252}$/.test(target.host) || !Number.isInteger(target.port) || target.port<1 || target.port>65535)throw Error('Проверьте адрес и порт SSH.');
  if(!/^[\p{L}\p{N}_.@\\-]{1,100}$/u.test(target.user) || !/^[\p{L}\p{N} _.\-]{1,64}$/u.test(target.name) || !target.name.trim())throw Error('Проверьте имя компьютера и логин SSH.');
  if(typeof target.user!=='string'||typeof target.name!=='string')throw Error('Введите имя компьютера и логин.');
  if(!presets.has(target.preset)||!['windows','linux'].includes(target.platform))throw Error('Выберите систему и назначение.');
}
function callback(register){return new Promise((resolve,reject)=>register((err,result)=>err?reject(err):resolve(result)));}
async function exec(client,command){
  const s=await callback(cb=>client.exec(command,cb));let out='',err='';
  s.on('data',d=>out=(out+d).slice(-3000));s.stderr.on('data',d=>err=(err+d).slice(-3000));
  return new Promise((resolve,reject)=>{s.on('error',reject);s.on('close',code=>code===0?resolve(out):reject(Error((err||out||'Команда SSH завершилась с кодом '+code).trim())));});
}
function bridge(a,b){
  a.on('error',()=>b.destroy());b.on('error',()=>a.destroy());
  a.pipe(b).pipe(a);
}
async function upload(sftp,src,dst,progress){
  const source=createReadStream(src);
  if(progress){
    const total=(await fs.stat(src)).size;let received=0,last=0;
    source.on('data',chunk=>{received+=chunk.length;const pct=Math.floor(received/total*10)*10;if(pct>last){progress(pct);last=pct;}});
  }
  await pipeline(source,sftp.createWriteStream(dst,{mode:0o600}));
}
const knownId=/^[a-f0-9-]{36}$/i;
export class RemoteFleet{
  constructor(root,data,center,log,confirmKey,store){
    this.root=root;this.data=data;this.center=center;this.log=log;this.confirmKey=confirmKey;
    this.entries=store??{};this.live=new Map();this.busy=new Set();this.packages=new Map();
    this.cache=null;this.writes=Promise.resolve();this.closing=false;this.pending=new Map();
  }
  async load(){try{this.entries=JSON.parse(await fs.readFile(path.join(this.data,'desktop-stations.json'),'utf8'));}catch(e){if(e.code!=='ENOENT')throw e;}}
  async save(){
    const snapshot=JSON.stringify(this.entries,null,2);
    this.writes=this.writes.catch(()=>{}).then(async()=>{
      await fs.mkdir(this.data,{recursive:true,mode:0o700});
      const name=path.join(this.data,'desktop-stations.json');
      await fs.writeFile(name+'.new',snapshot,{mode:0o600});await fs.rename(name+'.new',name);
    });return this.writes;
  }
  list(){return Object.values(this.entries).map(({key,id,host,port,user,name,preset,platform,localParent,globalParent,ai})=>({key,id,host,port,user,name,preset,platform,localParent,globalParent,ai,connected:this.live.has(key),model:this.live.get(key)?.model}));}
  async pack(platform){
    if(!this.packages.has(platform)){
      this.cache??=fs.mkdtemp(path.join(os.tmpdir(),'mysticego-release-'));
      const build=(async()=>{
        const dest=path.join(await this.cache,platform+'.tgz');
        const args=['-czf',dest,'-C',this.root,'runtime/app','runtime/vision',platform==='windows'?'runtime/win-x64':'runtime/linux-x64.tar.gz'];
        await launch(process.platform==='win32'?path.join(process.env.SystemRoot,'System32','tar.exe'):'tar',args,this.root,this.log).done;
        const hash=createHash('sha256');for await(const part of createReadStream(dest))hash.update(part);
        return {dest,digest:hash.digest('hex')};
      })().catch(e=>{this.packages.delete(platform);throw e;});
      this.packages.set(platform,build);
    }
    return this.packages.get(platform);
  }
  async connect(target,auth){
    if(this.closing)throw Error('Центр закрывается.');
    valid(target);
    const existing=target.key?this.entries[target.key]:Object.values(this.entries).find(x=>x.host===target.host && x.port===target.port && x.user===target.user);
    if(target.key && !existing)throw Error('Профиль не найден.');
    if(existing && (existing.platform!==target.platform||existing.preset!==target.preset||existing.name!==target.name))throw Error('У этого SSH-узла уже есть профиль. Выберите его в списке.');
    const entry=existing?{...existing,ai:!!target.ai,localParent:target.localParent||'',globalParent:target.globalParent||''}:{key:randomUUID(),host:target.host,port:target.port,user:target.user,name:target.name,preset:target.preset,platform:target.platform,ai:!!target.ai,localParent:target.localParent||'',globalParent:target.globalParent||''};
    const endpoint=entry.user+'@'+entry.host+':'+entry.port;
    if(this.live.has(entry.key))throw Error('Соединение с этим узлом уже открыто.');
    if(this.busy.has(endpoint))throw Error('Подключение уже выполняется.');
    this.busy.add(endpoint);
    const client=new Client();this.pending.set(entry.key,client);
    let session;let connected=false;
    const report=msg=>this.log(entry.name+' · '+msg);
    try{
      for(const [kind,key] of [['local',entry.localParent],['global',entry.globalParent]]){
        if(key && (!this.live.has(key)||!this.entries[key]?.profile?.credentials.some(x=>x.kind===kind)))throw Error('Сначала подключите родительский узел '+kind+'.');
      }
      const options={host:entry.host,port:entry.port,username:entry.user,readyTimeout:35000,keepaliveInterval:10000,keepaliveCountMax:3,
        hostVerifier:(raw,answer)=>{
          const pin='SHA256:'+createHash('sha256').update(raw).digest('base64').replace(/=+$/,'');
          if(entry.fingerprint){if(pin!==entry.fingerprint)report('Ключ SSH изменился. Соединение отклонено.');answer(pin===entry.fingerprint);return;}
          Promise.resolve(this.confirmKey(endpoint,pin)).then(ok=>{if(ok)entry.fingerprint=pin;answer(!!ok);},()=>answer(false));
        }};
      if(auth.password && !auth.keyPath){
        options.tryKeyboard=true;
        client.on('keyboard-interactive',(_name,_instructions,_lang,prompts,finish)=>{
          const single=prompts.length===1 && !prompts[0].echo && /password|пароль/i.test(prompts[0].prompt);
          finish(single?[auth.password]:[]);
        });
      }
      if(auth.keyPath){options.privateKey=await fs.readFile(auth.keyPath);if(auth.password)options.passphrase=auth.password;}
      else if(auth.password)options.password=auth.password;
      else if(process.env.SSH_AUTH_SOCK)options.agent=process.env.SSH_AUTH_SOCK;
      else if(process.platform==='win32')options.agent='\\\\.\\pipe\\openssh-ssh-agent';
      else throw Error('Введите пароль SSH или выберите приватный ключ.');
      report('Подключение к '+endpoint);
      await new Promise((resolve,reject)=>{
        client.on('error',e=>{report(e.message);reject(e);});
        client.once('close',()=>{reject(Error('SSH-соединение закрыто.'));if(this.live.get(entry.key)?.client===client)this.live.delete(entry.key);});
        client.once('ready',resolve);client.connect(options);
      });
      const sftp=await callback(cb=>client.sftp(cb));
      const win=entry.platform==='windows';
      const arch=await exec(client,win?'cmd.exe /d /c "echo %PROCESSOR_ARCHITECTURE% & where tar.exe"':'uname -sm && command -v tar');
      if(!(win?/AMD64/i:/Linux x86_64/i).test(arch))throw Error('Выбранная система не соответствует удалённой ОС или архитектуре x64.');
      const forwards=new Map();
      for(const [role,port] of Object.entries(ports)){
        if((entry.preset==='site'&&role==='local')||(entry.preset==='global'&&role==='global'))continue;
        const parent=role==='local'?entry.localParent:role==='global'?entry.globalParent:null;
        forwards.set(port+upstream,{port,parent});
      }
      client.on('tcp connection',(info,accept,reject)=>{
        const mapping=forwards.get(info.destPort);
        if(!mapping||info.destIP!=='127.0.0.1'){reject();return;}
        const peer=mapping.parent?this.live.get(mapping.parent)?.client:null;
        if(mapping.parent && !peer){reject();return;}
        const remote=accept();
        if(peer)peer.forwardOut('127.0.0.1',0,'127.0.0.1',mapping.port,(e,stream)=>{if(e)remote.destroy();else bridge(remote,stream);});
        else bridge(remote,net.connect(mapping.port,'127.0.0.1'));
      });
      for(const port of forwards.keys())await callback(cb=>client.forwardIn('127.0.0.1',port,cb));
      if(!entry.profile){
        entry.profile=await this.center.api('','POST',{name:entry.name,preset:entry.preset});
        entry.id=entry.profile.credentials[0].identityId;
      }
      this.entries[entry.key]=entry;await this.save();
      if(!knownId.test(entry.id))throw Error('Неверный идентификатор станции.');
      report('Подготовка пакета для '+(win?'Windows':'Linux'));
      const {dest,digest}=await this.pack(entry.platform);
      const relative='MysticEgo-Stations/'+entry.id;
      async function mkdir(name){try{await callback(cb=>sftp.mkdir(name,{mode:0o700},cb));}catch(e){const st=await callback(cb=>sftp.stat(name,cb));if(!st.isDirectory())throw e;}}
      await mkdir('MysticEgo-Stations');await mkdir(relative);
      const release=relative+'/r-'+digest;
      await mkdir(release);
      let ready=false;
      try {ready=(await callback(cb=>sftp.readFile(release+'/.ready','utf8',cb))).trim()===digest;}catch(e){if(e.code!==2)throw e;}
      if(!ready){
        report('Передача файлов и среды .NET по SFTP');
        await upload(sftp,dest,release+'/payload.tgz',p=>report('Передача пакета: '+p+'%'));
        if(win)await upload(sftp,path.join(this.root,'desktop','control','prepare-windows.cmd'),release+'/prepare.cmd');
        const cmd=win?'cmd.exe /d /c '+release.replaceAll('/','\\')+'\\prepare.cmd '+digest:
          'cd '+release+' && printf \'%s  payload.tgz\\n\' '+digest+' | sha256sum -c - && tar -xzf payload.tgz && mkdir -p runtime/linux-x64 && tar -xzf runtime/linux-x64.tar.gz -C runtime/linux-x64 && rm payload.tgz runtime/linux-x64.tar.gz';
        await exec(client,cmd);
        await callback(cb=>sftp.writeFile(release+'/.ready',digest,{mode:0o600},cb));
      } else report('Проверенный пакет уже размещён');
      // A partial upload never replaces the active profile.
      const profile=relative+'/station-'+entry.id+'.json';
      await callback(cb=>sftp.writeFile(profile+'.new',JSON.stringify(entry.profile),{mode:0o600},cb));
      const rename=win?'cmd.exe /d /c move /y '+(profile+'.new').replaceAll('/','\\')+' '+profile.replaceAll('/','\\'):'mv '+profile+'.new '+profile;
      await exec(client,rename);
      const flags=' --join '+(win?'..\\':'../')+'station-'+entry.id+'.json --upstream-offset '+upstream+' --parent-stdin'+(entry.ai?' --headless-lm':' --no-lm');
      const cmd=win?'cmd.exe /d /c "cd /d '+release.replaceAll('/','\\')+' && runtime\\win-x64\\dotnet.exe runtime\\app\\bin\\MysticEgo.Installer.dll'+flags+'"':
        'cd '+release+' && exec ./runtime/linux-x64/dotnet runtime/app/bin/MysticEgo.Installer.dll'+flags;
      session=await callback(cb=>client.exec(cmd,cb));
      const opened={client,session,model:entry.ai?'подготовка':null};this.live.set(entry.key,opened);
      const observe=(s)=>{
        s.setEncoding('utf8');let tail='';
        s.on('data',d=>{const lines=(tail+d).split(/[\r\n]+/);tail=lines.pop().slice(-2000);for(const line of lines){if(!line.trim())continue;report(line.trim().slice(-1500));if(line.includes('LM Studio: проверен ответ модели'))opened.model='готова';else if(line.includes('LM Studio: ошибка подготовки.'))opened.model='ошибка, см. журнал';}});
      };
      observe(session);observe(session.stderr);
      session.on('error',e=>report(e.message));
      session.on('close',code=>{if(this.live.get(entry.key)===opened)this.live.delete(entry.key);report('Сеанс завершён'+(code==null?'':' · '+code));client.end();});
      connected=true;
      report('Ожидание первого отчёта Orchestra');
      const since=Date.now()-1500;
      for(let i=0;i<75;i++){
        if(!this.live.has(entry.key))throw Error('Удалённый процесс завершился. Смотрите журнал.');
        const row=(await this.center.api('')).find(r=>r.id===entry.id);
        if(row?.report && Date.parse(row.report.seen)>=since && (!row.enabled || row.roles.every(r=>row.report.healthy.includes(r)))){
          report(row.enabled?'Роли на связи':'Роли остановлены в Orchestra');return entry.key;
        }
        await sleep(1000);
      }
      throw Error('SSH-сессия открыта, но за 75 секунд Orchestra не подтвердил все роли. Подробности в журнале.');
    }finally{
      this.busy.delete(endpoint);this.pending.delete(entry.key);
      if(!connected)client.end();
    }
  }
  async disconnect(key){
    const conn=this.live.get(key);
    if(!conn)return;
    const ended=new Promise(resolve=>conn.session.once('close',resolve));
    conn.session.end('stop\n');await Promise.race([ended,sleep(6000)]);
    conn.client.end();
  }
  async close(){
    this.closing=true;
    for(const [key,c] of this.pending)if(!this.live.has(key))c.end();
    await Promise.all([...this.live.keys()].map(k=>this.disconnect(k)));
    if(this.cache)await fs.rm(await this.cache,{recursive:true,force:true}).catch(()=>{});
  }
}
