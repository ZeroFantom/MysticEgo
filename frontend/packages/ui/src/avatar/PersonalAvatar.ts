import {useEffect,useState} from 'react';
import {useApp,useRemote} from '../app/context';
import type {AvatarMotion} from './AvatarStage';
import type {ClipAsset} from './AvatarClips';

export const motions:Record<AvatarMotion,string>={idle:'Ожидание',walk:'Прогулка',wave:'Приветствие',nod:'Кивок',dance:'Танец',talk:'Разговор',
  sit:'Сесть',lean:'Опереться',perch:'Сесть на край',bump:'Столкновение с краем',listen:'Слушать'};
export const styles={calm:'Спокойный',lively:'Живой',focused:'Собранный'};
export type MotionStyle=keyof typeof styles;
export const intensityByStyle:Record<MotionStyle,number>={calm:.5,lively:1.1,focused:.75};
export type AvatarAppearance={zoom:number;yaw:number;vertical:number;light:number;speed:number;idleMotion:AvatarMotion;showOnSite:boolean;animateReplies:boolean};
export const defaultAppearance:AvatarAppearance={zoom:1,yaw:0,vertical:0,light:1,speed:1,idleMotion:'idle',showOnSite:true,animateReplies:true};
export type AvatarAsset={id:string;extension:string;kind:string;size:number;name?:string};
export type Selection={assetId:string;motions?:Partial<Record<AvatarMotion,string>>;motionStyle?:MotionStyle;appearance?:AvatarAppearance};
export type Library={selection?:Selection;assets:AvatarAsset[]};
export const accountAvatar='/avatars/account';
export const deviceAvatar='/avatars/personal';
export const avatarSaved='mystic-avatar-saved';

export function usePersonalAvatar(enabled=true,followSaves=true){
  const {api,mode,portal}=useApp();
  const [revision,revise]=useState(0);
  useEffect(()=>{
    if(!followSaves)return;
    const update=()=>revise(previous=>previous+1);
    window.addEventListener(avatarSaved,update);
    return()=>window.removeEventListener(avatarSaved,update);
  },[followSaves]);
  return useRemote(async()=>{
    if(!enabled)return null;
    if(mode!=='audit')return {...await api.request<Library>(deviceAvatar+'/profile'),path:deviceAvatar};
    const account=await api.request<Library>(accountAvatar+'/profile').catch(error=>{if(portal)throw error;return null});
    if(account?.selection)return {...account,path:accountAvatar};
    if(!portal){
      const current=await api.request<Library>(deviceAvatar+'/profile');
      if(current.selection||current.assets.length||!account)return {...current,path:deviceAvatar};
    }
    return {...account!,path:accountAvatar};
  },[api,mode,portal,revision,enabled]);
}

type Media={model?:{url:string;mime:string};clips:Partial<Record<AvatarMotion,ClipAsset>>;error?:string};
export function useAvatarMedia(modelId:string|undefined,mapping:Selection['motions'],assets:AvatarAsset[]|undefined,
  modelRoute:string,clipRoute=modelRoute,enabled=true){
  const {session}=useApp();
  const [media,setMedia]=useState<Media>({clips:{}});
  const base=session.url.replace(/\/$/,'');
  useEffect(()=>{
    setMedia({clips:{}});
    if(!enabled||!modelId)return;
    const controller=new AbortController();
    const urls:string[]=[];
    const download=async(route:string,id:string)=>{
      const response=await fetch(base+'/api/v1'+route+encodeURIComponent(id),{
        signal:controller.signal,headers:{Authorization:'Bearer '+session.token}
      });
      if(!response.ok)throw Error('Файл аватара недоступен.');
      const blob=await response.blob();
      if(controller.signal.aborted)return '';
      const url=URL.createObjectURL(blob);urls.push(url);return url;
    };
    void (async()=>{
      const mime=assets?.find(asset=>asset.id===modelId)?.extension??'.vrm';
      const url=modelId==='bundled'?base+'/bundled/mysticego.vrm':await download(modelRoute,modelId);
      if(!controller.signal.aborted)setMedia({model:{url,mime},clips:{}});
      const results=await Promise.allSettled(Object.entries(mapping??{}).map(async([slot,id])=>{
        const asset=assets?.find(item=>item.id===id);
        return asset?[slot,{url:await download(clipRoute,asset.id),extension:asset.extension}] as const:null;
      }));
      const records=results.flatMap(result=>result.status==='fulfilled'&&result.value?[result.value]:[]);
      if(!controller.signal.aborted)setMedia({model:{url,mime},clips:Object.fromEntries(records),
        error:results.some(value=>value.status==='rejected')?'Часть анимаций недоступна. Используются встроенные движения.':undefined});
    })().catch(error=>{if(!controller.signal.aborted)setMedia({clips:{},error:error.message})});
    return()=>{controller.abort();urls.forEach(url=>URL.revokeObjectURL(url))};
  },[assets,base,clipRoute,enabled,mapping,modelId,modelRoute,session.token]);
  return media;
}
