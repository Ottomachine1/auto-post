import {useEffect,useState} from 'react';
import './theme.css';
type Theme='light'|'dark'|'system';
const key='atlas-theme';
function read():Theme {try{const t=localStorage.getItem(key);return t==='light'||t==='dark'?t:'system';}catch{return 'system';}}
function apply(t:Theme){const resolved=t==='system'?(matchMedia('(prefers-color-scheme: light)').matches?'light':'dark'):t;document.documentElement.dataset.theme=resolved;document.querySelector('meta[name="theme-color"]')?.setAttribute('content',resolved==='light'?'#f8fafc':'#0b0f14');}
export function ThemeListener(){useEffect(()=>{const update=()=>apply(read());update();const media=matchMedia('(prefers-color-scheme: light)');media.addEventListener('change',update);window.addEventListener('storage',update);return()=>{media.removeEventListener('change',update);window.removeEventListener('storage',update);};},[]);return null;}
export function ThemeSettings(){const [theme,setTheme]=useState<Theme>(read);useEffect(()=>{const sync=()=>setTheme(read());window.addEventListener('storage',sync);return()=>window.removeEventListener('storage',sync);},[]);return <section className="theme-settings" aria-label="外观设置"><h3>外观</h3><label>页面主题 <select aria-label="页面主题" value={theme} onChange={e=>{const value=e.target.value as Theme;setTheme(value);apply(value);try{localStorage.setItem(key,value);}catch{}}}><option value="system">跟随系统</option><option value="dark">深色</option><option value="light">浅色</option></select></label><p className="muted">保存在当前浏览器，跟随系统时自动响应系统外观变化。</p></section>}
