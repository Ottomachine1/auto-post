import {useQuery} from '@tanstack/react-query';
import {api} from './api';
export function ModelPicker({value,onChange,label,disabled=false}:{value:string;onChange:(v:string)=>void;label:string;disabled?:boolean}) {
 const models=useQuery<{items:string[]}>({queryKey:['ai-models'],queryFn:()=>api('/ai/models'),staleTime:300000,retry:false});
 return <label className="model-picker">{label}<select aria-label={label} value={value} disabled={disabled||models.isPending||models.isError} onChange={e=>onChange(e.target.value)}><option value="">使用系统默认模型</option>{value&&!models.data?.items.includes(value)&&<option value={value}>{value}</option>}{models.data?.items.map(m=><option key={m} value={m}>{m}</option>)}</select>{models.isError&&<span role="alert">模型列表暂时不可用 <button type="button" onClick={()=>void models.refetch()}>重新获取</button></span>}{models.data?.items.length===0&&<small>未配置凭据或暂无对话模型</small>}</label>
}
