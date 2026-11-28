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

export type SoulAttribute={id:string;label:string;value:number|null;locked:boolean};
export type SoulView={state:'EgoShard'|'DigitalSoul';level:number;rank:string;experience:number;inLevel:number;toNext:number;availablePoints:number;attributes:SoulAttribute[]};
export type SoulResponse={exists:boolean;soul:SoulView|null;placementAvailable:false};
export function validSoul(value:unknown):value is SoulResponse{
  if(!value||typeof value!=='object')return false;
  const v=value as Partial<SoulResponse>;
  if(v.placementAvailable!==false||typeof v.exists!=='boolean')return false;
  if(!v.exists)return v.soul===null;
  const s=v.soul;
  const integer=(n:unknown,min=0,max=1000000)=>typeof n==='number'&&Number.isInteger(n)&&n>=min&&n<=max;
  return Boolean(s&&(s.state==='EgoShard'||s.state==='DigitalSoul')&&integer(s.level,1,90)&&
    /^(F|E|D|C|B|A|S|SS|SSS)$/.test(s.rank)&&integer(s.experience)&&integer(s.inLevel)&&integer(s.toNext)&&
    integer(s.availablePoints)&&Array.isArray(s.attributes)&&s.attributes.length<=16&&
    s.attributes.every(a=>a&&typeof a.id==='string'&&a.id.length<=64&&typeof a.label==='string'&&a.label.length<=80&&
      typeof a.locked==='boolean'&&(a.locked?a.value===null:integer(a.value,0,100))));
}
export async function soul(token:string):Promise<SoulResponse>{
  const response=await fetch('/api/mini/soul',{method:'POST',headers:{'Content-Type':'application/json','X-Mystic-Mini-Session':token},
    body:'{}',credentials:'omit',cache:'no-store',redirect:'error',referrerPolicy:'no-referrer',signal:AbortSignal.timeout(20000)});
  if(!response.ok)throw new MiniRequestError(response.status);
  const value:unknown=await response.json();
  if(!validSoul(value))throw new MiniRequestError(502);
  return value;
}

export type PresenceContact={name:string;kind:'npc'|'player';reachability:'local'|'friend'};
export type PresenceView={embodiment:string|null;area:string|null;nearby:PresenceContact[];friends:PresenceContact[]};
export type PresenceResponse={snapshotAvailable:boolean;presence:PresenceView|null;freshForSeconds:number};
export async function presence(token:string):Promise<PresenceResponse>{
  const response=await fetch('/api/mini/presence',{method:'POST',headers:{'Content-Type':'application/json','X-Mystic-Mini-Session':token},
    body:'{}',credentials:'omit',cache:'no-store',redirect:'error',referrerPolicy:'no-referrer',signal:AbortSignal.timeout(20000)});
  if(!response.ok)throw new MiniRequestError(response.status);
  const value=await response.json() as Partial<PresenceResponse>|null;
  const label=(s:unknown)=>s===null||typeof s==='string'&&s.length>0&&s.length<=80;
  const contacts=(items:unknown,friend:boolean)=>Array.isArray(items)&&items.length<=20&&items.every(c=>
    c&&typeof c.name==='string'&&label(c.name)&&(c.kind==='npc'||c.kind==='player')&&
    (c.reachability==='local'||friend&&c.reachability==='friend')&&(!friend||c.kind==='player'));
  if(!value||typeof value.snapshotAvailable!=='boolean'||typeof value.freshForSeconds!=='number'||
    !Number.isInteger(value.freshForSeconds)||value.freshForSeconds<0||value.freshForSeconds>15||
    (!value.snapshotAvailable&&(value.presence!==null||value.freshForSeconds!==0))||
    (value.snapshotAvailable&&value.freshForSeconds===0)||
    (value.presence!==null&&(!value.presence||!label(value.presence.embodiment)||!label(value.presence.area)||
      !contacts(value.presence.nearby,false)||!contacts(value.presence.friends,true))))throw new MiniRequestError(502);
  return value as PresenceResponse;
}
