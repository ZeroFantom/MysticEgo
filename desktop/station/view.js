import {initEffects} from '../control/effects.js';
const $=id=>document.getElementById(id);
const api=window.station;
let initialized=false,centralEdited=false,register=false;
initEffects($('design'));
async function refresh(){
  const state=await api.snapshot();
  const node=state.node,authorized=Boolean(state.actor);
  if(!initialized){$('allow').checked=state.features.allow;$('processes').value=state.features.processes;initialized=true}
  if(!centralEdited&&state.central){$('central').value=state.central;$('portal-lan').checked=state.lan}
  $('embedded').hidden=!state.embedded;$('workspace').hidden=state.embedded;
  $('identity').hidden=!authorized;$('login-box').hidden=authorized;$('identity-name').textContent=state.actor??'';
  $('state').textContent=state.online?'Узел в сети':state.running?'Узел запускается':node?'Узел остановлен':'Ожидание подключения';
  $('state').classList.toggle('online',state.online);
  $('agent-badge').classList.toggle('online',state.clubAvailable);
  $('agent-badge').textContent=state.beacon?'● '+state.beacon:'○ Agent';
  $('current').textContent=node?node.name+' · '+node.kind+' · '+node.url:'Agent подключается из комплекта Local. Для Global и Local используется конфигурация администратора.';
  const global=$('global-panel');
  const tag=JSON.stringify(state.globals);
  if(global.dataset.options!==tag){
    global.dataset.options=tag;
    global.replaceChildren(new Option('Мои организации Global',''),...state.globals.map(org=>new Option(org.name,org.id)));
  }
  global.disabled=!state.globals.length;
  $('import').hidden=!authorized||node?.kind==='agent';$('start').hidden=$('stop').hidden=!authorized;
  $('start').disabled=!node||state.running||state.online;$('stop').disabled=!state.running;
  $('panel').disabled=!state.online||node?.kind!=='agent'&&!authorized;
  $('companion').disabled=!state.online||!['audit','agent'].includes(node?.kind)||node?.kind==='audit'&&!authorized;
  $('home').disabled=node?.kind==='agent';
  $('folder').hidden=!authorized;
  $('allow').disabled=$('processes').disabled=state.running;$('record').disabled=state.recording;$('finish').disabled=!state.recording;
  $('apps').hidden=!authorized||Boolean(node&&!['audit','agent'].includes(node.kind));
  $('app-add').disabled=$('app-remove').disabled=state.running;
  $('app-list').textContent=Object.keys(state.features.apps||{}).join(' · ');
  $('automation').hidden=!authorized||state.platform!=='win32'||Boolean(node&&!['audit','agent'].includes(node.kind));
  const logs=state.logs.join('\n');if($('logs').textContent!==logs){$('logs').textContent=logs;$('logs').scrollTop=$('logs').scrollHeight}
}
async function run(action){
  $('message').textContent='';
  try{const result=await action();if(typeof result==='string')$('message').textContent=result;await refresh()}
  catch(error){$('message').textContent=error.message}
}
$('central').oninput=$('portal-lan').onchange=()=>{centralEdited=true};
$('register').onclick=()=>{
  register=!register;$('nickname-label').hidden=!register;$('nickname').required=register;
  $('login-title').textContent=register?'Создайте аккаунт Audit':'Войдите в MysticEgo';
  $('login-button').textContent=register?'Создать аккаунт':'Войти';
  $('register').textContent=register?'У меня есть аккаунт':'Создать аккаунт';
  $('password').autocomplete=register?'new-password':'current-password';
};
$('login-form').onsubmit=event=>{
  event.preventDefault();$('login-button').disabled=true;
  void run(async()=>{
    try{return await api.auth({login:$('login').value,password:$('password').value,register,nickname:$('nickname').value,central:$('central').value,lan:$('portal-lan').checked})}
    finally{$('password').value='';$('login-button').disabled=false}
  });
};
$('import').onclick=()=>run(api.import);
$('start').onclick=()=>run(()=>api.start({allow:$('allow').checked,processes:$('processes').value}));
$('stop').onclick=()=>run(api.stop);
$('panel').onclick=()=>run(api.panel);
$('home').onclick=()=>run(api.home);
$('global-panel').onchange=event=>{const id=event.currentTarget.value;event.currentTarget.value='';if(id)void run(()=>api.global(id))};
$('portal').onclick=()=>run(()=>api.portal($('central').value,$('portal-lan').checked));
$('back').onclick=()=>run(api.back);
$('logout').onclick=()=>run(api.logout);
$('companion').onclick=()=>run(api.companion);
$('app-add').onclick=()=>run(()=>api.app($('app-alias').value,false));
$('app-remove').onclick=()=>run(()=>api.app($('app-alias').value,true));
$('record').onclick=()=>run(()=>api.record($('record-process').value));
$('finish').onclick=()=>run(api['finish-record']);
$('folder').onclick=()=>run(api.folder);
refresh();window.setInterval(()=>refresh().catch(()=>{}),1600);
