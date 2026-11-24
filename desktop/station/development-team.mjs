const route='/api/v1/managed/development-team';
const statuses=new Set(['pending','approved','rejected','revoked']);

export function developmentTeam(request){
  async function status({central,token}){
    const result=await request(central+route,{headers:{Authorization:'Bearer '+token}});
    const state=result?.membership?.status;
    return {owner:result?.owner===true,status:statuses.has(state)?state:null};
  }
  return {
    status,
    async apply(session){
      await request(session.central+route+'/apply',{method:'POST',headers:{Authorization:'Bearer '+session.token}});
      return 'Заявка на вступление отправлена.';
    },
    async orchestra(session){
      const info=await status(session);
      if(!info.owner&&info.status!=='approved')throw Error('Доступ к Orchestra ещё не выдан или отозван.');
      return new URL('/',session.central).href;
    }
  };
}
