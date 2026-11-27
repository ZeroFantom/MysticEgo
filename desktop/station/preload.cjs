const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('station',Object.fromEntries(
  ['snapshot','presentation','registration-documents','registration-document-open','club-join','auth','logout','import','start','stop','panel','home','audit-stop','team-apply','orchestra','global','portal','back','companion','app','record','finish-record','folder']
    .map(name=>[name,(...args)=>ipcRenderer.invoke('station:'+name,...args)])
));

