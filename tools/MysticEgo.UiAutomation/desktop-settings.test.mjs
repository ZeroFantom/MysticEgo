import test from 'node:test';
import assert from 'node:assert/strict';
import { sessionPlatform, DesktopSettingsService } from './desktop-settings.mjs';
const env={XDG_SESSION_TYPE:'x11',DISPLAY:':0',XDG_CURRENT_DESKTOP:'KDE'};
test('Windows and explicit X11 only, not XWayland',()=>{
  assert.equal(sessionPlatform('win32',{}),'windows');
  assert.equal(sessionPlatform('linux',env),'plasma-x11');
  assert.equal(sessionPlatform('linux',{...env,WAYLAND_DISPLAY:'wayland-0'}),'unsupported');
  assert.equal(sessionPlatform('linux',{...env,XDG_SESSION_TYPE:'wayland'}),'unsupported');
  assert.equal(sessionPlatform('linux',{DISPLAY:':0'}),'unsupported');
  assert.equal(sessionPlatform('darwin',{}),'unsupported');
});
test('KDE settings require consent and approved schemes; animation and desktop count readback',async()=>{
  let factor='0',count=1; const calls=[];
  const svc=new DesktopSettingsService({enabled:true,colorSchemes:['BreezeDark']},{platform:'linux',env,run:async(names,args)=>{
    calls.push([names,args]);
    if(names[0]==='kreadconfig6')return factor;
    if(names[0]==='kwriteconfig6'){factor=args.at(-1);return '';}
    if(args.includes('org.freedesktop.DBus.Properties.Get'))return '(<uint32 '+count+'>,)';
    if(args.includes('org.kde.KWin.VirtualDesktopManager.createDesktop'))count++;
    return '';
  }});
  assert.equal((await svc.call('desktop_animations_set',{enabled:true})).enabled,true);
  assert.equal((await svc.call('desktop_create',{name:'Работа'})).count,2);
  await assert.rejects(svc.call('desktop_colors_set',{scheme:'--help'}));
  await svc.call('desktop_colors_set',{scheme:'BreezeDark'});
  assert.deepEqual(calls.at(-1),[['plasma-apply-colorscheme'],['BreezeDark']]);
  svc.config=undefined;
  await assert.rejects(svc.call('desktop_count',{}),/approved/);
});
test('Windows desktop switch needs separate permission; no false verification',async()=>{
  const calls=[];
  const svc=new DesktopSettingsService({enabled:true},{platform:'win32',windowsRun:async args=>{calls.push(args);return {shortcutSent:true,verified:false};}});
  await assert.rejects(svc.call('desktop_create',{}));
  svc.config.allowDesktopSwitch=true;
  assert.deepEqual(await svc.call('desktop_create',{}),{shortcutSent:true,verified:false});
  assert.deepEqual(calls,[['desktop-create','local']]);
});
