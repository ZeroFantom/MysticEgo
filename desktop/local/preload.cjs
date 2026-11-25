const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('local',Object.fromEntries(
  ['connect','provision','status','start','stop','panel'].map(name=>[name,(...args)=>ipcRenderer.invoke('local:'+name,...args)])
));
