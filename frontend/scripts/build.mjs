import {build} from 'vite';
import react from '@vitejs/plugin-react';
import {cp, mkdir, readFile, writeFile, rm, mkdtemp} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';

const root=fileURLToPath(new URL('../',import.meta.url));
const temp=await mkdtemp(join(tmpdir(),'mysticego-ui-'));
const titles={orchestra:'Orchestra',global:'Global',local:'Local',agent:'Agent',audit:'AuditOrchestra'};
try{
  await build({root,plugins:[react()],publicDir:false,build:{outDir:temp,emptyOutDir:true}});
  await cp(join(root,'faq'),join(temp,'faq'),{recursive:true});
  const template=await readFile(join(temp,'index.html'),'utf8');
  for(const [role,title] of Object.entries(titles)){
    const app=join(root,'apps',role);
    const dist=join(app,'dist');
    await rm(dist,{recursive:true,force:true});await mkdir(dist,{recursive:true});
    await cp(temp,dist,{recursive:true});await cp(join(app,'public'),dist,{recursive:true});
    await writeFile(join(dist,'index.html'),template.replace('data-role="orchestra"','data-role="'+role+'"').replace('<title>MysticEgo</title>','<title>MysticEgo · '+title+'</title>'));
  }
}finally{await rm(temp,{recursive:true,force:true})}
