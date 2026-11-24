import {useEffect,useState} from 'react';
import {useApp} from './context';

export function preferenceKey(scope:string,name:string){
 return 'mysticego.panel.v1.'+scope+'.'+name;
}

export function usePanelPreference<T>(name:string,defaultValue:T,valid:(value:unknown)=>value is T){
 const {preferenceScope:scope}=useApp();
 const key=preferenceKey(scope,name);
 const [value,setValue]=useState<T>(()=>{
  if(scope)try{
   const saved:unknown=JSON.parse(localStorage.getItem(key)??'null');
   if(valid(saved))return saved;
  }catch{/* Use the default if storage is unavailable. */}
  return defaultValue;
 });
 useEffect(()=>{
  if(scope)try{localStorage.setItem(key,JSON.stringify(value));}catch{}
 },[scope,key,value]);
 return [value,setValue] as const;
}
