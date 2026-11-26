import {useEffect,useRef,type ReactNode} from 'react';

export function ProfileDialog({label,busy,onClose,children,wide=false}:{label:string;busy:boolean;onClose:()=>void;children:ReactNode;wide?:boolean}){
  const dialog=useRef<HTMLDialogElement>(null);
  useEffect(()=>{
    const element=dialog.current;
    if(!element)return;
    element.showModal();
    return()=>{if(element.open)element.close()};
  },[]);
  return <dialog ref={dialog} className={'modal profile-dialog'+(wide?' quiz-modal':'')} aria-label={label}
    onCancel={event=>{event.preventDefault();if(!busy)onClose()}}
    onClick={event=>{
      if(busy||event.target!==event.currentTarget)return;
      const rect=event.currentTarget.getBoundingClientRect();
      if(event.clientX<rect.left||event.clientX>rect.right||event.clientY<rect.top||event.clientY>rect.bottom)onClose();
    }}>
    {children}
  </dialog>;
}
