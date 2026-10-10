import json,urllib.request,urllib.error,http.cookiejar,sys
from pathlib import Path
values=dict(line.split('=',1) for line in Path('/mnt/storage/auto-post/private.env').read_text().splitlines() if '=' in line and not line.startswith('#'))
base='https://auto-post.maxson.cc'
jar=http.cookiejar.CookieJar();client=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
def req(path,method='GET',data=None,headers=None):
 request=urllib.request.Request(base+'/api'+path,data=None if data is None else json.dumps(data).encode(),method=method,headers={'Content-Type':'application/json','User-Agent':'AutoPostAcceptance/0.3 (+https://auto-post.maxson.cc)',**(headers or {})})
 with client.open(request,timeout=30) as r:
  body=r.read();return (json.loads(body) if body else {}),r.headers
result={}
result['health']=req('/health')[0]
try:req('/status');raise RuntimeError('Unauthenticated status was allowed')
except urllib.error.HTTPError as e:assert e.code==401
req('/session','POST',{'token':values['ADMIN_TOKEN']})
result['cookieSecure']=all(c.secure for c in jar if c.name=='atlas-session')
csrf=req('/session')[0]['csrfToken'];headers={'X-CSRF-TOKEN':csrf}
status=req('/status')[0];result.update(events=status['events'],sources=len(status['sources']),enabled=sum(s['enabled'] for s in status['sources']),autoPaused=status['settings']['autoPaused'],modelConfigured=status['ai_configured'],embeddingConfigured=status.get('embedding_configured',False))
result['databaseProvider']=status.get('databaseProvider')
result['binanceConfigured']=next((c['configured'] for c in status['channels'] if c['id']=='binance'),False)
page=req('/events/page')[0];result['firstPage']=len(page['items']);result['historyCursor']=bool(page['nextCursor'])
if page['nextCursor']:
 from urllib.parse import quote
 second=req('/events/page?cursor='+quote(page['nextCursor'],safe=''))[0]
 assert not set(e['id'] for e in page['items']).intersection(e['id'] for e in second['items']);result['historyDistinct']=True
settings={'autoPaused':True,'analysisDailyLimit':status['settings']['analysisDailyLimit'],'channelDailyLimit':status['settings']['channelDailyLimit']}
try:req('/settings','PUT',settings);raise RuntimeError('CSRF missing write was allowed')
except urllib.error.HTTPError as e:assert e.code==403
source,_=req('/sources','POST',{'kind':'rss','name':'Temporary disabled server acceptance source','address':'','topic':'bitcoin ETF','enabled':False,'category':'crypto'},headers)
assert '&hl=en-US&' in source['address'];source['intervalSeconds']=1800
req('/sources/'+source['id'],'PUT',source,headers);req('/sources/'+source['id'],'DELETE',headers=headers);result['keywordCrud']=True
result['csrfRejected']=True
req('/session','DELETE',headers=headers)
print(json.dumps(result,ensure_ascii=False))
