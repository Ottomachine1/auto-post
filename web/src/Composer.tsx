import twitterText from "twitter-text";
import {useRef,useState} from 'react';
import {ImagePlus,Link,Smile,ArrowLeft,ArrowRight,X,Eye,Check} from 'lucide-react';
import {api, type Attachment} from './api';

function host(url:string){try{return new URL(url).hostname;}catch{return url;}}
export function postLength(text:string) { return twitterText.parseTweet(text).weightedLength; }
export function PostBody({text}:{text:string}) {
  return <p className="post-body">{text.split(/(https?:\/\/[^\s]+)/g).map((part,i)=>/^https?:\/\//.test(part)?<a key={i} href={part} target="_blank" rel="noopener noreferrer">{part}</a>:<span key={i}>{part}</span>)}</p>;
}
export function MediaGrid({media}:{media:Attachment[]}) {
  return media.length?<div className={'post-images count-'+media.length}>{media.map(m=><img key={m.id} src={'/api/media/'+m.id} alt={m.alt||'文章图片'} />)}</div>:null;
}
export function Composer({content,onContent,media,onMedia,channels,onChannels,disabled,onUploading}:{content:string,onContent:(v:string)=>void,media:Attachment[],onMedia:(v:Attachment[])=>void,channels:string[],onChannels:(v:string[])=>void,disabled:boolean,onUploading:(v:boolean)=>void}) {
  const file=useRef<HTMLInputElement>(null),text=useRef<HTMLTextAreaElement>(null);
  const [uploading,setUploading]=useState(false),[error,setError]=useState(''),[drag,setDrag]=useState(false),[preview,setPreview]=useState(false),[emoji,setEmoji]=useState(false),[link,setLink]=useState(''),[linkEditor,setLinkEditor]=useState(false);
  const [card,setCard]=useState<{title:string,description:string,url:string}|null>(null),[cardBusy,setCardBusy]=useState(false);
  const length=postLength(content),url=content.match(/https?:\/\/[^\s]+/)?.[0];
  async function upload(files:File[]) {
    if(uploading||disabled) return;
    if(media.length+files.length>4){setError('每篇最多添加4张图片');return;}
    if(files.some(f=>!['image/png','image/jpeg','image/webp'].includes(f.type)||f.size>5*1024*1024)){setError('请选择5MB以内的PNG、JPEG或WebP图片');return;}
    setUploading(true);onUploading(true);setError('');
    const added:Attachment[]=[];
    try {
      for(const f of files){
        const data=await new Promise<string>((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result).split(',')[1]);reader.onerror=()=>reject(new Error('图片读取失败'));reader.readAsDataURL(f);});
        const result=await api<{id:string}>('/media','POST',{data});added.push({id:result.id,alt:''});
      }
    }catch(e){setError((e as Error).message);}
    finally {onMedia([...media,...added]);setUploading(false);onUploading(false);if(file.current)file.current.value='';}
  }
  function insert(value:string){const start=text.current?.selectionStart??content.length,end=text.current?.selectionEnd??start;onContent(content.slice(0,start)+value+content.slice(end));requestAnimationFrame(()=>{text.current?.focus();text.current?.setSelectionRange(start+value.length,start+value.length);});}
  function move(index:number,by:number){const copy=[...media];[copy[index],copy[index+by]]=[copy[index+by],copy[index]];onMedia(copy);}
  return <div className={'social-composer'+(drag?' dragging':'')} onDragOver={e=>{if(e.dataTransfer.types.includes('Files')){e.preventDefault();setDrag(true);}}} onDragLeave={e=>{if(!e.currentTarget.contains(e.relatedTarget as Node))setDrag(false);}} onDrop={e=>{e.preventDefault();setDrag(false);void upload(Array.from(e.dataTransfer.files));}}>
    <div className="composer-profile"><span className="profile-avatar">A</span><div><strong>我的创作</strong><span>普通推文 · 人工审核后发布</span></div><button type="button" className={preview?'active':''} onClick={()=>setPreview(!preview)}><Eye size={16}/>{preview?'返回编辑':'查看成稿'}</button></div>
    {preview?<article className="tweet-preview"><strong>我的账号 <Check size={14}/></strong><span className="meta"> @your_account · 刚刚</span><PostBody text={content||'你的想法会在这里呈现'}/><MediaGrid media={media}/><div className="post-engagement">回复　　转发　　喜欢　　浏览</div></article>:<>
      <textarea ref={text} aria-label="推文内容" placeholder="有什么新鲜事？" className="tweet-input" maxLength={10000} required={!media.length} value={content} disabled={uploading||disabled} onChange={e=>{onContent(e.target.value);setCard(null);}} onKeyDown={e=>{if((e.ctrlKey||e.metaKey)&&e.key==='Enter'){e.preventDefault();e.currentTarget.form?.requestSubmit();}}} onPaste={e=>{const images=Array.from(e.clipboardData.files);if(images.length){e.preventDefault();void upload(images);}}}/>
      {media.length>0&&<div className={"attachment-editor count-"+media.length}>{media.map((m,i)=><div className="attachment" key={m.id}><img src={'/api/media/'+m.id} alt={m.alt||'图片 '+(i+1)}/><button type="button" className="remove-image" aria-label={'移除图片 '+(i+1)} disabled={uploading} onClick={()=>onMedia(media.filter(x=>x.id!==m.id))}><X size={16}/></button><div className="attachment-controls"><button type="button" disabled={i===0||uploading} aria-label="图片向前移动" onClick={()=>move(i,-1)}><ArrowLeft size={14}/></button><span>图片 {i+1}</span><button type="button" disabled={i===media.length-1||uploading} aria-label="图片向后移动" onClick={()=>move(i,1)}><ArrowRight size={14}/></button></div><input aria-label={'图片 '+(i+1)+' 替代文字'} placeholder="添加图片描述（替代文字）" maxLength={1000} disabled={uploading} value={m.alt} onChange={e=>onMedia(media.map(x=>x.id===m.id?{...x,alt:e.target.value}:x))}/></div>)}</div>}
      <div className="composer-tools"><input ref={file} hidden type="file" accept="image/png,image/jpeg,image/webp" multiple onChange={e=>void upload(Array.from(e.target.files||[]))}/><button type="button" title="添加图片，也可拖拽或粘贴" aria-label="添加图片" disabled={disabled||uploading||media.length===4} onClick={()=>file.current?.click()}><ImagePlus size={20}/></button><button type="button" aria-label="插入链接" onClick={()=>setLinkEditor(!linkEditor)} disabled={uploading}><Link size={20}/></button><button type="button" aria-label="插入表情" onClick={()=>setEmoji(!emoji)} disabled={uploading}><Smile size={20}/></button><span>{uploading?'正在上传图片…':`拖拽或粘贴图片 · ${media.length}/4`}</span><span className={'character-count '+(length>280?'over':'')} aria-label="X字数">{length}/280</span></div>
      {emoji&&<div className="emoji-picker">{['😀','🔥','🚀','👀','💡','📈','📉','🌍','✅','⚠️','🤖','💬'].map(value=><button type="button" key={value} onClick={()=>{insert(value);setEmoji(false);}}>{value}</button>)}</div>}
      {linkEditor&&<div className="insert-link"><input type="url" aria-label="链接地址" placeholder="https://" value={link} onChange={e=>setLink(e.target.value)}/><button type="button" disabled={!/^https?:\/\//.test(link)||uploading} onClick={()=>{insert((content&&!content.endsWith(' ')?' ':'')+link);setLink('');setLinkEditor(false);}}>插入</button></div>}
    </>}
    {url&&<div className="link-card"><Link size={18}/><div><a href={url} target="_blank" rel="noopener noreferrer">{card?.title||host(url)}</a><span>{card?.description||url}</span></div><button type="button" disabled={cardBusy} onClick={()=>{setCardBusy(true);void api<{title:string,description:string,url:string}>('/link-preview','POST',{url}).then(setCard).catch(e=>setError(e.message)).finally(()=>setCardBusy(false));}}>{cardBusy?'读取中':'读取链接预览'}</button></div>}
    <div className="channel-select">{[['x','𝕏'],['binance','币安广场'],['telegram','Telegram'],['okx','OKX'],['truth','Truth']].map(([id,label])=><label key={id} className={channels.includes(id)?'selected':''}><input type="checkbox" checked={channels.includes(id)} disabled={uploading} onChange={()=>onChannels(channels.includes(id)?channels.filter(c=>c!==id):[...channels,id])}/>{label}</label>)}</div>
    {channels.includes('x')&&length>280&&<p className="accent-error">超过普通推文的280加权字数，保存后需缩短才能发布。</p>}
    {media.length>0&&channels.some(c=>!['x','binance'].includes(c))&&<p className="accent-error">图片投递目前支持 X 和币安广场，请为其他渠道另存文字草稿。</p>}
    {error&&<p role="alert" className="accent-error">{error}</p>}
    {drag&&<div className="drop-overlay"><ImagePlus size={36}/><strong>松开添加图片</strong><span>最多4张 · 每张5MB</span></div>}
  </div>;
}
