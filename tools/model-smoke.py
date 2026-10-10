"""Authorized production check; reads private credentials only on the server."""
import runpy, json, time
from pathlib import Path

ctx = runpy.run_path(str(Path(__file__).with_name('server-smoke.py')))
req = ctx['req']
req('/session', 'POST', {'token': ctx['values']['ADMIN_TOKEN']})
headers = {'X-CSRF-TOKEN': req('/session')[0]['csrfToken']}
models = req('/ai/models')[0]['items']
event = req('/events/page')[0]['items'][0]
session = req('/agent/sessions', 'POST', {}, headers)[0]
model = ctx['values']['OPENAI_MODEL']
assert model in models
message = req('/agent/sessions/'+session['id']+'/messages', 'POST', {
    'action': 'search', 'prompt': '请分析当前事件，区分报道和推测',
    'requestId': 'real-model-acceptance', 'eventId': event['id'], 'model': model
}, headers)[0]
for _ in range(90):
    task = req('/agent/tasks/'+message['id'])[0]
    if task['status'] not in ('queued', 'running'): break
    time.sleep(1)
assert task['status'] == 'completed', task['progress']
answer = json.loads(task['result'])
assert answer['sources'] and task['model'] == model and not answer.get('draftId')
print(json.dumps({'modelCount': len(models), 'model': model, 'status': task['status'],
    'citations': len(answer['sources']), 'usage': json.loads(task['usage'])}, ensure_ascii=False))
req('/session', 'DELETE', headers=headers)
