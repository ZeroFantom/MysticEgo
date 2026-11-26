const paletteList={iris:['Ирис','#b49bf7','#75d9c7'],jade:['Нефрит','#85dfcc','#aa8cf4'],amber:['Янтарь','#f1b47c','#bcb7fe'],arctic:['Арктика','#86c7ff','#75ebed'],rose:['Роза','#f2a9c7','#bc9df3'],lime:['Лайм','#bfed79','#6ee5b8'],indigo:['Индиго','#acaafa','#66dfff'],coral:['Коралл','#ffa99a','#f7d696']};
export function initEffects(host){
  if(!host)return;
  const defaults={theme:'dark',interfaceStyle:'simplified',transition:'flow',uiMotion:'normal',palette:'iris',motion:'none',intensity:55,accent:'#b49bf7',alt:'#75d9c7'};
  let state={...defaults};
  try{state={...state,...JSON.parse(localStorage.getItem('mysticego.desktop.style')||'{}')}}catch{}
  const choices={theme:['dark','light','system'],interfaceStyle:['simplified','immersive'],transition:['flow','fade','slide','scale'],uiMotion:['normal','low','off'],motion:['none','wave','grid','border','all'],palette:[...Object.keys(paletteList),'custom']};
  for(const [key,values] of Object.entries(choices))if(!values.includes(state[key]))state[key]=defaults[key];
  for(const key of ['accent','alt'])if(!/^#[\da-f]{6}$/i.test(state[key]))state[key]=defaults[key];
  state.intensity=Number.isFinite(Number(state.intensity))?Math.min(100,Math.max(10,Number(state.intensity))):defaults.intensity;
  const system=matchMedia('(prefers-color-scheme: light)');
  const selects=new Map(),colors=new Map(),styleButtons=new Map();
  function sync(){
    for(const [key,input] of selects)input.value=state[key];
    for(const [key,input] of colors)input.value=state[key];
    for(const [key,button] of styleButtons)button.setAttribute('aria-pressed',String(state.interfaceStyle===key));
    intensity.value=String(state.intensity);
  }
  function set(key,value){state[key]=value;apply();sync();try{localStorage.setItem('mysticego.desktop.style',JSON.stringify(state))}catch{}}
  function apply(){
    const root=document.documentElement;
    root.dataset.theme=state.theme==='system'?(system.matches?'light':'dark'):state.theme==='light'?'light':'dark';
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
    selects.set(key,input);input.value=state[key];input.onchange=()=>set(key,input.value);field(label,input);
  }
  function color(label,key){
    const input=document.createElement('input');input.type='color';input.value=state[key];colors.set(key,input);
    input.oninput=()=>{state.palette='custom';set(key,input.value)};field(label,input);
  }
  select('Тема','theme',[['dark','Тёмная'],['light','Светлая'],['system','Как в системе']]);
  select('Палитра','palette',[...Object.entries(paletteList).map(([id,[name]])=>[id,name]),['custom','Своя']]);
  const styles=document.createElement('div');styles.className='desktop-style-options';styles.setAttribute('role','group');styles.setAttribute('aria-label','Стиль интерфейса');
  for(const [key,title] of [['simplified','Упрощённый'],['immersive','Насыщенный']]){
    const button=document.createElement('button');button.type='button';button.className='secondary desktop-style-option';button.dataset.preview=key;
    const preview=document.createElement('span');preview.className='desktop-art-preview';preview.setAttribute('aria-hidden','true');
    const text=document.createElement('strong');text.textContent=title;button.append(preview,text);
    button.onclick=()=>set('interfaceStyle',key);styleButtons.set(key,button);styles.append(button);
  }
  host.append(styles);
  select('Анимации интерфейса','uiMotion',[['normal','Обычные'],['low','Минимальные'],['off','Выключены']]);
  select('Переходы','transition',[['flow','Мягкое появление'],['fade','Растворение'],['slide','Скольжение'],['scale','Масштабирование']]);
  color('Основной цвет','accent');color('Второй цвет','alt');
  select('Анимация','motion',[['none','Без эффектов'],['wave','Неоновая волна'],['grid','Затухающая сетка'],['border','Свет по контуру'],['all','Все эффекты']]);
  const intensity=document.createElement('input');intensity.type='range';intensity.min='10';intensity.max='100';intensity.value=state.intensity;
  intensity.oninput=()=>set('intensity',Number(intensity.value));field('Яркость',intensity);
  const reset=document.createElement('button');reset.type='button';reset.className='secondary';reset.textContent='Сбросить оформление';reset.onclick=()=>{state={...defaults};set('theme',state.theme)};host.append(reset);
  system.addEventListener('change',apply);apply();sync();
}
