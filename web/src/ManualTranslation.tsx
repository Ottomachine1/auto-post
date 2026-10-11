import {useEffect, useRef, useState} from 'react';
import {useQuery,useQueryClient} from '@tanstack/react-query';
import {api} from './api';

type Translation = {id:string;status:string;translatedTitle:string;translatedBody:string;error?:string};
const labels:Record<string,string>={zh:'中文',en:'English',ja:'日本語',ko:'한국어',fr:'Français',de:'Deutsch',es:'Español',ru:'Русский',ar:'العربية'};

export function ManualTranslation({eventId,title='',body,onApply,compact=false,showOriginal=false}:{eventId?:string;title?:string;body:string;onApply?:(value:string)=>void;compact?:boolean;showOriginal?:boolean}) {
  const cache=useQueryClient();
  const revision=useRef(0);
  const [target,setTarget]=useState('zh'),[id,setId]=useState(''),[show,setShow]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const languages=useQuery({queryKey:['translation-languages'],queryFn:()=>api<{targets:string[];configured:boolean}>('/translation-languages'),staleTime:60000});
  const result=useQuery({queryKey:['manual-translation',id],queryFn:()=>api<Translation>('/translations/'+id),enabled:!!id,refetchInterval:q=>!q.state.error&&['pending','running'].includes(q.state.data?.status||'pending')?1500:false});
  useEffect(()=>{revision.current++;setId('');setShow(false);setError('');setBusy(false);return()=>{revision.current++;};},[eventId,title,body,target]);
  useEffect(()=>{if(languages.data?.targets.length&&!languages.data.targets.includes(target))setTarget(languages.data.targets[0]);},[languages.data,target]);
  const finished=['completed','native'].includes(result.data?.status||'');
  async function request() {
    const requestedRevision=revision.current;
    setBusy(true);setError('');
    try {
      const value=await api<Translation>(eventId?'/events/'+eventId+'/translation':'/translations','POST',eventId?{target}:{title,body,target});
      cache.setQueryData(['manual-translation',value.id],value);
      if(requestedRevision===revision.current){setId(value.id);setShow(true);}
    } catch(e){if(requestedRevision===revision.current)setError((e as Error).message);} finally{if(requestedRevision===revision.current)setBusy(false);}
  }
  return <section className={'manual-translation'+(compact?' compact':'')} aria-label="手动本地翻译" onClick={e=>e.stopPropagation()}>
    {showOriginal&&(!show||!finished)&&<><h3>{title}</h3><p className="muted">{body}</p></>}
    <div className="row">
      <select aria-label="翻译目标语言" value={target} onChange={e=>setTarget(e.target.value)} disabled={busy}>
        {(languages.data?.targets?.length?languages.data.targets:['zh']).map(t=><option key={t} value={t}>{labels[t]||t}</option>)}
      </select>
      <button type="button" onClick={finished?()=>setShow(!show):request} disabled={busy|| (!!id&&!finished&&result.data?.status!=='failed'&&!result.error)||!languages.data?.configured}>
        {busy?'提交中…':result.error?'重新获取翻译':id&&!finished&&result.data?.status!=='failed'?'翻译处理中…':finished?(show?(showOriginal?'查看原文':'收起译文'):'查看译文'):result.data?.status==='failed'?'重试翻译':'本地翻译'}
      </button>
    </div>
    {!languages.data?.configured&&<small>本地翻译服务未就绪</small>}
    {(error||result.error||result.data?.error)&&<p role="status">{error||(result.error as Error)?.message||result.data?.error}</p>}
    {show&&finished&&<div className="translation-output"><small>本地机器翻译 · 请核对原文</small>{result.data?.translatedTitle&&<h4>{result.data.translatedTitle}</h4>}<p>{result.data?.translatedBody}</p>{onApply&&<button type="button" onClick={()=>onApply(result.data!.translatedBody)}>使用译文编辑</button>}</div>}
  </section>;
}
