import os
os.environ['ADMIN_TOKEN']='test-token-with-more-than-thirty-two-characters'
os.environ['APP_MODE']='demo'
from fastapi.testclient import TestClient
from backend.app import db, main
import pytest

@pytest.fixture
def client(tmp_path,monkeypatch):
    monkeypatch.setattr(db,'PATH',str(tmp_path/'test.db'))
    monkeypatch.setattr(main,'DEMO',True)
    with TestClient(main.app) as client: yield client

def headers(): return {'Authorization':'Bearer '+os.environ['ADMIN_TOKEN']}

def test_auth_and_demo_read(client):
    assert client.get('/api/events').status_code==401
    rows=client.get('/api/events',headers=headers()).json()
    assert len(rows)==6 and all(r['demo'] for r in rows)
    assert client.get('/api/events?q=比特币',headers=headers()).json()[0]['source']=='crypto'

def test_draft_review_and_demo_publish_block(client):
    r=client.post('/api/drafts',headers=headers(),json={'content':'核验后的人工观察','channels':['x','binance']})
    assert r.status_code==200
    id=r.json()['id']
    assert client.post(f'/api/drafts/{id}/approve',headers=headers()).status_code==200
    assert client.post(f'/api/drafts/{id}/approve',headers=headers()).status_code==409
    assert client.post(f'/api/drafts/{id}/publish',headers=headers()).status_code==409
    assert client.get('/api/drafts',headers=headers()).json()[0]['status']=='approved'

def test_unknown_channel_and_long_x(client):
    assert client.post('/api/drafts',headers=headers(),json={'content':'test','channels':['invalid']}).status_code==422
    assert client.post('/api/drafts',headers=headers(),json={'content':'a'*281,'channels':['x']}).status_code==422

def test_deduplication(client):
    item=dict(source='rss',source_id='one',title='test',body='test',url='https://example.com/news',category='news',published_at=db.now())
    assert db.insert_event(item)
    assert not db.insert_event(item)

def test_publish_unique_claim_and_manual_export(client,monkeypatch):
    monkeypatch.setattr(main,'DEMO',False)
    calls=[]
    async def fake(channel,content):
        calls.append(channel)
        return ('manual_required',None) if channel=='binance' else ('published','123')
    monkeypatch.setattr(main,'deliver',fake)
    id=client.post('/api/drafts',headers=headers(),json={'content':'review me','channels':['x','binance']}).json()['id']
    assert client.post(f'/api/drafts/{id}/publish',headers=headers()).status_code==409
    client.post(f'/api/drafts/{id}/approve',headers=headers())
    assert client.post(f'/api/drafts/{id}/publish',headers=headers()).json()['status']=='partial'
    client.post(f'/api/drafts/{id}/publish',headers=headers())
    assert calls==['x','binance']
    deliveries=client.get('/api/drafts',headers=headers()).json()[0]['deliveries']
    assert {d['status'] for d in deliveries}=={'published','manual_required'}

def test_uncertain_delivery_never_retries(client,monkeypatch):
    monkeypatch.setattr(main,'DEMO',False)
    calls=[]
    async def uncertain(channel,content):
        calls.append(channel)
        raise main.AmbiguousDelivery('unknown')
    monkeypatch.setattr(main,'deliver',uncertain)
    id=client.post('/api/drafts',headers=headers(),json={'content':'review me','channels':['x']}).json()['id']
    client.post(f'/api/drafts/{id}/approve',headers=headers())
    client.post(f'/api/drafts/{id}/publish',headers=headers())
    client.post(f'/api/drafts/{id}/publish',headers=headers())
    assert len(calls)==1
    assert client.get('/api/drafts',headers=headers()).json()[0]['deliveries'][0]['status']=='unknown'

def test_live_excludes_demo(client,monkeypatch):
    monkeypatch.setattr(main,'DEMO',False)
    assert client.get('/api/events',headers=headers()).json()==[]

def test_static_page(client):
    assert client.get('/').status_code==200
    assert client.get('/assets/app.js').status_code==200
