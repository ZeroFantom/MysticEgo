import {createContext,useContext,useEffect,useRef,useState} from 'react';
import {NodeApi,type NodeMode} from '@mysticego/sdk';

export type Session={url:string;token:string;authUrl:string;refreshToken?:string;username?:string};
export type AccountProfile={id:string;displayName:string;bio:string;avatarUrl:string;backgroundUrl:string;coverUrl:string;accent:string;discoverable:boolean};
export type Personality={version:string;resultVersion?:string;completed:boolean;completedAt?:string;questions?:{id:string;axis:string;text:string}[];axes:{id:string;label:string;question:string;value:number|null}[]};
export type GameAttribute={id:string;label:string;group:'mind'|'social'|'body';value:number|null;locked:boolean;base:number|null;allocated:number;grade?:string;hint:string};
export type PersonalAccount={
  profile:AccountProfile;
  game:{id:string;nickname:string;rank:string;level:number;state:string;created:string;expiresAt?:string;immortal:boolean;ageHours:number};
  stats:GameAttribute[];
  personality:Personality;
  progress?:{experience:number;inLevel:number;toNext:number;percent:number;concealed:number;availablePoints:number;spentPoints:number;earnedPoints:number;
    resources:{id:string;title:string;current:number|null;maximum:number|null;formula:string;locked:boolean}[];
    traits:{id:string;title:string;value?:number;unit:string;formula:string;locked:boolean}[]};
};
export const CTX=createContext<{
  api:NodeApi;mode:NodeMode;session:Session;setSession:(x:Session)=>void;
  personal:PersonalAccount|null;profileError:string;reloadProfile:()=>void;portal:boolean;preferenceScope:string;
}|null>(null);
export function useApp(){
  const value=useContext(CTX);
  if(!value)throw new Error('Не удалось получить контекст приложения');
  return value;
}
export function useRemote<T>(load:()=>Promise<T>,deps:unknown[]=[],interval=0){
  const [value,setValue]=useState<T|null>(null);
  const [error,setError]=useState('');
  const [loading,setLoading]=useState(true);
  const [tick,setTick]=useState(0);
  const query=useRef(load);
  query.current=load;
  useEffect(()=>{
    let alive=true;
    let running=false;
    setValue(null);
    setLoading(true);
    setError('');
    async function update(){
      if(running)return;
      running=true;
      try{
        const result=await query.current();
        if(alive){setValue(result);setError('')}
      }catch(error){if(alive)setError(error instanceof Error?error.message:String(error))}
      finally{running=false;if(alive)setLoading(false)}
    }
    void update();
    const timer=interval?window.setInterval(()=>{if(document.visibilityState==='visible')void update()},interval):undefined;
    return()=>{alive=false;window.clearInterval(timer)};
  },[tick,interval,...deps]);
  return {value,error,loading,reload:()=>setTick(v=>v+1)};
}
