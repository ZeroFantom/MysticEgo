import {build} from 'vite';
import react from '@vitejs/plugin-react';
import {cp, mkdir, readFile, writeFile, rm, mkdtemp} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {roles,publicPages,panelHtml,auditCaddyHtml,writeSocialAssets} from './social.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
const temp=await mkdtemp(join(tmpdir(),'mysticego-ui-'));

try{
  await build({root,plugins:[react()],publicDir:false,build:{outDir:temp,emptyOutDir:true}});
  await cp(join(root,'faq'),join(temp,'faq'),{recursive:true});
  const template=await readFile(join(temp,'index.html'),'utf8');
  await writeSocialAssets(temp);
  for(const role of Object.keys(roles)){
    const app=join(root,'apps',role);
    const dist=join(app,'dist');
    await rm(dist,{recursive:true,force:true});await mkdir(dist,{recursive:true});
    await cp(temp,dist,{recursive:true});await cp(join(app,'public'),dist,{recursive:true});
    await writeFile(join(dist,'index.html'),panelHtml(template,role));
    await mkdir(join(dist,'social'),{recursive:true});
    for(const page of ['share',...(role==='orchestra'?Object.keys(publicPages):[])])
      await writeFile(join(dist,'social',page+'.html'),panelHtml(template,role,page));
    if(role==='audit')for(const page of ['root','share'])
      await writeFile(join(dist,'social','audit-caddy-'+page+'.html'),auditCaddyHtml(template,page));
  }
}finally{await rm(temp,{recursive:true,force:true})}
