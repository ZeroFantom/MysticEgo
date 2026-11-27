// Shared compact updater UI, used by all four products. No network or filesystem access.
export function mountDesktopUpdate(host,api){
 if(!host||!api)return ()=>{};
 let stopped=false,timer,working=false,requesting=false,current=null;
 const make=(tag,cls,text)=>{const el=document.createElement(tag);if(cls)el.className=cls;if(text)el.textContent=text;return el};
 const card=make('section','desktop-update-card');card.hidden=true;card.setAttribute('aria-label','Обновление приложения');
 const row=make('div','desktop-update-row'),mark=make('span','desktop-update-mark','↻');mark.setAttribute('aria-hidden','true');
 const copy=make('div','desktop-update-copy'),title=make('strong'),detail=make('small');copy.append(title,detail);
 const button=make('button','desktop-update-action');button.type='button';
 const progress=make('progress','desktop-update-progress');progress.max=100;progress.hidden=true;
 const details=make('details','desktop-update-details'),summary=make('summary','','Что нового'),notes=make('pre');details.append(summary,notes);
 row.append(mark,copy,button);card.append(row,progress,details);host.append(card);
 function render(state){
  if(stopped||!state)return;
  current=state;const p=state.phase;
  card.hidden=p==='unsupported'||p==='idle'&&!state.tag;
  const percent=state.total?Math.max(0,Math.min(100,Math.floor(100*state.bytes/state.total))):0;
  title.textContent=({checking:'Проверка обновлений',downloading:'Скачивание обновления',ready:'Обновление готово',installing:'Установка обновления', 'restart-ready':'Обновление установлено',restarting:'Перезапуск приложения',error:'Не удалось обновить приложение'})[p]??'Обновление приложения';
  detail.textContent=p==='error'?state.error:p==='downloading'?(state.tag??'')+' · '+percent+'%':p==='restart-ready'&&state.error?state.error:p==='restart-ready'&&!state.canRestart?'Завершите текущую операцию, затем перезапустите приложение.':p==='ready'&&!state.canRestart?'Завершите текущую операцию перед обновлением.':(state.tag??'');
  progress.hidden=p!=='downloading'&&p!=='installing';if(p==='downloading')progress.value=percent;else progress.removeAttribute('value');
  button.hidden=!['ready','restart-ready','error'].includes(p);
  button.textContent=p==='ready'?'Установить и перезапустить':p==='restart-ready'?'Перезапустить и открыть':'Повторить';
  button.disabled=working||(p!=='error'&&!state.canRestart);
  const next=state.notes??'';details.hidden=!next;if(notes.textContent!==next){notes.textContent=next;details.open=false;}
  card.dataset.phase=p;
 }
 async function refresh(){if(stopped||requesting)return;requesting=true;try{render(await api.status())}catch{}finally{requesting=false}}
 button.onclick=async()=>{if(working||!current)return;working=true;button.disabled=true;try{render(await api.action(current.phase==='error'?'check':current.phase==='restart-ready'?'restart':'install'))}catch(e){detail.textContent=String(e.message??e)}finally{working=false;await refresh()}};
 void refresh();timer=setInterval(refresh,500);
 return ()=>{stopped=true;clearInterval(timer);card.remove()};
}

// The embedded web panel is a separate WebContentsView. Its launcher toolbar
// stays accessible without giving the remote page updater IPC privileges.
export function mountToolbarUpdate(button,api){
 if(!button||!api)return ()=>{};
 let stopped=false,working=false,current=null,polling=false;
 async function refresh(){
  if(stopped||polling)return;polling=true;
  try{
   const state=await api.status();if(stopped)return;current=state;
   const p=state.phase,percent=state.total?Math.floor(state.bytes*100/state.total):0;
   button.hidden=['idle','unsupported','checking'].includes(p);
   button.textContent=({downloading:'Обновление · '+percent+'%',ready:'Установить обновление','restart-ready':'Перезапустить',installing:'Установка…',restarting:'Перезапуск…',error:'Повторить обновление'})[p]??'Обновление';
   button.title=(state.tag??'')+(state.error?' · '+state.error:'')+(!state.canRestart?' · Дождитесь завершения операции':'');
   button.disabled=working||!['ready','restart-ready','error'].includes(p)||p!=='error'&&!state.canRestart;
  }catch{}finally{polling=false}
 }
 button.onclick=async()=>{if(working||!current)return;working=true;button.disabled=true;try{await api.action(current.phase==='error'?'check':current.phase==='restart-ready'?'restart':'install')}catch(e){button.title=String(e.message??e)}finally{working=false;void refresh()}};
 void refresh();const timer=setInterval(refresh,500);return ()=>{stopped=true;clearInterval(timer)};
}
