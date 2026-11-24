const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('station',Object.fromEntries(
  ['snapshot','auth','logout','import','start','stop','panel','home','team-apply','orchestra','global','portal','back','companion','app','record','finish-record','folder']
    .map(name=>[name,(...args)=>ipcRenderer.invoke('station:'+name,...args)])
));
