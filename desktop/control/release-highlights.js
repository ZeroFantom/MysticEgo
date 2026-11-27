// Render release notes as text, never executable HTML. Shared by Local, Agent and Audit.
export function renderReleaseHighlights(value,dismiss){
 const card=document.getElementById('release-highlights');
 if(!card)return;
 if(!value?.tag||!value?.notes){card.hidden=true;return}
 if(card.dataset.tag!==value.tag){
  card.dataset.tag=value.tag;
  card.querySelector('h2').textContent='Что нового · '+value.tag;
  card.querySelector('pre').textContent=value.notes;
  const close=card.querySelector('button');
  close.onclick=async()=>{close.disabled=true;try{if(await dismiss(value.tag))card.hidden=true}finally{close.disabled=false}};
 }
 card.hidden=false;
}
