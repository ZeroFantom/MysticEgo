import {initEffects} from '../control/effects.js';
const $=id=>document.getElementById(id);
const api=window.local;
initEffects($('design'));
let dirty=false,busy=false;
async function refresh(){
  const state=await api.status();
  const input=$('connect').elements.global;
  if(!dirty&&state.global)input.value=state.global;
  $('state').textContent=state.online?'В сети':state.running?'Запускается':state.name?'Остановлен':'Ожидает настройки';
  $('name').textContent=state.name||'Local';
  $('address').textContent=state.address||'';
  $('start').disabled=busy||!state.name||state.running||state.online;
  $('stop').disabled=busy||!state.running;
  $('panel').disabled=busy||!state.online;
  $('provision').querySelector('button').disabled=busy||state.running||state.online;
  $('logs').textContent=state.logs.join('\n');
}
async function run(action){
  if(busy)return;
  busy=true;$('connect').querySelector('button').disabled=true;
  $('message').textContent='';
  try{await action()}catch(error){$('message').textContent=error.message}
  finally{busy=false;$('connect').querySelector('button').disabled=false;await refresh()}
}
$('connect').elements.global.oninput=()=>{dirty=true};
$('connect').onsubmit=event=>{
  event.preventDefault();
  void run(async()=>{
    const form=$('connect');
    $('provision').hidden=true;
    const values=Object.fromEntries(new FormData(form));
    form.elements.password.value='';
    const sites=await api.connect(values);
    $('provision').elements.site.replaceChildren(new Option('Выберите площадку',''),...sites.map(site=>new Option(site.name+' · '+site.publicUrl,site.id)));
    $('provision').hidden=!sites.length;
    if(!sites.length)$('message').textContent='Сначала создайте площадку в панели Global.';
  });
};
$('provision').onsubmit=event=>{event.preventDefault();void run(()=>api.provision($('provision').elements.site.value))};
for(const action of ['start','stop','panel'])$(action).onclick=()=>void run(api[action]);
void refresh();setInterval(()=>refresh().catch(()=>{}),1500);
