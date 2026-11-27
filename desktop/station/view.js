import {initEffects} from '../control/effects.js';
import {applyPresentation,captureStatic,fonts,languages,t} from '../control/presentation.js';
const $=id=>document.getElementById(id);
const api=window.station;
let initialized=false,centralEdited=false,register=false;
let documents=null;
let documentRequest=0;
initEffects($('design'));
let prefs={language:'en',font:'modern'},owner=null,preferenceGeneration=0,loadingPreferences=false;
try{prefs={...prefs,...JSON.parse(localStorage.getItem('mysticego.desktop.guest.presentation')||'{}')}}catch{}
const host=$('presentation-controls');
const languageSelect=document.createElement('select'),fontSelect=document.createElement('select');
languageSelect.append(...languages.map(([id,label])=>new Option(label,id)));
fontSelect.append(...fonts.map(([id,label])=>new Option(label,id)));
for(const [caption,input] of [['Язык',languageSelect],['Шрифт',fontSelect]]){
 const label=document.createElement('label');label.textContent=caption;label.append(input);host.append(label);
}
captureStatic(document.body);
function applyPrefs(){applyPresentation(prefs);languageSelect.value=prefs.language;fontSelect.value=prefs.font;$('global-panel').dataset.options='';}
applyPrefs();
async function savePref(patch){
 const generation=preferenceGeneration;prefs={...prefs,...patch};applyPrefs();
 if(!owner){try{localStorage.setItem('mysticego.desktop.guest.presentation',JSON.stringify(prefs))}catch{};return}
 try{const result=await api.presentation(patch);if(generation===preferenceGeneration){prefs={...prefs,...result};applyPrefs();$('presentation-status').textContent=''}}
 catch{if(generation===preferenceGeneration)$('presentation-status').textContent=t('Настройки не синхронизированы')}
}
languageSelect.onchange=()=>void savePref({language:languageSelect.value});
fontSelect.onchange=()=>void savePref({font:fontSelect.value});
async function bindOwner(next){
 if(next===owner)return;
 owner=next;const generation=++preferenceGeneration;
 prefs={language:'en',font:'modern'};applyPrefs();$('presentation-status').textContent='';
 if(!next){try{prefs={...prefs,...JSON.parse(localStorage.getItem('mysticego.desktop.guest.presentation')||'{}')}}catch{}applyPrefs();return}
 loadingPreferences=true;languageSelect.disabled=fontSelect.disabled=true;
 try{const result=await api.presentation();if(generation===preferenceGeneration){prefs={...prefs,...result};applyPrefs()}}
 catch{if(generation===preferenceGeneration)$('presentation-status').textContent=t('Настройки не синхронизированы')}
 finally{if(generation===preferenceGeneration){loadingPreferences=false;languageSelect.disabled=fontSelect.disabled=false}}
}
async function refresh(){
  const state=await api.snapshot();
  const node=state.node,authorized=Boolean(state.actor);
  await bindOwner(state.actor?JSON.stringify([state.central,state.actor]):null);
  document.body.dataset.product=state.isAgent?'agent':'audit';
  $('product-title').textContent=state.isAgent?'AgentDesktop':'AuditDesktop';
  $('product-subtitle').textContent=state.isAgent?t("Клубная рабочая станция"):t("Личный профиль MysticEgo");
  const icon=state.isAgent?'agent-icon.png':'audit-icon.png';
  if($('product-icon').getAttribute('src')!==icon)$('product-icon').src=icon;
  $('device-card').hidden=!state.isAgent;
  $('register').hidden=state.isAgent;
  $('portal').hidden=state.isAgent;
  $('home').hidden=$('audit-companion').hidden=$('audit-stop').hidden=state.isAgent;
  if(!initialized){$('allow').checked=state.features.allow;$('processes').value=state.features.processes;initialized=true}
  if(!centralEdited&&state.central){$('central').value=state.central;$('portal-lan').checked=state.lan}
  $('embedded').hidden=!state.embedded;$('workspace').hidden=state.embedded;
  $('identity').hidden=!authorized;$('login-box').hidden=authorized;$('identity-name').textContent=state.actor??'';
  $('state').textContent=(state.online||state.personalOnline)?t("В сети"):state.running?t("Узел запускается"):node?t("Узел остановлен"):t("Ожидание подключения");
  $('state').classList.toggle('online',state.online);
  $('agent-badge').classList.toggle('online',state.clubAvailable);
  $('agent-badge').textContent=state.beacon?'● '+state.beacon:'○ Agent';
  $('current').textContent=node?node.name+' · '+node.kind+' · '+node.url:t("Вставьте приглашение от Local. ПК должен находиться в сети клуба.");
  const global=$('global-panel');
  const tag=JSON.stringify(state.globals);
  if(global.dataset.options!==tag){
    global.dataset.options=tag;
    global.replaceChildren(new Option(t('Мои организации Global'),''),...state.globals.map(org=>new Option(org.name,org.id)));
  }
  global.disabled=!state.globals.length;
  const team=state.team;
  $('team').hidden=!team;
  if(team){
    const labels={pending:'Заявка на рассмотрении',approved:'Доступ выдан',rejected:'Заявка отклонена',revoked:'Доступ отозван'};
    $('team-status').textContent=team.error?t("Не удалось загрузить статус команды."):team.owner?t("Главный разработчик"):t(labels[team.status]??'Можно подать заявку.');
    $('team-apply').hidden=team.error||team.owner||['pending','approved'].includes(team.status);
    $('orchestra').hidden=team.error||!team.owner&&team.status!=='approved';
  }
  $('join-form').hidden=!state.isAgent||!!node;
  $('import').hidden=!state.isAgent||node?.kind==='agent';$('start').hidden=$('stop').hidden=!authorized;
  $('start').disabled=!node||state.running||state.online;$('stop').disabled=!state.running;
  $('panel').disabled=!state.online||node?.kind!=='agent'&&!authorized;
  $('companion').disabled=!state.online||!['audit','agent'].includes(node?.kind)||node?.kind==='audit'&&!authorized;
  $('home').disabled=!authorized;
  $('home').textContent=state.personalOnline?t("Открыть Audit"):state.personalRunning?t("Audit запускается…"):t("Запустить и открыть Audit");
  $('audit-companion').disabled=!state.personalOnline;
  $('audit-stop').disabled=!state.personalRunning;
  $('folder').hidden=!authorized;
  $('allow').disabled=$('processes').disabled=state.running||state.personalRunning;$('record').disabled=state.recording;$('finish').disabled=!state.recording;
  $('apps').hidden=!authorized||Boolean(node&&!['audit','agent'].includes(node.kind));
  $('app-add').disabled=$('app-remove').disabled=state.running||state.personalRunning;
  $('app-list').textContent=Object.keys(state.features.apps||{}).join(' · ');
  $('automation').hidden=!authorized||state.platform!=='win32'||Boolean(node&&!['audit','agent'].includes(node.kind));
  const logs=state.logs.join('\n');if($('logs').textContent!==logs){$('logs').textContent=logs;$('logs').scrollTop=$('logs').scrollHeight}
}
async function run(action){
  $('message').textContent='';
  try{const result=await action();if(typeof result==='string')$('message').textContent=result;await refresh()}
  catch(error){$('message').textContent=error.message}
}
async function loadRegistrationDocuments(){
  const request=++documentRequest;
  documents=null;
  $('registration-document-box').hidden=true;
  $('accept-terms').checked=false;
  if(!register)return;
  await run(async()=>{
    const result=await api['registration-documents']($('central').value,$('portal-lan').checked);
    if(request!==documentRequest||!register)return;
    documents=result;
    $('registration-document-box').hidden=!result.required;
  });
}
$('central').oninput=()=>{
  centralEdited=true;
  documents=null;
  $('registration-document-box').hidden=true;
  documentRequest++;
};
$('central').onchange=$('portal-lan').onchange=()=>{
  centralEdited=true;
  void loadRegistrationDocuments();
};
for(const [button,kind] of [['terms-document','terms'],['privacy-document','privacy']]){
  $(button).onclick=()=>void run(()=>api['registration-document-open'](kind,$('central').value,$('portal-lan').checked));
}
$('register').onclick=()=>{
  register=!register;$('nickname-label').hidden=!register;$('nickname').required=register;
  $('login-title').textContent=register?t("Создайте аккаунт Audit"):t("Войдите в MysticEgo");
  $('login-button').textContent=register?t("Создать аккаунт"):t("Войти");
  $('register').textContent=register?t("У меня есть аккаунт"):t("Создать аккаунт");
  $('password').autocomplete=register?'new-password':'current-password';
  void loadRegistrationDocuments();
};
$('login-form').onsubmit=event=>{
  event.preventDefault();$('login-button').disabled=true;
  void run(async()=>{
    try{
      if(register&&!documents)throw Error('Дождитесь загрузки документов регистрации.');
      if(register&&documents?.required&&!$('accept-terms').checked)throw Error('Примите условия использования.');
      return await api.auth({login:$('login').value,password:$('password').value,register,nickname:$('nickname').value,
        central:$('central').value,lan:$('portal-lan').checked,
        termsVersion:documents?.terms?.version,privacyVersion:documents?.privacy?.version});
    }
    finally{$('password').value='';$('login-button').disabled=false}
  });
};
$('join-form').onsubmit=event=>{
  event.preventDefault();
  const text=$('invitation');
  void run(async()=>{const result=await api['club-join'](text.value);text.value='';return result});
};
$('import').onclick=()=>run(api.import);
$('start').onclick=()=>run(()=>api.start({allow:$('allow').checked,processes:$('processes').value}));
$('stop').onclick=()=>run(api.stop);
$('panel').onclick=()=>run(api.panel);
$('home').onclick=()=>run(api.home);
$('team-apply').onclick=async()=>{
  $('team-apply').disabled=true;
  try{await run(api['team-apply']);}finally{$('team-apply').disabled=false;}
};
$('orchestra').onclick=()=>run(api.orchestra);
$('global-panel').onchange=event=>{const id=event.currentTarget.value;event.currentTarget.value='';if(id)void run(()=>api.global(id))};
$('portal').onclick=()=>run(()=>api.portal($('central').value,$('portal-lan').checked));
$('back').onclick=()=>run(api.back);
$('logout').onclick=()=>run(api.logout);
$('companion').onclick=()=>run(()=>api.companion('device'));
$('audit-companion').onclick=()=>run(()=>api.companion('audit'));
$('audit-stop').onclick=()=>run(api['audit-stop']);
$('app-add').onclick=()=>run(()=>api.app($('app-alias').value,false));
$('app-remove').onclick=()=>run(()=>api.app($('app-alias').value,true));
$('record').onclick=()=>run(()=>api.record($('record-process').value));
$('finish').onclick=()=>run(api['finish-record']);
$('folder').onclick=()=>run(api.folder);
refresh();window.setInterval(()=>refresh().catch(()=>{}),1600);


