"""Exercise real RSS and the manual workflow in an isolated temporary database.

Run from the repository root: python -m scripts.verify_rss_workflow
No model/platform credentials are used and no external messages are sent.
"""
import json
import os
import secrets
import tempfile
from pathlib import Path

from fastapi.testclient import TestClient


def verify():
    # This standalone process deliberately cannot call paid models or send posts.
    for name in ('OPENAI_API_KEY', 'X_BEARER_TOKEN', 'X_USER_ACCESS_TOKEN',
                 'TELEGRAM_BOT_TOKEN', 'TELEGRAM_CHAT_ID'):
        os.environ.pop(name, None)
    os.environ['APP_MODE'] = 'live'
    os.environ['ADMIN_TOKEN'] = secrets.token_urlsafe(48)
    os.environ['RSS_URLS'] = os.getenv('RSS_URLS') or (
        'https://www.coindesk.com/arc/outboundfeeds/rss/,'
        'https://feeds.bbci.co.uk/news/world/rss.xml'
    )
    with tempfile.TemporaryDirectory(prefix='atlas-verification-') as directory:
        os.environ['DATABASE_PATH'] = str(Path(directory) / 'workflow.db')
        from backend.app import db, main
        # Explicit collection only, so the background timer cannot skew counts.
        async def idle():
            import asyncio
            await asyncio.Event().wait()
        main.collector = idle
        headers = {'Authorization': 'Bearer ' + os.environ['ADMIN_TOKEN']}
        with TestClient(main.app) as client:
            assert client.get('/api/events').status_code == 401
            assert client.post('/api/collect', headers=headers).status_code == 200
            sources = client.get('/api/status', headers=headers).json()['sources']
            assert sources and all(s['status'] == 'connected' for s in sources), sources
            with db.connect() as connection:
                count = connection.execute('SELECT COUNT(*) FROM events').fetchone()[0]
            events = client.get('/api/events', headers=headers).json()
            assert count > 0 and events and all(not e['demo'] for e in events)
            assert client.post('/api/collect', headers=headers).status_code == 200
            with db.connect() as connection:
                repeated = connection.execute('SELECT COUNT(*) FROM events').fetchone()[0]
            # A live feed may add entries between requests; prior IDs must stay unique.
            with db.connect() as connection:
                duplicates = connection.execute(
                    'SELECT source,source_id FROM events GROUP BY source,source_id HAVING COUNT(*)>1'
                ).fetchall()
            assert not duplicates
            event = events[0]
            content = '流程验收草稿，请人工核验原文后决定是否发布。\n' + event['title'] + '\n' + event['url']
            response = client.post('/api/drafts', headers=headers, json={
                'content': content, 'channels': ['binance', 'okx', 'truth'], 'event_id': event['id']
            })
            assert response.status_code == 200
            draft_id = response.json()['id']
            endpoint = '/api/drafts/' + draft_id
            assert client.post(endpoint + '/publish', headers=headers).status_code == 409
            assert client.post(endpoint + '/approve', headers=headers).status_code == 200
            assert client.post(endpoint + '/publish', headers=headers).json()['status'] == 'partial'
            assert client.post(endpoint + '/publish', headers=headers).status_code == 200
            draft = client.get('/api/drafts', headers=headers).json()[0]
            assert draft['content'] == content
            assert len(draft['deliveries']) == 3
            assert all(d['status'] == 'manual_required' and d['remote_id'] is None for d in draft['deliveries'])
            assert client.post('/api/events/' + event['id'] + '/analysis', headers=headers).status_code == 409
            with db.connect() as connection:
                assert connection.execute('SELECT content FROM drafts WHERE id=?', (draft_id,)).fetchone()[0] == content
            actions = {row['action'] for row in client.get('/api/audit', headers=headers).json()}
            assert {'collect', 'draft_created', 'approved', 'publish_attempt'} <= actions
            print(json.dumps({
                'environment': 'isolated build environment, not target server',
                'sources': sources, 'events_first_collection': count,
                'events_second_collection': repeated, 'duplicate_source_ids': len(duplicates),
                'authentication': 'passed', 'manual_creation_and_review': 'passed',
                'persistent_draft_and_audit': 'passed',
                'manual_channels': 'manual_required; not published',
                'model_without_credentials': 'blocked as expected',
                'external_api_publishing': 'not attempted; credentials required'
            }, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    verify()
