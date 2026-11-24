import {useEffect,useState} from 'react';
import {Check,Moon,Palette,Sparkles,Sun} from 'lucide-react';
import {useApp} from '../app/context';
import {preferenceKey} from '../app/panelPreferences';
import {Header,Panel} from '../components/primitives';

const palettes=[
 {id:'iris',name:'Ирис',colors:['#b49bf7','#60449a','#75d9c7']},
 {id:'jade',name:'Нефрит',colors:['#87e0d1','#3b7770','#af9bf7']},
 {id:'amber',name:'Янтарь',colors:['#f4b47f','#915f4d','#a6a6fa']},
 {id:'arctic',name:'Арктика',colors:['#83c6ff','#315b87','#81e7eb']},
 {id:'rose',name:'Роза',colors:['#f4adc9','#824161','#bc9df3']},
 {id:'lime',name:'Лайм',colors:['#c2e78d','#557538','#64e2b9']},
 {id:'indigo',name:'Индиго',colors:['#aaaafa','#4e4b98','#78dafd']},
 {id:'coral',name:'Коралл',colors:['#ffa99a','#985047','#ffe2ab']},
 {id:'steel',name:'Сталь',colors:['#c6d5eb','#53657c','#a5cccf']}
];
type Style={theme:'dark'|'light'|'system';palette:string;primary:string;secondary:string;ambient:boolean;wave:boolean;grid:boolean;border:boolean;intensity:number;density:'compact'|'comfortable';size:'normal'|'large'};
const defaults:Style={theme:'dark',palette:'iris',primary:'#b49bf7',secondary:'#75d9c7',ambient:true,wave:false,grid:false,border:false,intensity:55,density:'comfortable',size:'normal'};
function current(scope:string):Style{
 try{return {...defaults,...JSON.parse(localStorage.getItem(preferenceKey(scope,'appearance'))||'{}')}}
 catch{return defaults}
}
export function applyStyle(style:Style){
 const root=document.documentElement;
 root.dataset.theme=style.theme==='system'?(matchMedia('(prefers-color-scheme:light)').matches?'light':'dark'):style.theme;
 root.dataset.palette=style.palette;
 root.dataset.ambient=String(style.ambient);
 root.dataset.neonWave=String(style.wave);root.dataset.neonGrid=String(style.grid);root.dataset.neonBorder=String(style.border);
 root.dataset.density=style.density;root.dataset.textSize=style.size;
 const palette=palettes.find(item=>item.id===style.palette);
 const safe=(value:string,fallback:string)=>/^#[\da-f]{6}$/i.test(value)?value:fallback;
 const accent=palette?.colors[0]??safe(style.primary,defaults.primary);
 const alt=palette?.colors[2]??safe(style.secondary,defaults.secondary);
 root.style.setProperty('--accent',accent);
 root.style.setProperty('--accent-deep',palette?.colors[1]??'color-mix(in srgb,var(--accent) 62%,var(--canvas))');
 root.style.setProperty('--alt',alt);
 root.style.setProperty('--accent-light','color-mix(in srgb,var(--accent) 70%,var(--ink))');
 root.style.setProperty('--neon-strength',String(Math.max(.05,Math.min(1,style.intensity/100))));
}
function save(scope:string,style:Style){applyStyle(style);try{localStorage.setItem(preferenceKey(scope,'appearance'),JSON.stringify(style))}catch{}}
export function initPalette(scope:string){
 const media=matchMedia('(prefers-color-scheme:light)');
 const update=()=>applyStyle(current(scope));
 update();media.addEventListener('change',update);
 return()=>media.removeEventListener('change',update);
}
export function ThemeToggle({scope=''}:{scope?:string}){
 const [light,setLight]=useState(()=>{const theme=current(scope).theme;return theme==='light'||theme==='system'&&matchMedia('(prefers-color-scheme:light)').matches;});
 return <button type="button" className="icon-btn auth-theme" aria-label={light?'Тёмная тема':'Светлая тема'} onClick={()=>
  {setLight(!light);save(scope,{...current(scope),theme:light?'dark':'light'})}}>{light?<Moon size={19}/>:<Sun size={19}/>}</button>
}
export function Settings(){
 const {preferenceScope:scope}=useApp();
 const [style,setStyle]=useState(()=>current(scope));
 const change=<K extends keyof Style>(field:K,value:Style[K])=>setStyle(previous=>({...previous,[field]:value}));
 useEffect(()=>save(scope,style),[scope,style]);
 return <><Header title="Настройки" description="Тема, палитра и эффекты оформления" action={<button className="btn secondary" onClick={()=>setStyle({...defaults})}>Сбросить оформление</button>}/>
  <div className="two-col">
   <Panel title="Тема и интерфейс"><div className="stack">
    <label>Тема<select aria-label="Тема" value={style.theme} onChange={event=>change('theme',event.target.value as Style['theme'])}><option value="dark">Тёмная</option><option value="light">Светлая</option><option value="system">Как в системе</option></select></label>
    <label>Плотность<select value={style.density} onChange={event=>change('density',event.target.value as Style['density'])}><option value="comfortable">Комфортная</option><option value="compact">Компактная</option></select></label>
    <label>Текст<select value={style.size} onChange={event=>change('size',event.target.value as Style['size'])}><option value="normal">Обычный</option><option value="large">Крупный</option></select></label>
   </div></Panel>
   <Panel title="Неоновые эффекты" aside={<Sparkles size={18}/>}><div className="stack">
    {([['ambient','Анимация фонового свечения'],['wave','Неоновая волна'],['grid','Градиент по затухающей сетке'],['border','Свет по контуру кнопок']] as const).map(([field,label])=>
     <label className="check-row" key={field}><input type="checkbox" checked={style[field]} onChange={event=>change(field,event.target.checked)}/>{label}</label>)}
    <label>Яркость · {style.intensity}%<input type="range" min={10} max={100} value={style.intensity} onChange={event=>change('intensity',Number(event.target.value))}/></label>
    <button type="button" className="btn neon-preview">Предпросмотр кнопки</button>
   </div></Panel>
  </div>
  <Panel title="Палитра" aside={<Palette size={18}/>}>
   <div className="palette-options">{[...palettes,{id:'custom',name:'Своя палитра',colors:[style.primary,'#444057',style.secondary]}].map(item=>
    <button className={'palette-option '+(style.palette===item.id?'selected':'')} key={item.id} onClick={()=>change('palette',item.id)} aria-pressed={style.palette===item.id}>
     <div className="palette-swatches">{item.colors.map((color,index)=><i style={{background:color}} key={index}/>)}</div><strong>{item.name}</strong>{style.palette===item.id&&<Check size={17}/>}
    </button>)}</div>
   {style.palette==='custom'&&<div className="form-pair stack custom-colors"><label>Основной цвет<input type="color" value={style.primary} onChange={event=>change('primary',event.target.value)}/></label>
    <label>Второй цвет<input type="color" value={style.secondary} onChange={event=>change('secondary',event.target.value)}/></label></div>}
  </Panel>
 </>;
}
