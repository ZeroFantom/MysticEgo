const paletteList={iris:['Ирис','#b49bf7','#75d9c7'],jade:['Нефрит','#85dfcc','#aa8cf4'],amber:['Янтарь','#f1b47c','#bcb7fe'],arctic:['Арктика','#86c7ff','#75ebed'],rose:['Роза','#f2a9c7','#bc9df3'],lime:['Лайм','#bfed79','#6ee5b8'],indigo:['Индиго','#acaafa','#66dfff'],coral:['Коралл','#ffa99a','#f7d696']};
export function initEffects(host){
  if(!host)return;
  let state={theme:'dark',interfaceStyle:'simplified',transition:'flow',uiMotion:'normal',palette:'iris',motion:'none',intensity:55,accent:'#b49bf7',alt:'#75d9c7'};
  try{state={...state,...JSON.parse(localStorage.getItem('mysticego.desktop.style')||'{}')}}catch{}
  const system=matchMedia('(prefers-color-scheme: light)');
  function set(key,value){state[key]=value;apply();try{localStorage.setItem('mysticego.desktop.style',JSON.stringify(state))}catch{}}
  function apply(){
    const root=document.documentElement;
    root.dataset.theme=state.theme==='system'?(system.matches?'light':'dark'):state.theme;
    root.dataset.interface=state.interfaceStyle==='immersive'?'immersive':'simplified';
    root.dataset.transition=['flow','fade','slide','scale'].includes(state.transition)?state.transition:'flow';
    root.dataset.uiMotion=['off','low','normal'].includes(state.uiMotion)?state.uiMotion:'normal';
    root.dataset.motion=root.dataset.interface==='immersive'&&root.dataset.uiMotion==='normal'?state.motion:'none';
    const colors=paletteList[state.palette];
    const safe=(value,fallback)=>/^#[\da-f]{6}$/i.test(value)?value:fallback;
    root.style.setProperty('--accent',colors?.[1]||safe(state.accent,'#b49bf7'));
    root.style.setProperty('--alt',colors?.[2]||safe(state.alt,'#75d9c7'));
    root.style.setProperty('--glow',String(Math.min(1,Math.max(.05,Number(state.intensity)/100))));
  }
  function field(label,input){
    const node=document.createElement('label');node.textContent=label;node.append(input);host.append(node);
  }
  function select(label,key,options){
    const input=document.createElement('select');input.append(...options.map(([value,text])=>new Option(text,value)));
    input.value=state[key];input.onchange=()=>set(key,input.value);field(label,input);
  }
  function color(label,key){
    const input=document.createElement('input');input.type='color';input.value=state[key];
    input.oninput=()=>{state.palette='custom';host.querySelectorAll('select')[1].value='custom';set(key,input.value)};field(label,input);
  }
  select('Тема','theme',[['dark','Тёмная'],['light','Светлая'],['system','Как в системе']]);
  select('Палитра','palette',[...Object.entries(paletteList).map(([id,[name]])=>[id,name]),['custom','Своя']]);
  select('Стиль интерфейса','interfaceStyle',[['simplified','Упрощённый'],['immersive','Насыщенный']]);
  select('Анимации интерфейса','uiMotion',[['normal','Обычные'],['low','Минимальные'],['off','Выключены']]);
  select('Переходы','transition',[['flow','Мягкое появление'],['fade','Растворение'],['slide','Скольжение'],['scale','Масштабирование']]);
  color('Основной цвет','accent');color('Второй цвет','alt');
  select('Анимация','motion',[['none','Без эффектов'],['wave','Неоновая волна'],['grid','Затухающая сетка'],['border','Свет по контуру'],['all','Все эффекты']]);
  const intensity=document.createElement('input');intensity.type='range';intensity.min='10';intensity.max='100';intensity.value=state.intensity;
  intensity.oninput=()=>set('intensity',Number(intensity.value));field('Яркость',intensity);
  const reset=document.createElement('button');reset.type='button';reset.className='secondary';reset.textContent='Сбросить оформление';reset.onclick=()=>{state={theme:'dark',interfaceStyle:'simplified',transition:'flow',uiMotion:'normal',palette:'iris',motion:'none',intensity:55,accent:'#b49bf7',alt:'#75d9c7'};for(const [i,key] of ['theme','palette','interfaceStyle','uiMotion','transition','motion'].entries())host.querySelectorAll('select')[i].value=state[key];const colors=host.querySelectorAll('input[type=color]');colors[0].value=state.accent;colors[1].value=state.alt;intensity.value=state.intensity;set('theme',state.theme)};host.append(reset);
  system.addEventListener('change',apply);apply();
}
