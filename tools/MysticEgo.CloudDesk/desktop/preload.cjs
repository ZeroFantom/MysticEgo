const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('desk',{rpc:(action,args={})=>ipcRenderer.invoke('rpc',action,args),login:()=>ipcRenderer.invoke('login'),cloudConsole:()=>ipcRenderer.invoke('cloudConsole'),portal:()=>ipcRenderer.invoke('portal'),script:()=>ipcRenderer.invoke('script')});
