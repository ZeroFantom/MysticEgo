import {useEffect,useRef} from 'react';
import {useApp} from '../app/context';
export type Snapshot={motion:'idle'|'wave'|'nod'|'walk'|'sit'|'dance';mood:'neutral'|'happy'|'sad'|'angry'|'surprised';phaseSeconds:number};
export type Destination='chat'|'ar'|'spatial';
let pending:{scope:string;destination:Destination;snapshot:Snapshot;expires:number}|undefined;
export function validSnapshot(value:unknown):value is Snapshot{
 if(!value||typeof value!=='object')return false;
 const s=value as Snapshot;
 return ['idle','wave','nod','walk','sit','dance'].includes(s.motion)&&['neutral','happy','sad','angry','surprised'].includes(s.mood)&&Number.isFinite(s.phaseSeconds)&&s.phaseSeconds>=0&&s.phaseSeconds<=3600;
}
export function stageAvatar(scope:string,destination:Destination,value:unknown){
 if(!validSnapshot(value))return false;
 pending={scope,destination,snapshot:{motion:value.motion,mood:value.mood,phaseSeconds:value.phaseSeconds},expires:Date.now()+15000};return true;
}
export function useIncomingAvatar(destination:Destination,apply:(snapshot:Snapshot)=>void,enabled=true){
 const {preferenceScope}=useApp(),callback=useRef(apply);callback.current=apply;
 useEffect(()=>{if(!enabled||!pending)return;if(pending.expires<=Date.now()||pending.scope!==preferenceScope){pending=undefined;return}
  if(pending.destination===destination){const value=pending.snapshot;pending=undefined;callback.current(value)}
 },[destination,preferenceScope,enabled]);
}
