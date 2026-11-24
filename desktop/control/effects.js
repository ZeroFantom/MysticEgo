const paletteList={iris:['Ирис','#b49bf7','#75d9c7'],jade:['Нефрит','#85dfcc','#aa8cf4'],amber:['Янтарь','#f1b47c','#bcb7fe'],arctic:['Арктика','#86c7ff','#75ebed'],rose:['Роза','#f2a9c7','#bc9df3'],lime:['Лайм','#bfed79','#6ee5b8'],indigo:['Индиго','#acaafa','#66dfff'],coral:['Коралл','#ffa99a','#f7d696']};
export function initEffects(host){
  if(!host)return;
  let state={theme:'dark',palette:'iris',motion:'none',intensity:55,accent:'#b49bf7',alt:'#75d9c7'};
  try{state={...state,...JSON.parse(localStorage.getItem('mysticego.desktop.style')||'{}')}}catch{}
  const system=matchMedia('(prefers-color-scheme: light)');
  function set(key,value){state[key]=value;apply();try{localStorage.setItem('mysticego.desktop.style',JSON.stringify(state))}catch{}}
  function apply(){
    const root=document.documentElement;
    root.dataset.theme=state.theme==='system'?(system.matches?'light':'dark'):state.theme;
    root.dataset.motion=state.motion;
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
  color('Основной цвет','accent');color('Второй цвет','alt');
  select('Анимация','motion',[['none','Без эффектов'],['wave','Неоновая волна'],['grid','Затухающая сетка'],['border','Свет по контуру'],['all','Все эффекты']]);
  const intensity=document.createElement('input');intensity.type='range';intensity.min='10';intensity.max='100';intensity.value=state.intensity;
  intensity.oninput=()=>set('intensity',Number(intensity.value));field('Яркость',intensity);
  system.addEventListener('change',apply);apply();
}
