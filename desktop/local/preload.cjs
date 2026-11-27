const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('local',Object.fromEntries(
  ['release-highlights-dismiss','connect','provision','status','start','stop','panel'].map(name=>[name,(...args)=>ipcRenderer.invoke('local:'+name,...args)])
));

contextBridge.exposeInMainWorld('desktopUpdates',Object.freeze({status:()=>ipcRenderer.invoke('desktop-update:status'),action:action=>ipcRenderer.invoke('desktop-update:action',action)}));
