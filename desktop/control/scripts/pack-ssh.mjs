import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
const control = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = path.resolve(control, '../..');
const { build } = createRequire(path.join(root, 'desktop/package.json'))('esbuild');
const dest = path.join(root, 'runtime/control-deps');
fs.mkdirSync(dest, {recursive:true});
await build({entryPoints:[path.join(control,'node_modules/ssh2/lib/index.js')],bundle:true,platform:'node',target:'node22',format:'cjs',outfile:path.join(dest,'ssh.cjs'),external:['*.node','cpu-features'],legalComments:'eof'});
const names = ['ssh2','asn1','bcrypt-pbkdf','safer-buffer','tweetnacl'];
const notices = names.map(name=>{
  const dir=path.join(control,'node_modules',name);
  const file=fs.readdirSync(dir).find(n=>/^(licen[sc]e|copying)(\.|$)/i.test(n));
  return name+'\n'+(file?fs.readFileSync(path.join(dir,file),'utf8'):fs.readFileSync(path.join(dir,'index.js'),'utf8').split("'use strict'")[0].slice(0,4500));
});
fs.writeFileSync(path.join(dest,'LICENSES.txt'),notices.join('\n\n'));
