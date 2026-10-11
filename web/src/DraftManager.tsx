import {useEffect,useState,type ReactNode} from 'react';
import {useQuery,useQueryClient} from '@tanstack/react-query';
import {api,type Draft} from './api';
import * as Dialog from '@radix-ui/react-dialog';

type Page={items:Draft[];total:number;page:number;pageSize:number};
type Selection={id:string;revision:number};
export function DraftManager({renderDraft}:{renderDraft:(draft:Draft)=>ReactNode}) {
 const cache=useQueryClient();
 const [view,setView]=useState('active'),[search,setSearch]=useState(''),[status,setStatus]=useState(''),[channel,setChannel]=useState(''),[sort,setSort]=useState('newest'),[page,setPage]=useState(1);
 const [selected,setSelected]=useState<Selection[]>([]),[confirm,setConfirm]=useState<{action:string;items:Selection[]}|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const result=useQuery({queryKey:['drafts','page',view,search,status,channel,sort,page],queryFn:()=>api<Page>('/drafts/page?'+new URLSearchParams({view,q:search,status,channel,sort,page:String(page)}))});
 useEffect(()=>{setPage(1);setSelected([]);},[view,search,status,channel,sort]);
 const rows=result.data?.items||[];
 const actions:Record<string,string>={trash:'移入回收站',restore:'恢复草稿',archive:'归档',unarchive:'取消归档'};
 function begin(action:string,items:Selection[]){setError('');setConfirm({action,items});}
 async function selectAll(){setBusy(true);setError('');try{const data=await api<{items:Selection[]}>('/drafts/selection?'+new URLSearchParams({view,q:search,status,channel}));setSelected(data.items);}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 async function execute(){if(!confirm)return;setBusy(true);setError('');let remaining=confirm.items;let completed=0;try{while(remaining.length){const batch=remaining.slice(0,200);await api('/drafts/bulk','POST',{action:confirm.action,items:batch});completed+=batch.length;remaining=remaining.slice(batch.length);setConfirm({...confirm,items:remaining});setSelected(remaining);}setConfirm(null);}catch(e){setError(`本次已处理 ${completed} 条，剩余 ${remaining.length} 条未处理：${(e as Error).message}`);}finally{await cache.invalidateQueries({queryKey:['drafts']});setBusy(false);}}
 return <section aria-label="草稿管理">
  <div className="panel content-card draft-manager-controls">
   <nav aria-label="草稿分类" className="actions">{[['active','发布中心'],['archived','归档'],['trash','回收站']].map(([id,label])=><button key={id} aria-pressed={view===id} className={view===id?'primary':''} onClick={()=>setView(id)}>{label}</button>)}</nav>
   <div className="actions">
    <input aria-label="搜索草稿" placeholder="搜索正文" value={search} maxLength={200} onChange={e=>setSearch(e.target.value)}/>
    <select aria-label="草稿状态" value={status} onChange={e=>setStatus(e.target.value)}><option value="">全部状态</option>{[['draft','待审核'],['approved','已审核'],['published','已发布'],['partial','需处理']].map(([id,label])=><option key={id} value={id}>{label}</option>)}</select>
    <select aria-label="草稿渠道" value={channel} onChange={e=>setChannel(e.target.value)}><option value="">全部渠道</option>{['x','telegram','binance','okx','truth'].map(c=><option key={c}>{c}</option>)}</select>
    <select aria-label="草稿排序" value={sort} onChange={e=>setSort(e.target.value)}><option value="newest">最新优先</option><option value="oldest">最早优先</option></select>
   </div>
   <div className="actions"><label><input type="checkbox" aria-label="选择本页草稿" checked={rows.length>0&&rows.every(d=>selected.some(s=>s.id===d.id))} onChange={e=>setSelected(e.target.checked?rows.map(d=>({id:d.id,revision:d.revision})):[])}/>选择本页</label><button disabled={busy||!result.data?.total} onClick={()=>void selectAll()}>选择全部筛选结果</button><span>已选 {selected.length} · 共 {result.data?.total??0} 条</span>
    {(view==='trash'?['restore']:view==='archived'?['unarchive','trash']:['archive','trash']).map(a=><button key={a} disabled={!selected.length||busy} onClick={()=>begin(a,selected)}>{actions[a]}</button>)}
   </div>
  </div>
  {(error||result.error)&&<p role="alert">{error||(result.error as Error)?.message}</p>}
  <Dialog.Root open={!!confirm} onOpenChange={open=>{if(!open&&!busy)setConfirm(null);}}><Dialog.Portal><Dialog.Overlay className="overlay"/><Dialog.Content className="modal" role="alertdialog"><Dialog.Title>{confirm?actions[confirm.action]:''} {confirm?.items.length} 条草稿？</Dialog.Title><Dialog.Description>每批最多处理200条；中断时保留未处理选择，可重试。仅管理站内内容。排队投递将取消，已在平台发布的帖子保持不变；恢复后需要重新审核。</Dialog.Description>{error&&<p role="alert">{error}</p>}<div className="actions"><button disabled={busy} onClick={()=>setConfirm(null)}>取消</button><button disabled={busy} className="primary" onClick={()=>void execute()}>{busy?'处理中…':'确认'+(confirm?actions[confirm.action]:'')}</button></div></Dialog.Content></Dialog.Portal></Dialog.Root>
  {result.isPending?<p role="status">正在加载草稿…</p>:!rows.length?<div className="panel content-card"><h3>当前筛选没有草稿</h3><p>更换筛选条件，或创建新的内容。</p></div>:null}
  <div className="cards">{rows.map(d=><article key={d.id}><div className="actions draft-selection"><label><input type="checkbox" aria-label={'选择草稿 '+d.id} checked={selected.some(s=>s.id===d.id)} onChange={e=>setSelected(e.target.checked?[...selected,{id:d.id,revision:d.revision}]:selected.filter(s=>s.id!==d.id))}/>选择</label>{(view==='trash'?['restore']:view==='archived'?['unarchive','trash']:['archive','trash']).map(a=><button key={a} disabled={busy} onClick={()=>begin(a,[{id:d.id,revision:d.revision}])}>{actions[a]}</button>)}</div>{view==='active'?renderDraft(d):<div className="panel content-card"><p className="draft-text">{d.content}</p><span>版本 {d.revision} · {d.channels.join(' / ')}</span>{d.deliveries.map(del=><p key={del.id}>{del.channel} · {del.status} · {del.remoteId||del.error}</p>)}</div>}</article>)}</div>
  <div className="actions draft-pagination"><button disabled={page===1||result.isFetching} onClick={()=>{setPage(p=>p-1);setSelected([]);}}>上一页</button><span>第 {page} 页 / {Math.max(1,Math.ceil((result.data?.total||0)/50))} 页</span><button disabled={page*50>=(result.data?.total||0)||result.isFetching} onClick={()=>{setPage(p=>p+1);setSelected([]);}}>下一页</button></div>
 </section>;
}
