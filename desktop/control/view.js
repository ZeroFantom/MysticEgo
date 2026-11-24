import {initEffects} from './effects.js';
const $=id=>document.getElementById(id);
const center=window.center;
initEffects($('design'));
const message=$('message');
let initialized=false;
function status(element,ok){element.textContent=ok?'Доступен':'Ожидание';element.classList.toggle('online',ok)}
async function refresh(){
  const data=await center.snapshot();
  if(!initialized){$('host').value=data.settings.host;$('lan').checked=data.settings.lan;initialized=true}
  status($('auth'),data.health.auth);status($('orchestra'),data.health.orchestra);
  status($('state'),data.health.auth&&data.health.orchestra);
  $('state').textContent=data.health.auth&&data.health.orchestra?'Сервер работает':data.running?'Службы запускаются':'Сервер остановлен';
  $('start').disabled=data.running||data.health.orchestra;$('stop').disabled=!data.owned;
  $('open-orchestra').disabled=$('open-audit').disabled=!data.health.orchestra;
  const text=data.logs.join('\n');if($('logs').textContent!==text){$('logs').textContent=text;$('logs').scrollTop=$('logs').scrollHeight}
}
async function run(action){
  message.textContent='';
  try{const result=await action();if(typeof result==='string')message.textContent=result;await refresh()}
  catch(error){message.textContent=error.message}
}
$('start').onclick=()=>run(center.start);
$('stop').onclick=()=>run(center.stop);
$('open-orchestra').onclick=()=>run(()=>center.link('orchestra'));
$('open-audit').onclick=()=>run(()=>center.link('audit'));
$('logs-folder').onclick=()=>run(()=>center.link('folder'));
$('copy-login').onclick=()=>run(()=>center.credentials('login'));
$('copy-password').onclick=()=>run(()=>center.credentials('password'));
$('show-password').onclick=()=>run(async()=>{
  const field=$('password');const show=field.type==='password';
  field.value=show?await center.credentials('show'):'';
  field.type=show?'text':'password';$('show-password').textContent=show?'Скрыть':'Показать';
});
$('settings').onsubmit=event=>{
  event.preventDefault();void run(async()=>{await center.save({host:$('host').value,lan:$('lan').checked});return 'Параметры сохранены.'});
};
refresh();
window.setInterval(()=>refresh().catch(()=>{}),1800);
