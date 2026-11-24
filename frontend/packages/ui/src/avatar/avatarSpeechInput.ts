// Optional browser speech recognition, explicitly user-triggered; may rely on browser/cloud speech services.
type RecognitionInstance={lang:string;interimResults:boolean;onresult:((e:{results:ArrayLike<ArrayLike<{transcript:string}>>})=>void)|null;onerror:((e:unknown)=>void)|null;onend:(()=>void)|null;start:()=>void;stop:()=>void};
type RecognitionConstructor=new()=>RecognitionInstance;
export function startVoiceInput(onText:(text:string)=>void,onError:(error:string)=>void):()=>void{
 const global=window as Window&{SpeechRecognition?:RecognitionConstructor;webkitSpeechRecognition?:RecognitionConstructor};
 const Constructor=global.SpeechRecognition??global.webkitSpeechRecognition;
 if(!Constructor){onError('SpeechRecognition не поддерживается браузером');return()=>{}}
 const recognition=new Constructor();recognition.lang='ru-RU';recognition.interimResults=false;
 recognition.onresult=(event)=>onText(event.results[0]?.[0]?.transcript??'');
 recognition.onerror=()=>onError('Не удалось распознать голос. Проверьте разрешение на микрофон.');
 recognition.start();return()=>recognition.stop();
}
