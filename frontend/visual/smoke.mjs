import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile,stat,mkdir,writeFile} from 'node:fs/promises';
import {resolve,extname,sep} from 'node:path';
import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright';

const here=fileURLToPath(new URL('.',import.meta.url));
const output=resolve(process.env.VISUAL_OUTPUT||resolve(here,'artifacts'));
await mkdir(output,{recursive:true});
const roles=['orchestra','global','local','agent','audit'];
const sizes=[{width:390,height:844},{width:768,height:1024},{width:1440,height:900}];
const combinations=[['dark','simplified'],['dark','immersive'],['light','immersive'],['light','simplified']];
const mime={'.html':'text/html; charset=utf-8','.js':'text/javascript','.mjs':'text/javascript','.css':'text/css','.json':'application/json','.png':'image/png','.svg':'image/svg+xml','.webp':'image/webp','.webm':'video/webm','.ico':'image/x-icon','.woff2':'font/woff2'};
async function serve(role){
  const root=resolve(here,'../apps',role,'dist');
  await stat(resolve(root,'index.html'));
  const server=createServer(async(req,res)=>{
    try{
      const pathname=decodeURIComponent(new URL(req.url,'http://localhost').pathname);
      const filename=resolve(root,'.'+pathname);
      if(filename!==root&&!filename.startsWith(root+sep)){res.writeHead(403).end();return}
      let file=filename;
      if(!(await stat(file).catch(()=>null))?.isFile()){
        if(extname(pathname)){res.writeHead(404).end();return}
        file=resolve(root,'index.html');
      }
      res.writeHead(200,{'Content-Type':mime[extname(file)]||'application/octet-stream','Cache-Control':'no-store'}).end(await readFile(file));
    }catch{res.writeHead(500).end()}
  });
  await new Promise((ok,fail)=>{server.once('error',fail);server.listen(0,'127.0.0.1',ok)});
  return {server,origin:'http://127.0.0.1:'+server.address().port};
}
const now=new Date().toISOString();
const avatar='/mystic-mark.png';
const profile={profile:{id:'visual-user',displayName:'Визуальный тест MysticEgo',bio:'Синтетический профиль для проверки интерфейса.',statusText:'Проверка отображения',avatarUrl:avatar,backgroundUrl:'',coverUrl:'',accent:'#b49bf7',discoverable:true},game:{id:'visual-user',nickname:'VisualUser',rank:'Странник',level:3,state:'active',created:now,immortal:false,ageHours:72},stats:[],personality:{version:'fixture',completed:false,axes:[]}};
const contact={id:'contact-1',status:'accepted',incoming:false,person:{id:'visual-peer',nickname:'VisualPeer',displayName:'Тестовый контакт',statusText:'В сети',avatarUrl:avatar},last:{text:'Сообщение для визуального теста',at:now,mine:false}};
const messages=[{id:'m1',sender:'visual-peer',recipient:'visual-user',text:'Сообщение для визуального теста',at:now},{id:'m2',sender:'visual-user',recipient:'visual-peer',text:'Ответ с длинной строкой: '+ 'MysticEgo'.repeat(12),at:now}];
const sample=(seconds,cpuPercent,hostCpuPercent)=>({at:new Date(Date.now()+seconds*1000).toISOString(),cpuPercent,hostCpuPercent,memoryBytes:450*1048576,managedBytes:200*1048576,memoryLimitBytes:1024*1048576,rxBytesPerSecond:102400,txBytesPerSecond:20480,diskFreeBytes:40*1073741824,diskTotalBytes:80*1073741824,uptimeSeconds:1200});
const topology=[{node:{id:'g1',kind:'global',name:'Тестовая организация Global',parentId:'root',ownerId:'other',mode:'native',enabled:true},people:12,sites:2,report:{seen:now,healthy:['global']}},{node:{id:'l1',kind:'local',name:'Тестовая площадка Local',parentId:'g1',mode:'native',enabled:true},people:4,sites:0},{node:{id:'a1',kind:'agent',name:'Рабочая станция Agent',parentId:'l1',mode:'native',enabled:false},people:0,sites:0}];
const token='fixture.'+Buffer.from(JSON.stringify({sub:'visual-user'})).toString('base64url')+'.fixture';
function apiResponse(path,origin){
  if(path==='/api/auth/login'||path==='/api/auth/refresh')return {accessToken:token,refreshToken:'fixture-only',user:{login:'visual-user'},expiresAt:new Date(Date.now()+3600000).toISOString()};
  const routes={
    '/api/v1/collective/me':profile,'/api/v1/collective/notifications':[],'/api/v1/collective/shard/quests':[],
    '/api/v1/collective/contacts':[contact],'/api/v1/collective/messages/visual-peer':messages,
    '/api/v1/managed/context':{rootId:'root',rootName:'Оркестр',orchestraUrl:origin,authUrl:origin,developer:false,owner:false,actor:'visual-user',lan:false},
    '/api/v1/managed/topology':topology,'/api/v1/managed/development-team':{owner:false},
    '/api/v1/managed/applications':[],'/api/v1/public/project-release':{latest:null},'/api/v1/guest/availability':{available:false},
    '/api/v1/orchestra/telemetry':{os:'Fixture Linux',processors:8,observabilityAvailable:false,samples:[sample(-65,20,40),sample(-60,32,46),sample(-55,12,null),sample(-25,72,55),sample(-20,55,60),sample(-15,34,48),sample(-10,64,55),sample(-5,40,42),sample(0,55,50)],links:[]},
    '/api/v1/overview':{node:'visual',kind:'agent',agentsOnline:2,nodes:3,records:4,at:now},
    '/api/v1/nodes':[],'/api/v1/agents':[],'/api/v1/diagnostics/links':[],'/api/v1/audit/page':{items:[]}
  };
  return routes[path];
}
async function isolate(page,origin){
  await page.route('**/*',async route=>{
    const url=new URL(route.request().url());
    if(url.origin===origin&&url.pathname==='/mystic-config.js')return route.fulfill({contentType:'text/javascript',body:'window.__MYSTIC_CONFIG__='+JSON.stringify({apiUrl:origin,authUrl:origin})});
    if(url.pathname.startsWith('/api/')){
      const value=apiResponse(url.pathname,origin);
      return route.fulfill({status:value===undefined?404:200,contentType:'application/json',body:JSON.stringify(value??{error:'Fixture endpoint not implemented'})});
    }
    if(url.origin!==origin)return route.abort();
    return route.continue();
  });
}
const results=[];
async function check(page,label,fn){
  try{await fn();results.push({label,passed:true});console.log('PASS',label)}
  catch(error){results.push({label,passed:false,error:String(error)});console.error('FAIL',label,error);await page.screenshot({path:resolve(output,label+'-failure.png'),animations:'disabled',timeout:5000}).catch(()=>{})}
}
async function visible(locator){await locator.waitFor({state:'visible',timeout:8000})}
async function rootState(page,theme,style){await page.waitForFunction(([t,s])=>document.documentElement.dataset.theme===t&&document.documentElement.dataset.interface===s,[theme,style])}
async function switchAppearance(page,theme,style){
  const state=await page.locator('html').evaluate(el=>({theme:el.dataset.theme,style:el.dataset.interface}));
  if(state.theme!==theme)await page.locator('.appearance-quick button').first().click();
  if(state.style!==style)await page.getByRole('button',{name:'Насыщенный стиль',exact:true}).click();
  await rootState(page,theme,style);
  const toggle=page.getByRole('button',{name:'Насыщенный стиль',exact:true});
  assert.equal(await toggle.getAttribute('aria-pressed'),String(style==='immersive'));
  const shape=await toggle.evaluate(el=>{const r=el.getBoundingClientRect(),c=getComputedStyle(el);return {width:r.width,height:r.height,radius:parseFloat(c.borderTopLeftRadius),raw:c.borderTopLeftRadius}});
  assert(Math.abs(shape.width-shape.height)<1,'style control must be square before rounding');
  assert(shape.raw.endsWith('%')?shape.radius>=50:shape.radius>=shape.width/2-1,'style control must be circular');
}
async function checkHeaderNotifications(page,header){
  const bell=page.locator(header+' .notification-float[data-placement=header] > .bell-button');
  await visible(bell);
  assert.equal(await bell.locator('..').evaluate(el=>getComputedStyle(el).position),'relative','notification control must not float over page inputs');
  await bell.click();
  const drawer=page.locator(header+' .notification-drawer.motion-disclosure');
  await visible(drawer);
  const bounds=await drawer.evaluate(el=>{const r=el.getBoundingClientRect();return {left:r.left,right:r.right,width:innerWidth}});
  assert(bounds.left>=-1&&bounds.right<=bounds.width+1,'notification drawer exceeds viewport '+JSON.stringify(bounds));
  await page.keyboard.press('Escape');await drawer.waitFor({state:'hidden'});
  assert.equal(await bell.getAttribute('aria-expanded'),'false');
}
async function checkArtwork(page,theme,style,selector,kind){
  const data=await page.evaluate(async({theme,style,selector,kind})=>{
    const root=getComputedStyle(document.documentElement);
    const url=raw=>{const match=raw.match(/url\(["']?([^"')]+)["']?\)/);return match?new URL(match[1],document.baseURI).href:null};
    const hero=url(root.getPropertyValue('--mystic-art')),entry=url(root.getPropertyValue('--mystic-entry-art')),header=url(root.getPropertyValue('--mystic-header-art')),sidebar=url(root.getPropertyValue('--mystic-sidebar-art')),overview=url(root.getPropertyValue('--mystic-overview-art')),profile=url(root.getPropertyValue('--mystic-profile-art'));
    const load=async src=>{if(!src)throw Error('Missing artwork URL');const img=new Image();img.src=src;await img.decode();return [img.naturalWidth,img.naturalHeight]};
    const element=document.querySelector(selector);
    if(!element)throw Error('Missing artwork surface '+selector);
    const css=getComputedStyle(element),active=url(css.backgroundImage),rect=element.getBoundingClientRect();
    const expected={hero,entry,header,sidebar,overview,profile}[kind];
    return {hero,entry,header,sidebar,overview,profile,active,expected,heroSize:await load(hero),entrySize:await load(entry),headerSize:await load(header),sidebarSize:await load(sidebar),overviewSize:await load(overview),profileSize:await load(profile),prefix:style+'-'+theme,fit:css.backgroundSize,position:css.backgroundPosition,ratio:rect.width/rect.height,background:root.getPropertyValue('--mystic-bg').trim(),surface:root.getPropertyValue('--mystic-surface').trim()};
  },{theme,style,selector,kind});
  assert.deepEqual(data.heroSize,[1916,821]);assert.deepEqual(data.entrySize,[1122,1402]);
  assert(data.hero.includes(data.prefix+'-hero')&&data.entry.includes(data.prefix+'-entry'),'Incorrect theme artwork');
  assert(data.header.includes(data.prefix+'-header')&&data.sidebar.includes(data.prefix+'-sidebar'),'Wrong narrow artwork');
  assert(data.headerSize[0]/data.headerSize[1]>8&&data.sidebarSize[0]/data.sidebarSize[1]<.25,'Incorrect narrow artwork proportions');
  assert.equal(data.active,data.expected,'Incorrect artwork for '+selector);
  assert.equal(new Set([data.hero,data.entry,data.header,data.sidebar,data.overview,data.profile]).size,6,'Artwork repeated across different surfaces');
  assert(data.overview.includes(data.prefix+'-overview'));
  assert.deepEqual(data.overviewSize,theme==='light'&&style==='immersive'?[1916,821]:[2172,724]);
  assert.deepEqual(data.profileSize,[1916,821]);
  if(kind==='header'||selector==='.dashboard-landscape'){
    // A color-only final background layer serializes as ", auto" in Chromium.
    const imageFit=data.fit.split(',')[0].trim();
    assert(['100%','100% auto'].includes(imageFit),'Image must span the entire width: '+data.fit);
  }
  if(selector==='.dashboard-landscape')assert(Math.abs(data.ratio-data.overviewSize[0]/data.overviewSize[1])<.06,'Overview image box must preserve native proportions: '+data.ratio);
  if(theme==='light'){assert.equal(data.background,'#eae7ef');assert.equal(data.surface,'#f4f1f6')}

}
async function capture(page,label){
  await page.evaluate(()=>document.fonts.ready);
  await page.screenshot({path:resolve(output,label+'.png'),animations:'disabled',fullPage:true,timeout:15000});
}
async function noHorizontalOverflow(page){
  const layout=await page.evaluate(()=>({width:document.documentElement.clientWidth,scroll:document.documentElement.scrollWidth}));
  assert(layout.scroll<=layout.width+2,'horizontal document overflow: '+JSON.stringify(layout));
}
const browser=await chromium.launch({headless:true});
try{
  for(const role of roles){
    const {server,origin}=await serve(role);
    try{
      for(const viewport of sizes){
        const context=await browser.newContext({viewport,locale:'ru-RU',timezoneId:'UTC',colorScheme:'dark',reducedMotion:'reduce'});
        const page=await context.newPage();page.setDefaultTimeout(8000);
        const errors=[];page.on('pageerror',e=>errors.push(String(e)));
        const prefix=role+'-'+viewport.width;
        try{
          await isolate(page,origin);
          await page.goto(origin+'/settings',{waitUntil:'domcontentloaded'});
          await visible(page.locator('.auth-form-card'));
          for(const [theme,style] of combinations)await check(page,prefix+'-login-'+theme+'-'+style,async()=>{
            await switchAppearance(page,theme,style);await checkArtwork(page,theme,style,'.auth-visual',viewport.width<=780?'hero':'entry');
            const typography=await page.locator('.auth-visual :is(.auth-brand,.auth-role)').evaluateAll(items=>items.map(el=>{const c=getComputedStyle(el);return {bg:c.backgroundColor,border:c.borderTopWidth,padding:c.paddingTop,color:c.color}}));
            assert.equal(typography.length,2);
            for(const item of typography){assert.equal(item.bg,'rgba(0, 0, 0, 0)');assert.equal(item.border,'0px');assert.equal(item.padding,'0px');assert.equal(item.color,'rgb(255, 249, 255)')}
            await noHorizontalOverflow(page);await capture(page,prefix+'-login-'+theme+'-'+style);
          });
          await check(page,prefix+'-login-notifications',async()=>{
            await checkHeaderNotifications(page,'.auth-top-actions');
            const reveal=page.getByRole('button',{name:'Показать пароль',exact:true});
            await reveal.click();assert.equal(await page.locator('#auth-password').getAttribute('type'),'text');
            await page.getByRole('button',{name:'Скрыть пароль',exact:true}).click();
            assert.equal(await page.locator('#auth-password').getAttribute('type'),'password');
          });
          await check(page,prefix+'-authenticated',async()=>{
            await page.locator('#auth-login').fill('visual-user');await page.locator('#auth-password').fill('fixture-password');
            await page.locator('.auth-submit').click();await visible(page.locator('.mystic-workspace'));
            await visible(page.locator('.appearance-style-options'));
          });
          if(await page.locator('.appearance-style-options').isVisible()){
            for(const [theme,style] of combinations)await check(page,prefix+'-settings-'+theme+'-'+style,async()=>{
              await switchAppearance(page,theme,style);
              const previews=await page.locator('.appearance-art-preview').evaluateAll(items=>items.map(el=>{const r=el.getBoundingClientRect(),c=getComputedStyle(el);return {ratio:r.width/r.height,fit:c.backgroundSize,repeat:c.backgroundRepeat}}));
              assert.equal(previews.length,2);for(const preview of previews){assert(Math.abs(preview.ratio-21/9)<.08);assert.equal(preview.fit,'contain');assert.equal(preview.repeat,'no-repeat')}
              await noHorizontalOverflow(page);await capture(page,prefix+'-settings-'+theme+'-'+style);
            });
            if(viewport.width<=850)await check(page,prefix+'-drawer',async()=>{
              const trigger=page.getByRole('button',{name:'Открыть меню'});await trigger.click();
              await visible(page.getByRole('dialog',{name:'Навигация MysticEgo'}));
              assert(await page.locator('.main').evaluate(el=>el.inert));
              await page.keyboard.press('Escape');
              await page.waitForFunction(()=>document.querySelector('.main')?.inert===false);
              assert.equal(await trigger.getAttribute('aria-expanded'),'false');
            });
            await check(page,prefix+'-appearance-persistence',async()=>{
              await page.reload({waitUntil:'domcontentloaded'});
              await visible(page.locator('.appearance-style-options'));
              await rootState(page,'light','simplified');
            });
            if(role==='orchestra'&&viewport.width===1440)await check(page,prefix+'-motion-preferences',async()=>{
              await page.emulateMedia({reducedMotion:'no-preference'});
              const select=page.getByLabel('Анимации',{exact:true});
              await select.selectOption('off');
              await page.waitForFunction(()=>document.documentElement.dataset.uiMotion==='off');
              const duration=await page.locator('.account-trigger').evaluate(el=>getComputedStyle(el).transitionDuration);
              assert(duration.split(',').every(v=>parseFloat(v)===0),'off must disable control transitions');
              await select.selectOption('normal');
              await page.waitForFunction(()=>document.documentElement.dataset.uiMotion==='normal');
              await page.emulateMedia({reducedMotion:'reduce'});
            });
            await check(page,prefix+'-workspace-notifications',async()=>{
              await checkHeaderNotifications(page,'.top-actions');
            });
            await check(page,prefix+'-account',async()=>{
              const trigger=page.locator('.account-trigger');await trigger.click();await visible(page.locator('.account-popover'));
              await page.keyboard.press('Escape');await page.locator('.account-popover').waitFor({state:'hidden'});
              assert.equal(await trigger.getAttribute('aria-expanded'),'false');
            });
            await check(page,prefix+'-overview',async()=>{
              await page.goto(origin+'/',{waitUntil:'domcontentloaded'});
              await visible(page.locator('.dashboard-hero'));
              if(['orchestra','global','local'].includes(role))await visible(page.locator('.dashboard-network .network-card').first());
              if(role==='orchestra')await visible(page.locator('.dashboard-monitor-grid .chart-gap-bridge').first());
              for(const [theme,style] of combinations){
                await switchAppearance(page,theme,style);
                await noHorizontalOverflow(page);
                await checkArtwork(page,theme,style,'.dashboard-landscape','overview');
                await checkArtwork(page,theme,style,'.orchestral-topbar','header');
                await checkArtwork(page,theme,style,'.sidebar','sidebar');
                await capture(page,prefix+'-overview-'+theme+'-'+style);
              }
            });
            if(role==='orchestra')for(const [route,selector] of [['profile','.profile-hero'],['nodes','.network-card'],['diagnostics','.chart-line'],['collective','.chat-thread']])await check(page,prefix+'-'+route,async()=>{
              await page.goto(origin+'/'+route,{waitUntil:'domcontentloaded'});await visible(page.locator(selector).first());
              if(route==='profile'){
                for(const [theme,style] of combinations){
                  await switchAppearance(page,theme,style);
                  await checkArtwork(page,theme,style,'.profile-hero','profile');
                  await noHorizontalOverflow(page);
                  await capture(page,prefix+'-profile-'+theme+'-'+style);
                }
                await page.getByRole('button',{name:'Оформление',exact:true}).click();await visible(page.getByRole('dialog',{name:'Оформление профиля'}));await capture(page,prefix+'-profile-dialog');await page.keyboard.press('Escape');await page.getByRole('dialog',{name:'Оформление профиля'}).waitFor({state:'hidden'})}
              if(route==='diagnostics'){await visible(page.locator('.chart-gap-bridge').first());assert.equal(await page.locator('.chart-line').first().getAttribute('class'),'chart-line')}
              if(route==='collective'){await page.locator('.chat-thread').first().click();await visible(page.getByRole('textbox',{name:'Сообщение',exact:true}));await visible(page.locator('.chat-message').first());await page.getByRole('button',{name:'Эмодзи'}).click();await visible(page.locator('.emoji-menu'));await page.keyboard.press('Escape');await page.locator('.emoji-menu').waitFor({state:'hidden'})}
              await capture(page,prefix+'-'+route);
            });
          }
          if(role==='orchestra')for(const route of ['studio','developer'])await check(page,prefix+'-landing-'+route,async()=>{
            await page.goto(origin+'/'+route,{waitUntil:'domcontentloaded'});
            const hero=page.locator('.landing-hero'),nav=page.locator('.studio-nav');
            await visible(hero);
            for(const [theme,style] of combinations){
              await switchAppearance(page,theme,style);
              await checkArtwork(page,theme,style,'.landing-hero',route==='studio'?'overview':'profile');
              const size=await hero.evaluate(el=>{const r=el.getBoundingClientRect(),n=document.querySelector('.studio-nav').getBoundingClientRect(),copy=getComputedStyle(el.querySelector('.landing-hero-copy'));return {width:r.width,height:r.height,navHeight:n.height,viewportWidth:document.documentElement.clientWidth,viewportHeight:innerHeight,copyBg:copy.backgroundColor,copyBorder:copy.borderTopWidth}});
              assert(Math.abs(size.width-size.viewportWidth)<=2,'Landing must span viewport width');
              assert(size.height+size.navHeight>=size.viewportHeight-45,'First screen must fill viewport');
              assert.equal(size.copyBg,'rgba(0, 0, 0, 0)');assert.equal(size.copyBorder,'0px');
              await noHorizontalOverflow(page);
              await capture(page,prefix+'-landing-'+route+'-'+theme+'-'+style);
            }
            const target=route==='studio'?'projects':'experience';
            await hero.locator('a[href="#'+target+'"]').click();
            assert.equal(new URL(page.url()).hash,'#'+target);
            await visible(page.locator('#'+target));
            if(route==='studio'){await visible(page.locator('#model'));await visible(page.locator('#partners'))}
            else await visible(page.locator('#developer-contact'));
          });
          if(role==='orchestra'&&viewport.width===1440)await check(page,prefix+'-cinematic',async()=>{
            await page.goto(origin+'/studio',{waitUntil:'domcontentloaded'});
            const scene=page.locator('.cinematic-live');
            await visible(scene);
            await scene.scrollIntoViewIfNeeded();
            assert.equal(await scene.getAttribute('data-playing'),'false');
            assert.equal(await page.locator('.cinematic-effects').count(),0);
            await page.emulateMedia({reducedMotion:'no-preference'});
            for(const [theme,style] of combinations){
              await switchAppearance(page,theme,style);
              await scene.scrollIntoViewIfNeeded();
              await checkArtwork(page,theme,style,'.studio-hero-cover','overview');
              const image=await scene.locator('img').evaluate(async img=>{await img.decode();const rect=img.getBoundingClientRect();return {src:img.currentSrc,ratio:rect.width/rect.height,fit:getComputedStyle(img).objectFit}});
              assert(image.src.includes(style+'-'+theme+'-hero'));
              assert(Math.abs(image.ratio-21/9)<.08&&image.fit==='contain');
              await page.getByRole('button',{name:'Смотреть сцену',exact:true}).click();
              await page.waitForFunction(()=>document.querySelector('.cinematic-live')?.getAttribute('data-running')==='true');
              await page.getByRole('button',{name:'Пауза',exact:true}).click();
              assert.equal(await scene.getAttribute('data-running'),'false');
              await page.getByRole('button',{name:'Продолжить',exact:true}).click();
              await page.waitForFunction(()=>document.querySelector('.cinematic-live')?.getAttribute('data-running')==='true');
              await page.getByRole('button',{name:'Остановить',exact:true}).click();
              assert.equal(await page.locator('.cinematic-effects').count(),0);
              await capture(page,prefix+'-cinematic-'+theme+'-'+style);
            }
            await page.getByRole('button',{name:'Смотреть сцену',exact:true}).click();
            await page.emulateMedia({reducedMotion:'reduce'});
            await page.waitForFunction(()=>!document.querySelector('.cinematic-effects'));
            assert.equal(await page.getByRole('button',{name:'Смотреть сцену',exact:true}).count(),0);
          });
          await check(page,prefix+'-runtime',async()=>assert.deepEqual(errors,[],'uncaught browser exceptions'));
        }catch(error){results.push({label:prefix+'-setup',passed:false,error:String(error)});await page.screenshot({path:resolve(output,prefix+'-setup-failure.png'),timeout:5000}).catch(()=>{})}
        finally{await context.close()}
      }
    }finally{await new Promise(ok=>server.close(ok))}
  }
}finally{
  await browser.close();
  await writeFile(resolve(output,'results.json'),JSON.stringify(results,null,2));
}
const failed=results.filter(r=>!r.passed);
console.log(results.length+' checks; '+failed.length+' failed');
if(failed.length)process.exitCode=1;
