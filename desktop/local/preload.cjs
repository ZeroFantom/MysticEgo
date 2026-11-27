const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('local',Object.fromEntries(
  ['release-highlights-dismiss','connect','provision','status','start','stop','panel'].map(name=>[name,(...args)=>ipcRenderer.invoke('local:'+name,...args)])
));
