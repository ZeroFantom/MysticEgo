const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('station',Object.fromEntries(
  ['snapshot','release-highlights-dismiss','presentation','registration-documents','registration-document-open','club-join','auth','logout','import','start','stop','panel','home','audit-stop','team-apply','orchestra','global','portal','back','companion','app','record','finish-record','folder']
    .map(name=>[name,(...args)=>ipcRenderer.invoke('station:'+name,...args)])
));


contextBridge.exposeInMainWorld('desktopUpdates',Object.freeze({status:()=>ipcRenderer.invoke('desktop-update:status'),action:action=>ipcRenderer.invoke('desktop-update:action',action)}));
