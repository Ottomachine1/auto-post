import asyncio, hmac, json, os
from contextlib import asynccontextmanager
from pathlib import Path
from fastapi import FastAPI, Depends, HTTPException, Request
from fastapi.responses import StreamingResponse, FileResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field
from . import db
from .providers import collect, analyse, deliver, CHANNELS, AmbiguousDelivery

DEMO=os.getenv('APP_MODE','demo')=='demo'

async def collector():
    while True:
        if not DEMO:
            try: await collect()
            except Exception: db.health('collector','error','采集循环失败')
        await asyncio.sleep(max(30,int(os.getenv('COLLECT_INTERVAL','60'))))

@asynccontextmanager
async def lifespan(app):
    if not DEMO and len(os.getenv('ADMIN_TOKEN',''))<32: raise RuntimeError('生产模式必须配置至少 32 字符 ADMIN_TOKEN')
    db.init()
    if DEMO: db.seed()
    with db.connect() as c:
        c.execute("UPDATE deliveries SET status='unknown', error='服务重启，请人工核验平台发送结果' WHERE status='sending'")
    task=asyncio.create_task(collector())
    yield
    task.cancel()
    try: await task
    except asyncio.CancelledError: pass

app=FastAPI(title='Signal Atlas API',lifespan=lifespan)

def auth(request:Request):
    token=os.getenv('ADMIN_TOKEN','')
    supplied=request.headers.get('Authorization','').removeprefix('Bearer ')
    if not token or not hmac.compare_digest(token,supplied): raise HTTPException(401,'请输入管理访问令牌')

@app.get('/api/health')
def health(): return {'status':'ok','mode':'demo' if DEMO else 'live'}

@app.get('/api/status',dependencies=[Depends(auth)])
def status():
    with db.connect() as c:
        return {'mode':'demo' if DEMO else 'live','sources':[dict(r) for r in c.execute('SELECT * FROM source_health')], 'ai_configured':bool(os.getenv('OPENAI_API_KEY')), 'channels':[{'id':k,'label':v['label'],'mode':v['mode'],'configured':v['configured']()} for k,v in CHANNELS.items()]}

def events(source='',q='',limit=50):
    with db.connect() as c:
        sql='SELECT * FROM events WHERE demo=?'
        params=[int(DEMO)]
        if source: sql+=' AND source=?';params.append(source)
        if q: sql+=' AND (title LIKE ? OR body LIKE ?)';params.extend(['%'+q+'%']*2)
        sql+=' ORDER BY published_at DESC LIMIT ?';params.append(limit)
        return [dict(r) for r in c.execute(sql,params)]

@app.get('/api/events',dependencies=[Depends(auth)])
def get_events(source:str='',q:str='',limit:int=50): return events(source,q,min(max(limit,1),200))

@app.get('/api/stream',dependencies=[Depends(auth)])
async def stream(request:Request):
    async def generate():
        previous=None
        while not await request.is_disconnected():
            data=json.dumps(events(),ensure_ascii=False)
            if data!=previous:
                yield 'event: events\ndata: '+data+'\n\n';previous=data
            else: yield ': heartbeat\n\n'
            await asyncio.sleep(3)
    return StreamingResponse(generate(),media_type='text/event-stream',headers={'Cache-Control':'no-cache','X-Accel-Buffering':'no'})

@app.post('/api/collect',dependencies=[Depends(auth)])
async def manual_collect():
    if DEMO: raise HTTPException(409,'演示模式不采集真实数据')
    await collect();db.audit('collect','sources');return {'ok':True}

@app.post('/api/events/{event_id}/analysis',dependencies=[Depends(auth)])
async def analysis(event_id:str):
    with db.connect() as c:
        event=c.execute('SELECT * FROM events WHERE id=? AND demo=?',(event_id,int(DEMO))).fetchone()
        cached=c.execute('SELECT result FROM analyses WHERE event_id=?',(event_id,)).fetchone()
    if not event: raise HTTPException(404,'事件不存在')
    if cached: return json.loads(cached['result'])
    try: result=await analyse(dict(event))
    except ValueError as e: raise HTTPException(409,str(e))
    except Exception: raise HTTPException(502,'模型服务失败，请检查服务与配额')
    with db.connect() as c: c.execute('INSERT OR REPLACE INTO analyses VALUES(?,?,?)',(event_id,json.dumps(result,ensure_ascii=False),db.now()))
    db.audit('analyse',event_id);return result

class Draft(BaseModel):
    content:str=Field(min_length=1,max_length=10000)
    channels:list[str]=Field(min_length=1,max_length=5)
    event_id:str|None=None

@app.post('/api/drafts',dependencies=[Depends(auth)])
def create_draft(body:Draft):
    if not body.content.strip(): raise HTTPException(422,'内容不能为空')
    if any(x not in CHANNELS for x in body.channels): raise HTTPException(422,'未知渠道')
    if 'x' in body.channels and len(body.content)>280: raise HTTPException(422,'X 草稿超过当前 280 字符限制，请编辑后保存')
    id=db.uid()
    with db.connect() as c:
        if body.event_id and not c.execute('SELECT 1 FROM events WHERE id=? AND demo=?',(body.event_id,int(DEMO))).fetchone(): raise HTTPException(422,'来源事件不存在')
        c.execute('INSERT INTO drafts VALUES(?,?,?,?,?,?,?,?)',(id,body.content,json.dumps(list(dict.fromkeys(body.channels))),body.event_id,'draft',1,db.now(),db.now()))
    db.audit('draft_created',id);return {'id':id,'status':'draft'}

@app.get('/api/drafts',dependencies=[Depends(auth)])
def drafts():
    with db.connect() as c:
        rows=[dict(r) for r in c.execute('SELECT * FROM drafts ORDER BY created_at DESC LIMIT 100')]
        for r in rows:
            r['channels']=json.loads(r['channels']);r['deliveries']=[dict(x) for x in c.execute('SELECT * FROM deliveries WHERE draft_id=?',(r['id'],))]
        return rows

@app.post('/api/drafts/{id}/approve',dependencies=[Depends(auth)])
def approve(id:str):
    with db.connect() as c:
        cursor=c.execute("UPDATE drafts SET status='approved',updated_at=? WHERE id=? AND status='draft'",(db.now(),id))
        if not cursor.rowcount: raise HTTPException(409,'草稿不存在或已审核')
    db.audit('approved',id);return {'status':'approved'}

@app.post('/api/drafts/{id}/publish',dependencies=[Depends(auth)])
async def publish(id:str):
    if DEMO: raise HTTPException(409,'演示模式禁止对外发布，可导出草稿')
    with db.connect() as c:
        row=c.execute('SELECT * FROM drafts WHERE id=?',(id,)).fetchone()
        if not row or row['status'] not in ('approved','partial','published'): raise HTTPException(409,'请先审核草稿')
    for channel in json.loads(row['channels']):
        with db.connect() as c:
            # Atomic unique claim: repeats never submit the same channel twice.
            claim=c.execute('INSERT OR IGNORE INTO deliveries VALUES(?,?,?,?,?,?,?,?)',(db.uid(),id,channel,'sending',None,None,db.now(),db.now()))
            if not claim.rowcount: continue
        try:
            state,remote=await deliver(channel,row['content']);error=None
        except AmbiguousDelivery as e: state='unknown';remote=None;error=str(e)
        except Exception: state='failed';remote=None;error='发送失败，请检查凭据、配额和内容限制'
        with db.connect() as c: c.execute('UPDATE deliveries SET status=?,remote_id=?,error=?,updated_at=? WHERE draft_id=? AND channel=?',(state,remote,error,db.now(),id,channel))
    with db.connect() as c:
        states=[r['status'] for r in c.execute('SELECT status FROM deliveries WHERE draft_id=?',(id,))]
        final='published' if states and all(x=='published' for x in states) else 'partial'
        c.execute('UPDATE drafts SET status=?,updated_at=? WHERE id=?',(final,db.now(),id))
    db.audit('publish_attempt',id);return {'status':final}

@app.get('/api/audit',dependencies=[Depends(auth)])
def audit():
    with db.connect() as c: return [dict(r) for r in c.execute('SELECT * FROM audit ORDER BY created_at DESC LIMIT 100')]

ROOT=Path(__file__).resolve().parents[2]/'frontend'/'dist'
app.mount('/assets',StaticFiles(directory=ROOT/'assets'),name='assets')
@app.get('/')
def index(): return FileResponse(ROOT/'index.html')
