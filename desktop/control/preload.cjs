const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('center',Object.fromEntries(
  ['snapshot','start','stop','save','credentials','link'].map(name=>[name,(...args)=>ipcRenderer.invoke('center:'+name,...args)])
));
