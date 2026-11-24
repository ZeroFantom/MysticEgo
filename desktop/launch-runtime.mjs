import path from 'node:path';
import {existsSync} from 'node:fs';

export function getDotnet(root){
  if(process.env.MYSTICEGO_DOTNET)return process.env.MYSTICEGO_DOTNET;
  const candidate=path.join(root,'runtime',process.platform==='win32'?'win-x64':'linux-x64',process.platform==='win32'?'dotnet.exe':'dotnet');
  return existsSync(candidate)?candidate:'dotnet';
}
