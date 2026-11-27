import {spawn} from 'node:child_process';

export async function json(url, options={}) {
  const response = await fetch(url, {signal:AbortSignal.timeout(10000),redirect:'error',...options});
  if (!response.ok) {const error=new Error('HTTP '+response.status+' · '+(await response.text()).slice(0,250));error.status=response.status;throw error;}
  return response.json();
}
export function launch(exe,args,root,log,env={}) {
  const child=spawn(exe,args,{cwd:root,env:{...process.env,...env},windowsHide:true,stdio:['pipe','pipe','pipe']});
  child.stdin.on('error',()=>{});

  const strict=new TextDecoder('utf-8',{fatal:true});
  const decode=bytes=>{
    try{return strict.decode(bytes)}
    catch{return new TextDecoder(process.platform==='win32'?'ibm866':'utf-8').decode(bytes)}
  };
  for(const stream of [child.stdout,child.stderr]){
    let tail=Buffer.alloc(0);
    const send=bytes=>{const line=decode(bytes).trim();if(line)log(line.slice(0,1600))};
    stream.on('data',chunk=>{
      const bytes=Buffer.concat([tail,chunk]);
      let start=0;
      for(let offset=0;offset<bytes.length;offset++){
        if(bytes[offset]===10||bytes[offset]===13){send(bytes.subarray(start,offset));start=offset+1}
      }
      tail=bytes.subarray(Math.max(start,bytes.length-8192));
    });
    stream.on('end',()=>send(tail));
  }
  const done=new Promise((resolve,reject)=>{
    child.once('error',reject);
    child.once('close',(code,signal)=>code===0?resolve():reject(Error('Процесс завершился: '+(code??signal))));
  });
  return {child,done};
}
