const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('mysticDesktop',Object.freeze({
 onEdge:(handler)=>{const fn=()=>handler();ipcRenderer.on('avatar:edge',fn);return()=>ipcRenderer.removeListener('avatar:edge',fn)},
 walk:value=>ipcRenderer.invoke('avatar:walk',value),
 openLink:value=>ipcRenderer.invoke('avatar:approved-link',value),
 context:()=>ipcRenderer.invoke('avatar:context'),
 handoffReady:()=>ipcRenderer.invoke('avatar:handoff-ready'),
 login:(login,password)=>ipcRenderer.invoke('avatar:login',login,password),
 request:(options)=>ipcRenderer.invoke('avatar:request',options),
 taskbar:()=>ipcRenderer.invoke('avatar:dock-taskbar'),
 windows:()=>ipcRenderer.invoke('avatar:windows'),
 anchor:(title)=>ipcRenderer.invoke('avatar:anchor',title),
 clickThrough:(value)=>ipcRenderer.invoke('avatar:click-through',Boolean(value)),
 close:()=>ipcRenderer.invoke('avatar:close')
}));
