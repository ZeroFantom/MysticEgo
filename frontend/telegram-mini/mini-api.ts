// Dedicated same-origin API. No application-wide Auth headers, cookies or persisted credentials.
const endpoint='/api/auth/messenger/telegram/mini/';
export type MiniSession={sessionToken:string;expiresInSeconds:600};
export type MiniStatus={linked:true;scope:'mini-app';worldAvailable:boolean};
export class MiniRequestError extends Error{
  constructor(public readonly status:number){super('Mini App request unavailable')}
}
export function validSession(value:unknown):value is MiniSession{
  if(!value||typeof value!=='object')return false;
  const v=value as Partial<MiniSession>;
  return typeof v.sessionToken==='string'&&/^[A-Za-z0-9_-]{43}$/.test(v.sessionToken)&&v.expiresInSeconds===600;
}
export function validStatus(value:unknown):value is MiniStatus{
  if(!value||typeof value!=='object')return false;
  const v=value as Partial<MiniStatus>;
  return v.linked===true&&v.scope==='mini-app'&&typeof v.worldAvailable==='boolean';
}
async function request(action:'exchange'|'status'|'logout',body:object,session?:string):Promise<unknown>{
  const headers:Record<string,string>={'Content-Type':'application/json'};
  if(session)headers['X-Mystic-Mini-Session']=session;
  const response=await fetch(endpoint+action,{method:'POST',headers,body:JSON.stringify(body),
    credentials:'omit',cache:'no-store',redirect:'error',referrerPolicy:'no-referrer',signal:AbortSignal.timeout(12000)});
  if(!response.ok)throw new MiniRequestError(response.status);
  if(action==='logout')return undefined;
  return response.json();
}
export async function exchange(initData:string):Promise<MiniSession>{
  if(!initData||initData.length>16384||location.protocol!=='https:')throw new MiniRequestError(400);
  const response=await request('exchange',{initData});
  if(!validSession(response))throw new MiniRequestError(502);
  return response;
}
export async function status(token:string):Promise<MiniStatus>{
  const response=await request('status',{},token);
  if(!validStatus(response))throw new MiniRequestError(502);
  return response;
}
export async function logout(token:string):Promise<void>{
  await request('logout',{},token);
}
