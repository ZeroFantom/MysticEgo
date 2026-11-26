import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const bytes=p=>readFileSync(new URL('../../'+p,import.meta.url));
const read=p=>bytes(p).toString('utf8');
const groups=[
 ['frontend/packages/ui/src/app/appearance.css','frontend/packages/ui/src/app/appearance-art','frontend/packages/ui/src/pages/Settings.tsx'],
 ['desktop/control/style.css','desktop/control/appearance-art','desktop/control/effects.js'],
 ['tools/MysticEgo.CloudDesk/src/style.css','tools/MysticEgo.CloudDesk/src/appearance-art','tools/MysticEgo.CloudDesk/src/Appearance.jsx']
];
for(const [css,assets,settings] of groups){
 const styles=read(css),controls=read(settings);
 for(const color of ['dark','light'])for(const style of ['immersive','simplified']){
  const name=style+'-'+color+'-hero.webp';
  assert(styles.includes('[data-interface="'+style+'"][data-theme="'+color+'"]'),css+' missing '+style+'/'+color);
  const entry=style+'-'+color+'-entry.webp';
  const header=style+'-'+color+'-header.webp',sidebar=style+'-'+color+'-sidebar.webp';
  const headerHeight=style==='simplified'&&color==='light'?166:167;
  const sidebarWidth=style==='simplified'&&color==='dark'?169:style==='immersive'&&color==='dark'?173:171;
  for(const [asset,width,height] of [[name,1916,821],[entry,1122,1402],[header,1414,headerHeight],[sidebar,sidebarWidth,708]]){
    assert(styles.includes(asset),css+' missing '+asset);
    const art=bytes(assets+'/'+asset);
    assert.equal(art.subarray(0,4).toString(),'RIFF');assert.equal(art.subarray(8,12).toString(),'WEBP');
    assert.equal(art.subarray(12,16).toString(),'VP8 ');assert.equal(art.subarray(23,26).toString('hex'),'9d012a');
    assert.equal(art.readUInt16LE(26)&0x3fff,width);assert.equal(art.readUInt16LE(28)&0x3fff,height);
    assert(art.equals(bytes(groups[0][1]+'/'+asset)),asset+' differs between clients');
    assert(read('tools/MysticEgo.CloudDesk/update.sh').includes('src/appearance-art/'+asset));
  }
 }
 for(const option of ['interfaceStyle','uiMotion','transition','system','simplified','immersive'])assert(controls.includes(option),settings+' missing '+option);
 assert(styles.includes('prefers-reduced-motion')&&styles.includes('data-ui-motion=off'),css+' motion override missing');
}
assert(read('desktop/local/index.html').includes('type="module" src="view.js"'));
assert(read('desktop/local/view.js').includes("initEffects($('design'))"));
assert(read('desktop/station/view.js').includes("initEffects($('design'))"));
assert(read('desktop/control/view.js').includes("initEffects($('design'))"));
assert(read('desktop/renderer/src/main.tsx').includes('overlay-appearance'));
assert(read('tools/MysticEgo.CloudDesk/update.sh').includes('src/appearance-art/simplified-light.webp'));
console.log('Appearance source contracts: four combinations, settings, motion, assets and desktop entrypoints');

for(const name of ['immersive-dark','immersive-light','simplified-light']){const media=bytes('frontend/packages/ui/src/app/appearance-art/'+name+'.webm');assert.equal(media.subarray(0,4).toString('hex'),'1a45dfa3');assert(media.length>10000)}
for(const role of ['orchestra','global','local','agent','audit']){const png=bytes('frontend/scripts/social-art/'+role+'.png');assert.equal(png.subarray(0,8).toString('hex'),'89504e470d0a1a0a');assert.equal(png.readUInt32BE(16),1200);assert.equal(png.readUInt32BE(20),630)}

// Source-level guards for the shared web controls (visual browser checks remain necessary).
const web=read('frontend/packages/ui/src/app/appearance.css');
const settings=read('frontend/packages/ui/src/pages/Settings.tsx');
assert(settings.includes('className="appearance-art-preview"'));
const preview=web.match(/\.appearance-style-option>\.appearance-art-preview\{([^}]+)\}/)?.[1]??'';
assert(preview.includes('aspect-ratio:21/9')&&preview.includes('background-size:contain')&&preview.includes('background-repeat:no-repeat'));
assert(!/\.appearance-style-option>span\{height:/.test(web));
const quick=web.match(/:root \.appearance-quick>button\.icon-btn\{([^}]+)\}/)?.[1]??'';
assert(quick.includes('border-radius:50%')&&quick.includes('background:transparent'));
assert(web.includes('button.icon-btn[aria-pressed=true]'));
assert(web.includes('.account-avatar>img{display:block;width:100%;height:100%;object-fit:cover'));

const shell=read('frontend/packages/ui/src/app/Shell.tsx');
const workspace=read('frontend/packages/ui/src/app/workspace-polish.css');
assert(shell.includes("import './workspace-polish.css'"));
assert(shell.includes('aria-controls="workspace-navigation"')&&shell.includes('aria-expanded={drawerOpen}'));
assert(shell.includes('inert={narrow&&!drawerOpen}')&&shell.includes('inert={drawerOpen}'));
assert(shell.includes("event.key==='Escape'")&&shell.includes('closeDrawer.current?.focus()'));
assert(shell.includes("document.body.style.overflow=previousOverflow"));
assert(workspace.includes('object-fit:contain')&&workspace.includes('visibility:hidden')&&workspace.includes('visibility:visible'));
assert(workspace.includes('prefers-reduced-motion:reduce')&&workspace.includes('data-ui-motion=off'));
assert(workspace.includes('.auth-recovery')&&workspace.includes('grid-template-columns:minmax(0,1fr)'));

const profile=read('frontend/packages/ui/src/pages/Profile.tsx');
const profileCss=read('frontend/packages/ui/src/pages/profile-polish.css');
const profileDialog=read('frontend/packages/ui/src/components/ProfileDialog.tsx');
const collective=read('frontend/packages/ui/src/pages/Collective.tsx');
const chatCss=read('frontend/packages/ui/src/pages/collective-chat.css');
assert(profile.includes('profile-edit-preview')&&profile.includes('data-custom-cover')&&profile.includes('<ProfileDialog'));
assert(profileDialog.includes('element.showModal()')&&profileDialog.includes('onCancel='));
assert(profileCss.includes('background-size:cover')&&profileCss.includes('dialog.profile-dialog::backdrop'));
assert(collective.includes('chat-compose-tools')&&collective.includes('draftSnapshot.current')&&collective.includes('historyRequest.current!==request'));
assert(chatCss.includes('grid-template-columns:auto minmax(0,1fr) auto')&&chatCss.includes('chat-compose-tools:has(.voice-draft)'));
assert(chatCss.includes('data-ui-motion=off')&&chatCss.includes('prefers-reduced-motion:reduce'));

const desktopCss=read('desktop/control/style.css'),desktopEffects=read('desktop/control/effects.js');
const cloudCss=read('tools/MysticEgo.CloudDesk/src/style.css'),cloudControls=read('tools/MysticEgo.CloudDesk/src/Appearance.jsx');
const overlay=read('desktop/renderer/src/overlay.css'),updater=read('tools/MysticEgo.CloudDesk/update.sh');
assert(desktopEffects.includes('styleButtons.set(key,button)')&&desktopEffects.includes("button.setAttribute('aria-pressed'"));
assert(desktopEffects.includes('state={...defaults}')&&desktopEffects.includes('sync();'));
assert(cloudControls.includes('aria-pressed={look.interfaceStyle===value}')&&cloudControls.includes('useLayoutEffect'));
for(const css of [desktopCss,cloudCss]){
 assert(css.includes('aspect-ratio:21/9')&&css.includes('background-size:contain')&&css.includes('simplified-dark-hero.webp'));
}
assert(desktopCss.includes('object-fit:contain')&&cloudCss.includes('aside .bottom{display:block'));
assert(overlay.includes('simplified-dark-header.webp')&&overlay.includes('border-radius:50%'));
assert(updater.includes('src/appearance-art/simplified-dark.svg')&&updater.includes('src/AuditPortal.jsx'));

const motion=read('frontend/packages/ui/src/app/motion.css');
const notifications=read('frontend/packages/ui/src/app/notifications.tsx');
const cinematic=read('frontend/packages/ui/src/app/CinematicPreview.tsx');
assert(shell.includes("import './motion.css'")&&shell.includes('data-motion-open={open} inert={!open}'));
assert(notifications.includes('data-motion-open={open} inert={!open}')&&notifications.includes('aria-controls={drawerId}'));
assert(motion.includes('@starting-style')&&motion.includes('allow-discrete')&&motion.includes('pointer-events:none'));
assert(motion.includes('var(--mystic-page-time) var(--mystic-ease) backwards')&&motion.includes('--motion-step:0ms'));
assert(motion.includes('data-ui-motion=off')&&motion.includes('prefers-reduced-motion:reduce'));
assert(cinematic.includes('IntersectionObserver')&&cinematic.includes('visibilitychange')&&cinematic.includes('visible&&pageVisible'));
assert(cinematic.includes("state==='playing'")&&cinematic.includes('data-running={running}')&&!cinematic.includes('autoPlay'));
assert(cinematic.includes('loading="lazy"')&&motion.includes('aspect-ratio:16/9')&&motion.includes('object-fit:contain'));

assert(web.includes('var(--mystic-entry-art) center/cover no-repeat')&&web.includes('@media(max-width:780px)'));
assert(motion.includes('data-playing=false]{aspect-ratio:21/9}'));

for(const css of [web,desktopCss,cloudCss])assert(css.includes('var(--mystic-header-art) center bottom/100% auto no-repeat'));
for(const css of [web,cloudCss])assert(css.includes('var(--mystic-sidebar-art) center bottom/contain no-repeat'));
assert(cinematic.includes('Смотреть сцену')&&cinematic.includes('Остановить')&&cinematic.includes('mystic-scene-haze'));
assert(motion.includes('.cinematic-live[data-running=false]')&&motion.includes('animation-play-state:paused')&&motion.includes('aspect-ratio:21/9;max-height:none'));
assert(!cinematic.includes('.webm')&&cinematic.includes('key={poster+\':\'+allowed}'));

const dashboardCss=read('frontend/packages/ui/src/components/dashboard.css');
const dashboardHero=read('frontend/packages/ui/src/components/DashboardHero.tsx');
assert(dashboardHero.includes('className="dashboard-landscape"')&&dashboardCss.includes('aspect-ratio:var(--mystic-overview-ratio,3/1)'));
assert(dashboardCss.includes('var(--mystic-overview-art) center/100% auto no-repeat'));
assert(!dashboardCss.includes('var(--mystic-art) right center/auto 100%'));
assert(web.includes('--mystic-bg:#eae7ef')&&web.includes('--mystic-veil:#f4f1f65c'));
assert(!shell.includes('style={bannerStyle(personal?.profile.coverUrl)}'));
assert(profileCss.includes('var(--mystic-profile-art)'));
for(const [name,width,height] of [
 ['simplified-dark-overview.webp',2172,724],['simplified-light-overview.webp',2172,724],['immersive-dark-overview.webp',2172,724],['immersive-light-overview.webp',1916,821],
 ['simplified-dark-profile.webp',1916,821],['light-profile.webp',1916,821],['immersive-dark-profile.webp',1916,821]
]){
 const art=bytes(groups[0][1]+'/'+name);
 assert(web.includes(name),name+' missing from appearance mapping');
 assert.equal(art.subarray(0,4).toString(),'RIFF');assert.equal(art.subarray(8,12).toString(),'WEBP');
 assert.equal(art.subarray(12,16).toString(),'VP8 ');assert.equal(art.subarray(23,26).toString('hex'),'9d012a');
 assert.equal(art.readUInt16LE(26)&0x3fff,width);assert.equal(art.readUInt16LE(28)&0x3fff,height);
}

const landing=read('frontend/packages/ui/src/pages/Studio.tsx');
const landingCss=read('frontend/packages/ui/src/pages/studio.css');
assert(landing.includes('data-landing="studio"')&&landing.includes('data-landing="developer"')&&landing.includes('id="experience"')&&landing.includes('<StudioEconomics/>'));
assert(landingCss.includes('min-height:calc(100svh - 76px)')&&landingCss.includes('max-width:none;width:100%;padding:0'));
assert(web.includes('background:transparent;color:#fff9ff;text-shadow:')&&web.includes('--landing-art:var(--mystic-profile-art)'));
