import {useEffect,useState} from 'react';
import {ImagePlus,Sparkles,Sticker as StickerIcon,Trash2} from 'lucide-react';
import {useApp,useRemote} from '../app/context';
import {ErrorBanner} from './primitives';
import {createSticker,stickerAccents,type StickerAccent} from './createSticker';
type Sticker={id:string;title:string;width:number;height:number};

export function StickerImage({id,title='Стикер'}:{id:string;title?:string}){
  const {session}=useApp();
  const [objectUrl,setObjectUrl]=useState('');
  useEffect(()=>{
    const cancel=new AbortController();let url='';
    setObjectUrl('');
    void fetch(session.url.replace(/\/$/,'')+'/api/v1/collective/stickers/'+encodeURIComponent(id)+'/image',{
      signal:cancel.signal,headers:{Authorization:'Bearer '+session.token}
    }).then(async response=>{
      if(!response.ok)return;
      const blob=await response.blob();
      if(!cancel.signal.aborted){url=URL.createObjectURL(blob);setObjectUrl(url)}
    }).catch(()=>{});
    return()=>{cancel.abort();if(url)URL.revokeObjectURL(url)};
  },[id,session.url,session.token]);
  return objectUrl?<img className="sticker-image" src={objectUrl} alt={title} draggable={false}/>:<StickerIcon className="sticker-placeholder" aria-label={title}/>;
}

export function StickerPicker({send}:{send:(id:string)=>void}){
  const {api,session}=useApp();
  const data=useRemote(()=>api.request<Sticker[]>('/collective/stickers'),[api]);
  const [error,setError]=useState(''),[busy,setBusy]=useState(false);
  const [caption,setCaption]=useState(''),[emoji,setEmoji]=useState('✨'),[accent,setAccent]=useState<StickerAccent>('mint');
  const [saved,setSaved]=useState(false);
  async function upload(getFile:()=>Promise<File>){
    if(busy)return;
    setSaved(false);
    setError('');setBusy(true);
    try{
      const file=await getFile();
      if(!['image/png','image/webp','image/gif'].includes(file.type)||file.size>524288)throw Error('Выберите PNG, WebP или GIF до 512 КиБ.');
      const url=URL.createObjectURL(file);
      try{
        const image=new Image();image.src=url;await image.decode();
        if(Math.min(image.naturalWidth,image.naturalHeight)<32||Math.max(image.naturalWidth,image.naturalHeight)>512)
          throw Error('Ширина и высота должны быть от 32 до 512 пикселей.');
      }finally{URL.revokeObjectURL(url)}
      const title=file.name.replace(/\.[^.]+$/,'').slice(0,60);
      const result=await fetch(session.url.replace(/\/$/,'')+'/api/v1/collective/stickers?name='+encodeURIComponent(title),
        {method:'POST',headers:{Authorization:'Bearer '+session.token,'Content-Type':'application/octet-stream'},body:file});
      if(!result.ok){const details=await result.json().catch(()=>({}));throw Error(details.error||'Не удалось сохранить стикер.')}
      data.reload();setSaved(true);
    }catch(error){setError(error instanceof Error?error.message:String(error))}
    finally{setBusy(false)}
  }
  async function remove(id:string){
    try{await api.request('/collective/stickers/'+id,{method:'DELETE'});data.reload()}
    catch(error){setError(String(error))}
  }
  return <section className="sticker-picker" aria-label="Мои стикеры">
    <div className="row-actions"><strong>Мои стикеры</strong><label className="btn secondary" title="Добавить стикер"><ImagePlus size={16}/> Добавить
      <input aria-label="Добавить стикер" accept="image/png,image/webp,image/gif" type="file" disabled={busy} onChange={event=>{const file=event.target.files?.[0];event.target.value='';if(file)void upload(async()=>file)}}/></label></div>
    <details className="sticker-workshop">
      <summary><Sparkles size={15} aria-hidden="true"/> Создать стикер</summary>
      <div className="stack">
        <label>Подпись<input maxLength={18} value={caption} placeholder="Например, «Готово!»"
            onKeyDown={event=>{
              if(event.key!=='Enter'||event.nativeEvent.isComposing)return;
              event.preventDefault();
              if(caption.trim())void upload(()=>createSticker(caption,emoji,accent));
            }} onChange={event=>setCaption(event.target.value)}/></label>
        <div className="sticker-options">
          <label>Знак<select value={emoji} onChange={event=>setEmoji(event.target.value)}>
            {['✨','🎮','💜','🤖','🔥','👋'].map(symbol=><option key={symbol}>{symbol}</option>)}
          </select></label>
          <label>Цвет<select value={accent} onChange={event=>setAccent(event.target.value as StickerAccent)}>
            {Object.entries(stickerAccents).map(([id,entry])=><option value={id} key={id}>{entry.name}</option>)}
          </select></label>
        </div>
        <button type="button" className="btn secondary" disabled={busy||!caption.trim()}
          onClick={()=>void upload(()=>createSticker(caption,emoji,accent))}>Создать и сохранить PNG</button>
        <small className="footnote">Бесплатный шаблон. Рисуется в браузере.</small>
      </div>
    </details>
    {saved&&<p className="footnote" role="status">Стикер сохранён.</p>}
    <small className="footnote">PNG, WebP, GIF · 32–512 px · до 512 КиБ</small>
    <ErrorBanner text={error||data.error}/>
    <div className="sticker-grid">{data.value?.map(item=><article key={item.id}>
      <button type="button" className="sticker-send" aria-label={'Отправить '+item.title} onClick={()=>send(item.id)}><StickerImage id={item.id} title={item.title}/></button>
      <button type="button" className="icon-btn" aria-label={'Удалить '+item.title} onClick={()=>void remove(item.id)}><Trash2 size={12}/></button>
    </article>)}</div>
  </section>;
}
