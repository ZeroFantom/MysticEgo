export type NodeMode = 'orchestra' | 'global' | 'local' | 'agent' | 'audit';
export type NodeInfo = { id: string; name: string; kind: NodeMode; version: string; features: string[] };
export type Overview = { node: string; kind: string; agentsOnline: number; nodes: number; records: number; at: string };
export type Agent = {clientId: string; host?:string; connectionId: string; online: boolean; connectedAt: string; lastSeenAt?: string;lastSeen?:string; capabilityCount: number};
export type Parameter = {name:string;clrType:string;description:string;required:boolean;default?:string;values?:string[];format?:string;example?:string};
export type Capability = {id:string;displayName:string;description:string;category:string;version:string;parameters:Parameter[];tags?:string[];examples?:{command?:string;description?:string}[]};
export type Node = {id: string; name: string; kind: string; url?: string; seenAt: string; agentCount: number};
export type RecordDocument = {id:string;collection:string;name:string;data:unknown;updatedAt:string};
export type DispatchResult = {executionId:string;commandId?:string;success:boolean;output?:string;structuredOutput?:unknown;errorCode?:string;errorMessage?:string;duration?:string};
export type AuditEntry = {id:string;at:string;actor:string;action:string;target:string;success:boolean;detail?:string};
export class ApiError extends Error { constructor(public status:number,message:string){super(message)} }
export class NodeApi {
  constructor(readonly baseUrl:string, private getToken:()=>string){}
  async request<T>(path:string, options:RequestInit={}):Promise<T>{
    const controller=new AbortController();
    const timeoutMs = path.startsWith('/commands/') ? 145000 : path.startsWith('/assistant/') ? 155000 : 25000;
    const timeoutMessage=path.startsWith('/assistant/')?'Ассистент не ответил вовремя. Проверьте состояние модели в журнале MysticEgo.':
      'Запрос не завершился за отведённое время. Проверьте подключение к узлу.';
    const timer=setTimeout(()=>controller.abort(new DOMException(timeoutMessage, 'TimeoutError')),timeoutMs);
    try {
      const response=await fetch(this.baseUrl.replace(/\/$/,'')+'/api/v1'+path,{
        ...options,signal:options.signal??controller.signal,headers:{'Content-Type':'application/json','Authorization':'Bearer '+this.getToken(),...(options.headers??{})}
      });
      if(!response.ok){
        const text=await response.text(); let description=text.slice(0,300);
        try{const parsed=JSON.parse(text);description=parsed.error||parsed.detail||parsed.message||parsed.title||description}catch{}
        throw new ApiError(response.status,description||('HTTP '+response.status));
      }
      if(response.status===204)return undefined as T;
      const body=await response.text();
      return body.trim()?JSON.parse(body) as T:undefined as T;
    }catch(error){ if(error instanceof DOMException && (error.name==='AbortError'||error.name==='TimeoutError'))throw new Error(timeoutMessage);throw error; }finally{clearTimeout(timer)}
  }
  async diagnosticReport(format:'json'|'txt'|'xlsx'):Promise<Blob>{
    const response=await fetch(this.baseUrl.replace(/\/$/,'')+'/api/v1/orchestra/diagnostics/report?format='+format,{
      headers:{Authorization:'Bearer '+this.getToken()},cache:'no-store',signal:AbortSignal.timeout(25000)
    });
    if(!response.ok)throw new ApiError(response.status,'Не удалось сформировать отчёт (HTTP '+response.status+').');
    return response.blob();
  }
  deployment=()=>this.request<{mode:string;label:string;description?:string;node:string;kind:string;auth:string;global?:string;local?:string}>('/deployment');
  ping=()=>this.request<{node:string;serverUtc:string;monotonicMs:number}>('/diagnostics/ping');
  links=()=>this.request<{name:string;url:string;status:number;ms:number;reachable:boolean}[]>('/diagnostics/links');
  async throughput():Promise<number>{const start=performance.now();const response=await fetch(this.baseUrl.replace(/\/$/,'')+'/api/v1/diagnostics/payload?bytes=1048576',{headers:{Authorization:'Bearer '+this.getToken()}});if(!response.ok)throw new ApiError(response.status,'Не удалось измерить скорость загрузки');const buffer=await response.arrayBuffer();return (buffer.byteLength*8/1e6)/((performance.now()-start)/1000)}
  meta=()=>this.request<NodeInfo>('/meta');
  overview=()=>this.request<Overview>('/overview');
  nodes=()=>this.request<Node[]>('/nodes');
  agents=()=>this.request<Agent[]>('/agents');
  capabilities=(id:string)=>this.request<Capability[]>(`/agents/${encodeURIComponent(id)}/capabilities`);
  records=(collection:string)=>this.request<RecordDocument[]>(`/records/${encodeURIComponent(collection)}`);
  audit=()=>this.request<AuditEntry[]>('/audit');
  create=(collection:string,body:{name:string;data:unknown})=>this.request<RecordDocument>(`/records/${collection}`,{method:'POST',body:JSON.stringify(body)});
  update=(collection:string,id:string,body:{name:string;data:unknown})=>this.request<RecordDocument>(`/records/${collection}/${id}`,{method:'PUT',body:JSON.stringify(body)});
  remove=(collection:string,id:string)=>this.request<void>(`/records/${collection}/${id}`,{method:'DELETE'});
  dispatch=(body:{targetId:string;commandId:string;rawArgs?:string;namedArgs?:Record<string,string|number|boolean>;timeoutSeconds?:number;confirmedByHuman?:boolean})=>this.request<DispatchResult>('/commands/dispatch',{method:'POST',body:JSON.stringify(body)});
  agentUpdateLatest=async()=>{try{return await this.request<{version:string;sha256:string;size:number;signature:string;fileName:string;publishedUtc:string}>('/updates/agent/latest')}catch(e){if(e instanceof ApiError&&e.status===404)return null;throw e}};
  publishAgentUpdate=async(version:string,file:File)=>{const body=new FormData();body.append('version',version);body.append('package',file);const response=await fetch(this.baseUrl.replace(/\/$/,'')+'/api/v1/updates/agent',{method:'POST',headers:{Authorization:'Bearer '+this.getToken()},body});if(!response.ok)throw new ApiError(response.status,await response.text());return await response.json()};
  agentUpdateCheck=()=>this.request<{started:boolean}>('/agent/updates/check',{method:'POST'});
  agentUpdateStatus=()=>this.request<{status:string;version?:string;error?:string;checkedAt?:string}>('/agent/updates/status');
  hierarchy=()=>this.request<{current:{kind:string;tier:number;label:string};tiers:{kind:string;tier:number;label:string}[]}>('/ecosystem/hierarchy');
  ether=()=>this.request<{id:string;nickname:string;level:number;experience:number;rank:string;characterStatus:string}|null>('/ecosystem/ether/me');
  etherRegister=(nickname:string)=>this.request('/ecosystem/ether/register',{method:'POST',body:JSON.stringify({nickname})});
  directory=()=>this.request<{nickname:string;rank:string;kind:string;title:string;accent:string;avatarUrl:string;accountScope:string;network?:string;club?:string;position?:string;roleLabel?:string;gameLevel:number}[]>('/social/directory');
  system=()=>this.request<{host:string;os:string;uptimeMs:number}>('/agent/system');
}
export const collections = [
  {id:'clubs',label:'Клубы и офисы',scope:['global','local']},
  {id:'zones',label:'Зоны',scope:['global','local']},
  {id:'presets',label:'Пресеты',scope:['global','local','agent']},
  {id:'automations',label:'Автоматизация',scope:['global','local','agent']},
  {id:'configs',label:'Конфигурации',scope:['global','local','agent']},
  {id:'langame-links',label:'Langame · связи',scope:['global','local']},
  {id:'langame-logs',label:'Langame · журналы',scope:['global','local']},
  {id:'devices',label:'Оборудование',scope:['local','agent']},
  {id:'applications',label:'Приложения',scope:['local','agent']},
  {id:'sessions',label:'Сессии',scope:['global','local']},
  {id:'profiles',label:'Профили',scope:['global','local','agent']},
  {id:'alerts',label:'Оповещения',scope:['global','local','agent']},
] as const;

