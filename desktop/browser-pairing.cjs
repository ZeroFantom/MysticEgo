'use strict';
// Local opt-in discovery and native confirmation. Never expose an existing token or account name to the web.
const http=require('node:http');
const fs=require('node:fs');
const path=require('node:path');
const {Menu,dialog,app}=require('electron');
const ports=Object.freeze({AuditDesktop:19471,AgentDesktop:19472,LocalDesktop:19473,CloudDesk:19474});
const valid=/^[a-f0-9]{64}$/;
function origin(value){try{const u=new URL(value);return u.protocol==='https:'&&u.origin===value&&!u.username&&!u.password?u.origin:null}catch{return null}}
async function readBody(req){
 let size=0;const chunks=[];
 for await(const c of req){size+=c.length;if(size>1024)throw Error('Body too large');chunks.push(c)}
 return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}
function installBrowserPairing({product,window,getSession,approve}){
 const port=ports[product];if(!port)throw Error('Unknown product');
 const preference=path.join(app.getPath('userData'),'browser-pairing-consent.json');
 let enabled=false,server=null,prompt=false,lastPrompt=0;
 try{enabled=JSON.parse(fs.readFileSync(preference,'utf8')).enabled===true}catch{}
 const stop=()=>{server?.close();server=null};
 function start(){
  if(server||!enabled)return;
  const instance=http.createServer({requestTimeout:8000,headersTimeout:5000,maxHeaderSize:4096},async(req,res)=>{
   const finish=(status,data)=>{if(res.destroyed)return;res.writeHead(status,{'Content-Type':'application/json','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});res.end(JSON.stringify(data))};
   if(!enabled||req.headers.host!=='127.0.0.1:'+port||!origin(req.headers.origin)||!['/availability','/approve'].includes(req.url)){finish(404,{});return}
   try{
    const current=await getSession();
    if(!current||origin(current.origin)!==req.headers.origin){finish(404,{});return}
    res.setHeader('Access-Control-Allow-Origin',current.origin);res.setHeader('Vary','Origin');
    if(req.method==='OPTIONS'){
     res.setHeader('Access-Control-Allow-Methods','GET, POST, OPTIONS');res.setHeader('Access-Control-Allow-Headers','Content-Type');
     if(req.headers['access-control-request-private-network']==='true')res.setHeader('Access-Control-Allow-Private-Network','true');
     res.writeHead(204);res.end();return;
    }
    if(req.method==='GET'&&req.url==='/availability'){finish(200,{available:true,product});return}
    if(req.method!=='POST'||req.url!=='/approve'||!/^application\/json(?:;|$)/i.test(req.headers['content-type']||'')||prompt||Date.now()-lastPrompt<10000){finish(409,{});return}
    const body=await readBody(req);
    if(typeof body?.challenge!=='string'||!valid.test(body.challenge)){finish(400,{});return}
    if(prompt||Date.now()-lastPrompt<10000){finish(409,{});return}
    prompt=true;lastPrompt=Date.now();
    try{
     const parent=window();if(!parent||parent.isDestroyed()){finish(409,{});return}
     const answer=await dialog.showMessageBox(parent,{type:'question',title:'Вход в браузер MysticEgo',message:'Разрешить вход в браузер под текущим аккаунтом?',detail:'Сайт: '+current.origin+'\nПриложение: '+product+'\n'+(current.login?'Аккаунт: '+current.login+'\n':'')+'Подтверждайте только запрос, который вы сами только что создали.',buttons:['Отмена','Разрешить вход'],defaultId:0,cancelId:0,noLink:true});
     if(answer.response!==1||!enabled||res.destroyed){finish(403,{});return}
     const latest=await getSession();
     if(!latest||latest.origin!==current.origin||latest.identity!==current.identity){finish(409,{});return}
     const code=await approve(latest,body.challenge);
     if(!valid.test(code||'')){finish(502,{});return}
     finish(200,{code});
    }finally{prompt=false}
   }catch{finish(503,{})}
  });
  instance.maxConnections=12;
  instance.on('error',()=>{if(server===instance)server=null});
  instance.listen(port,'127.0.0.1');server=instance;
 }
 function set(value){
  enabled=value===true;
  try{fs.mkdirSync(path.dirname(preference),{recursive:true});fs.writeFileSync(preference,JSON.stringify({enabled}),{mode:0o600})}catch{}
  if(enabled)start();else stop();
 }
 const previous=Menu.getApplicationMenu();
 // Native menu is intentionally visible so sharing can be disabled without a web request.
 const menu=Menu.buildFromTemplate([{label:'MysticEgo',submenu:[{label:'Предлагать вход в браузере',type:'checkbox',checked:enabled,click:item=>set(item.checked)},{role:'quit'}]}]);
 if(previous?.items?.length){previous.append(menu.items[0]);Menu.setApplicationMenu(previous)}else Menu.setApplicationMenu(menu);
 const parent=window();parent?.setAutoHideMenuBar(false);parent?.setMenuBarVisibility(true);
 if(enabled)start();
 app.once('before-quit',stop);
 return {stop,set};
}
async function approveSession(session,challenge){
 const response=await fetch(session.origin+'/api/auth/desktop/approve',{method:'POST',headers:{'Content-Type':'application/json'},redirect:'error',cache:'no-store',signal:AbortSignal.timeout(12000),body:JSON.stringify({refreshToken:session.refreshToken,challenge})});
 if(!response.ok)throw Error('Desktop approval failed');
 return (await response.json()).code;
}
module.exports={installBrowserPairing,approveSession};
