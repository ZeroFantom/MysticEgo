#!/usr/bin/env bash
set -Eeuo pipefail
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
target=$HOME/Downloads/MysticEgo.CloudDesk
launch=0
for arg in "$@"; do
  if [[ $arg == --launch ]]; then launch=1; elif [[ $arg == --help ]]; then echo 'bash update.sh [путь к установленному CloudDesk] [--launch]'; exit 0; else target=$arg; fi
done
[[ $EUID != 0 ]] || { echo 'Не запускайте обновление от root.' >&2; exit 1; }
[[ -f "$target/package.json" && -f "$target/backend/CloudDesk.csproj" ]] || { echo "Проект не найден: $target" >&2; exit 1; }
target=$(cd -- "$target" && pwd)
[[ "$target" != "$here" ]] || { echo 'Укажите прежнюю установку CloudDesk.' >&2; exit 1; }
[[ $(node -p 'require(process.argv[1]).name' "$target/package.json") == mysticego-clouddesk ]] || { echo 'Это не CloudDesk.' >&2; exit 1; }
if command -v dotnet >/dev/null; then export CLOUDDESK_DOTNET=$(command -v dotnet); elif [[ -x $HOME/.dotnet/dotnet ]]; then export CLOUDDESK_DOTNET=$HOME/.dotnet/dotnet; else echo 'Нет .NET SDK 10' >&2; exit 1; fi
files=(backend/DnsDeletion.cs backend/DeploymentBinding.cs backend/PublicDns.cs github-network-check.sh backend/GithubConnection.cs backend/Locations.cs backend/OrderSafety.cs backend/Program.cs backend/TimewebDiagnostics.cs backend/Monitor.cs backend/DeveloperSession.cs backend/Domains.cs backend/Discovery.cs backend/DnsManagement.cs desktop/main.cjs desktop/preload.cjs src/main.jsx src/Infrastructure.jsx src/Provision.jsx src/DomainPicker.jsx src/DnsCleanup.jsx src/style.css src/Appearance.jsx src/appearance-art/immersive-dark.webp src/appearance-art/immersive-light.webp src/appearance-art/simplified-dark.webp src/appearance-art/simplified-light.webp src/appearance-art/immersive-dark-entry.webp src/appearance-art/immersive-light-entry.webp run.sh run-windows.cmd ensure-hosts.sh ensure-hosts.ps1 orchestra-host.txt PORTABLE.md package.json README.md index.html)
backup=$(mktemp -d "$target/.backup-v046-XXXXXXXX")
for f in "${files[@]}"; do mkdir -p -- "$backup/$(dirname "$f")"; [[ ! -e "$target/$f" ]] || cp -a -- "$target/$f" "$backup/$f"; done
stage=$(mktemp -d "$target/.build-v046-XXXXXXXX")
rollback(){ local rc=$?; trap - ERR; echo "Обновление прервано. Восстанавливаем из $backup" >&2; for f in "${files[@]}"; do if [[ -e "$backup/$f" ]]; then cp -a -- "$backup/$f" "$target/$f"; else rm -f -- "$target/$f"; fi; done; for d in out dist; do if [[ -e "$backup/$d" ]]; then rm -rf -- "$target/$d"; mv -- "$backup/$d" "$target/$d"; elif [[ -e "$stage/.new-$d" ]]; then rm -rf -- "$target/$d"; fi; done; rm -rf -- "$stage"; exit "$rc"; }
trap rollback ERR
for f in "${files[@]}"; do mkdir -p -- "$target/$(dirname "$f")"; cp -- "$here/$f" "$target/$f"; done
chmod +x "$target/run.sh" "$target/github-network-check.sh"
cd -- "$target"
if [[ -f "$here/out/CloudDesk.dll" && -f "$here/dist/index.html" ]]; then
  cp -a -- "$here/out" "$here/dist" "$stage/"
else
  "$CLOUDDESK_DOTNET" publish backend/CloudDesk.csproj -c Release -o "$stage/out"
  npm install --ignore-scripts --include=dev --include=optional
  npm run build -- --outDir "$stage/dist"
fi
# Non-network IPC smoke test. No SSH key creation, cloud calls or secrets.
node - "$CLOUDDESK_DOTNET" "$stage/out/CloudDesk.dll" <<'JS'
const {spawn}=require('node:child_process');
const child=spawn(process.argv[2],[process.argv[3]],{stdio:['pipe','pipe','pipe']});
let out='',err='';const t=setTimeout(()=>{child.kill('SIGKILL');console.error('IPC smoke timeout');process.exitCode=1},15000);
child.stdout.on('data',d=>out+=d);child.stderr.on('data',d=>err+=d);
child.on('error',e=>{clearTimeout(t);console.error(e.message);process.exitCode=1});
child.on('close',code=>{clearTimeout(t);try{const responses=out.trim().split('\n').map(JSON.parse);const res=responses.find(r=>r.id==='smoke'),mon=responses.find(r=>r.id==='monitor-smoke'),cat=responses.find(r=>r.id==='catalog-smoke'),dns=responses.find(r=>r.id==='dns-smoke'),naming=responses.find(r=>r.id==='naming-smoke'),tw=responses.find(r=>r.id==='tw-smoke'),gh=responses.find(r=>r.id==='gh-smoke'),order=responses.find(r=>r.id==='order-smoke'),binding=responses.find(r=>r.id==='binding-smoke'),deletion=responses.find(r=>r.id==='dns-delete-smoke');if(code!==0||!res||res.ok!==false||!res.error.includes('host')||!mon?.ok||mon.result?.passed!==2||mon.result?.developerChecks!==5||!cat?.ok||cat.result?.passed!==17||!dns?.ok||dns.result?.passed!==8||!naming?.ok||naming.result?.passed!==4||!tw?.ok||tw.result?.passed!==6||!gh?.ok||gh.result?.passed!==8||!order?.ok||order.result?.passed!==39||!binding?.ok||binding.result?.passed!==14||!deletion?.ok||deletion.result?.passed!==11)throw Error('Unexpected RPC result');console.log('SSH + monitor IPC + catalog + DNS regression: OK');}catch(e){console.error(e.message,err);process.exitCode=1;}});
child.stdin.end(JSON.stringify({id:'smoke',action:'ssh.status',args:{host:'',user:'root',port:'22'}})+'\n'+JSON.stringify({id:'monitor-smoke',action:'topology.selftest',args:{}})+'\n'+JSON.stringify({id:'catalog-smoke',action:'catalog.selftest',args:{}})+'\n'+JSON.stringify({id:'dns-smoke',action:'dns.selftest',args:{}})+'\n'+JSON.stringify({id:'naming-smoke',action:'naming.selftest',args:{}})+'\n'+JSON.stringify({id:'tw-smoke',action:'timeweb.selftest',args:{}})+'\n'+JSON.stringify({id:'gh-smoke',action:'github.selftest',args:{}})+'\n'+JSON.stringify({id:'order-smoke',action:'order.selftest',args:{}})+'\n'+JSON.stringify({id:'binding-smoke',action:'binding.selftest',args:{}})+'\n'+JSON.stringify({id:'dns-delete-smoke',action:'dns.delete.selftest',args:{}})+'\n');
JS
for d in out dist; do if [[ -e "$target/$d" ]]; then mv -- "$target/$d" "$backup/$d"; else touch "$stage/.new-$d"; fi; mv -- "$stage/$d" "$target/$d"; done
trap - ERR
rm -rf -- "$stage"
echo "Обновлено до 0.4.7. Резервная копия: $backup"
echo 'Electron, ключи и токены не заменялись.'
if (( launch )); then export CLOUDDESK_RUNTIME_DIR="$(cd -- "$here/../.." && pwd -P)/runtime"; exec bash run.sh; fi
echo "Запуск: cd '$target' && bash run.sh"
