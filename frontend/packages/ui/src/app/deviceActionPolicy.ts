// Client capability hints only. Server account checks and explicit target approval remain mandatory.
export const navigationActions={'open-assistant':'/assistant','open-collective':'/collective','open-profile':'/profile','open-avatars':'/avatars'} as const;
export const actionLabels={'open-assistant':'Открыть ассистента','open-collective':'Открыть коллектив','open-profile':'Открыть профиль','open-avatars':'Открыть студию VRM','signal-device':'Подать сигнал','open-app-settings':'Настройки MysticEgo в Android','show-device-info':'Показать сведения о телефоне'} as const;
export type DeviceAction=keyof typeof actionLabels;
export function isAction(value:unknown):value is DeviceAction{return typeof value==='string'&&Object.hasOwn(actionLabels,value)}
export function advertisedActions(android:boolean):DeviceAction[]{return [...Object.keys(navigationActions) as (keyof typeof navigationActions)[],'signal-device',...(android?['open-app-settings','show-device-info'] as const:[])]}
export function nativeActionLink(action:DeviceAction,android:boolean):string|null{
 if(!android)return null;
 if(action==='signal-device')return 'mysticego://local-action/signal-device';
 if(action==='open-app-settings')return 'mysticego://local-action/open-app-settings';
 if(action==='show-device-info')return 'mysticego://local-action/show-device-info';
 return null;
}
