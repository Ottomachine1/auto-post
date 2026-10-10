// Read-only public SignalR connectivity check. No drafts or deliveries created.
import { createRequire } from 'node:module';
const require=createRequire(new URL('../web/package.json',import.meta.url));
const {HubConnectionBuilder,LogLevel,HttpTransportType}=require('@microsoft/signalr');
const base=process.env.LOAD_BASE_URL;
if(!base || !process.env.ADMIN_TOKEN) throw new Error('Private test configuration required');
const clients=Array.from({length:20},()=>new HubConnectionBuilder().withUrl(base+'/hubs/events',{headers:{Authorization:'Bearer '+process.env.ADMIN_TOKEN},transport:HttpTransportType.WebSockets}).configureLogging(LogLevel.None).build());
const received=new Set();clients.forEach((c,i)=>c.on('changes',()=>received.add(i)));
try {
 const began=performance.now();await Promise.all(clients.map(c=>c.start()));
 await new Promise(r=>setTimeout(r,5000));
 console.log(JSON.stringify({connections:clients.length,transport:'WebSockets',connectAllMs:performance.now()-began-5000,received:received.size}));
} finally {await Promise.all(clients.map(c=>c.stop()));}
