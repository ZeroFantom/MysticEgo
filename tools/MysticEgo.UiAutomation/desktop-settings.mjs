import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { existsSync } from 'node:fs';
import { isAbsolute } from 'node:path';
const exec = promisify(execFile);

export function sessionPlatform(platform = process.platform, env = process.env) {
  if (platform === 'win32') return 'windows';
  if (platform !== 'linux') return 'unsupported';
  if (env.WAYLAND_DISPLAY || String(env.XDG_SESSION_TYPE).toLowerCase() === 'wayland') return 'unsupported';
  if (String(env.XDG_SESSION_TYPE).toLowerCase() !== 'x11' || !env.DISPLAY) return 'unsupported';
  return /(^|[:;])(?:KDE|Plasma)(?:[:;]|$)/i.test(env.XDG_CURRENT_DESKTOP ?? '') ? 'plasma-x11' : 'x11';
}

const systemRun = async (names, args) => {
  const file = names.map(n=>'/usr/bin/'+n).find(existsSync);
  if (!file) throw new Error('Required desktop utility unavailable');
  const {stdout} = await exec(file,args,{timeout:8000,maxBuffer:128*1024});
  return stdout.trim();
};
const bus = ['gdbus'];
const base = ['call','--session','--dest','org.kde.KWin','--object-path','/VirtualDesktopManager','--method'];
const iface = 'org.kde.KWin.VirtualDesktopManager';
const schemaName = /^[A-Za-z0-9][A-Za-z0-9 _.-]{0,79}$/;
export class DesktopSettingsService {
  constructor(config, {platform=process.platform,env=process.env,run=systemRun,windowsRun}={}) {
    this.config=config; this.platform=sessionPlatform(platform,env); this.run=run;
    this.windowsRun=windowsRun ?? (async args=>{
      const file=this.config?.windowsHelper;
      if (!isAbsolute(file ?? '')) throw new Error('Configure trusted Windows helper');
      const {stdout}=await exec(file,args,{timeout:8000,windowsHide:true,maxBuffer:128*1024});
      return JSON.parse(stdout);
    });
  }
  async call(name,a) {
    if (!this.config?.enabled) throw new Error('Desktop settings not locally approved');
    if (name==='desktop_capabilities') return {platform:this.platform,animations:['plasma-x11','windows'].includes(this.platform),colorScheme:this.platform==='plasma-x11',plasmaTheme:this.platform==='plasma-x11',createDesktop:this.platform==='plasma-x11'||(this.platform==='windows'&&this.config.allowDesktopSwitch===true),windowsColorsSettings:this.platform==='windows'};
    if (this.platform==='windows') {
      if (name==='desktop_animations_get') return this.windowsRun(['animations-get','local']);
      if (name==='desktop_animations_set' && typeof a.enabled==='boolean') return this.windowsRun(['animations-set','local',String(a.enabled)]);
      if (name==='desktop_create' && this.config.allowDesktopSwitch===true) return this.windowsRun(['desktop-create','local']);
      if (name==='desktop_colors_open') return this.windowsRun(['colors-open','local']);
      throw new Error('Unsupported Windows desktop operation');
    }
    if (this.platform!=='plasma-x11') throw new Error('KDE Plasma X11 session required');
    const read=async()=> {
      const result=await this.run(['kreadconfig6','kreadconfig5'],['--file','kdeglobals','--group','KDE','--key','AnimationDurationFactor','--default','1']);
      const factor=Number(result);
      if (!result || !Number.isFinite(factor) || factor<0) throw new Error('Invalid KDE animation factor');
      return {enabled:factor>0,factor};
    };
    if (name==='desktop_animations_get') return read();
    if (name==='desktop_animations_set' && typeof a.enabled==='boolean') {
      const before=await read();
      if (before.enabled!==a.enabled) {
        await this.run(['kwriteconfig6','kwriteconfig5'],['--file','kdeglobals','--group','KDE','--key','AnimationDurationFactor','--notify',a.enabled?'1':'0']);
        await this.run(bus,['call','--session','--dest','org.kde.KWin','--object-path','/KWin','--method','org.kde.KWin.reconfigure']);
      }
      const after=await read();
      if(after.enabled!==a.enabled)throw new Error('Animation setting not verified');
      return {...after,previousFactor:before.factor};
    }
    if (name==='desktop_colors_list') return {schemes:await this.run(['plasma-apply-colorscheme'],['--list-schemes'])};
    if (name==='desktop_colors_set' && schemaName.test(a.scheme ?? '') && this.config.colorSchemes?.includes(a.scheme)) {
      await this.run(['plasma-apply-colorscheme'],[a.scheme]);
      return {requested:a.scheme,appliedCommand:true,note:'Panel follows the scheme only if its Plasma style supports system colors.'};
    }
    if (name==='desktop_theme_list') return {themes:await this.run(['plasma-apply-desktoptheme'],['--list-themes'])};
    if (name==='desktop_theme_set' && schemaName.test(a.theme ?? '') && this.config.plasmaThemes?.includes(a.theme)) {
      await this.run(['plasma-apply-desktoptheme'],[a.theme]);
      return {requested:a.theme,appliedCommand:true};
    }
    const count=async()=>{
      const out=await this.run(bus,[...base,'org.freedesktop.DBus.Properties.Get',iface,'count']);
      const match=out.match(/uint32\s+(\d+)/);
      if(!match)throw new Error('Cannot read virtual desktop count');
      return Number(match[1]);
    };
    if (name==='desktop_count') return {count:await count()};
    if (name==='desktop_create') {
      const before=await count();
      if(before>=20)throw new Error('Desktop limit reached');
      const label=a.name??'MysticEgo';
      if(typeof label!=='string'||!label.trim()||label.length>80||/[\x00-\x1f]/.test(label))throw new Error('Invalid desktop name');
      // GVariant string argument; JSON quoting protects quotes and option-like values.
      await this.run(bus,[...base,iface+'.createDesktop',String(before),JSON.stringify(label)]);
      const after=await count();
      if(after!==before+1)throw new Error('Desktop creation not verified');
      return {count:after,previousCount:before,name:label};
    }
    throw new Error('Unsupported desktop settings operation');
  }
}
