import React,{useLayoutEffect,useState} from 'react';
const key='mysticego.clouddesk.appearance.v1',defaults={theme:'dark',interfaceStyle:'simplified',uiMotion:'normal',transition:'flow'};
const options={theme:[['dark','Тёмная'],['light','Светлая'],['system','Системная']],interfaceStyle:[['simplified','Упрощённый'],['immersive','Насыщенный']],uiMotion:[['normal','Обычные'],['low','Минимальные'],['off','Выключены']],transition:[['flow','Мягкий'],['fade','Растворение'],['slide','Скольжение'],['scale','Масштабирование']]};
function read(){
 try{
  const saved=JSON.parse(localStorage.getItem(key)||'{}')??{};
  return Object.fromEntries(Object.entries(options).map(([name,choices])=>[name,choices.some(([v])=>v===saved[name])?saved[name]:defaults[name]]));
 }catch{return {...defaults}}
}
export function AppearanceControls(){
 const [look,setLook]=useState(read);
 useLayoutEffect(()=>{
  const media=matchMedia('(prefers-color-scheme:light)');
  const apply=()=>{const root=document.documentElement;root.dataset.theme=look.theme==='system'?(media.matches?'light':'dark'):look.theme;root.dataset.interface=look.interfaceStyle;root.dataset.uiMotion=look.uiMotion;root.dataset.transition=look.transition;};
  apply();media.addEventListener('change',apply);try{localStorage.setItem(key,JSON.stringify(look))}catch{}
  return()=>media.removeEventListener('change',apply)
 },[look]);
 const field=(name,label)=><label>{label}<select value={look[name]} onChange={e=>setLook(v=>({...v,[name]:e.target.value}))}>{options[name].map(([v,t])=><option key={v} value={v}>{t}</option>)}</select></label>;
 return <details className="desk-appearance"><summary>Оформление</summary>{field('theme','Тема')}
 <div className="desk-style-options" role="group" aria-label="Стиль интерфейса">{options.interfaceStyle.map(([value,title])=><button key={value} type="button" className="desk-style-option" data-preview={value} aria-pressed={look.interfaceStyle===value} onClick={()=>setLook(v=>({...v,interfaceStyle:value}))}><span className="desk-art-preview" aria-hidden="true"/><strong>{title}</strong></button>)}</div>
 {field('uiMotion','Анимации')}{field('transition','Переход')}<button type="button" className="subtle" onClick={()=>setLook({...defaults})}>Сбросить оформление</button></details>
}
