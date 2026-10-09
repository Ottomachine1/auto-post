import json, os, sqlite3, time, uuid
from contextlib import contextmanager

PATH = os.getenv('DATABASE_PATH', 'data/intelligence.db')

def now(): return int(time.time())

def uid(): return uuid.uuid4().hex

@contextmanager
def connect():
    os.makedirs(os.path.dirname(PATH) or '.', exist_ok=True)
    db = sqlite3.connect(PATH, timeout=20)
    db.row_factory = sqlite3.Row
    db.execute('PRAGMA journal_mode=WAL')
    try:
        yield db
        db.commit()
    finally: db.close()

def init():
    with connect() as db:
        db.executescript('''
        CREATE TABLE IF NOT EXISTS events (
          id TEXT PRIMARY KEY, source TEXT NOT NULL, source_id TEXT NOT NULL,
          title TEXT NOT NULL, body TEXT NOT NULL, url TEXT NOT NULL,
          category TEXT NOT NULL, published_at INTEGER NOT NULL,
          collected_at INTEGER NOT NULL, demo INTEGER NOT NULL DEFAULT 0,
          UNIQUE(source, source_id));
        CREATE TABLE IF NOT EXISTS analyses (
          event_id TEXT PRIMARY KEY, result TEXT NOT NULL, created_at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS drafts (
          id TEXT PRIMARY KEY, content TEXT NOT NULL, channels TEXT NOT NULL,
          event_id TEXT, status TEXT NOT NULL, revision INTEGER NOT NULL DEFAULT 1,
          created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS deliveries (
          id TEXT PRIMARY KEY, draft_id TEXT NOT NULL, channel TEXT NOT NULL,
          status TEXT NOT NULL, remote_id TEXT, error TEXT,
          created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL,
          UNIQUE(draft_id,channel));
        CREATE TABLE IF NOT EXISTS audit (
          id TEXT PRIMARY KEY, action TEXT NOT NULL, target TEXT NOT NULL, created_at INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS source_health (
          source TEXT PRIMARY KEY, status TEXT NOT NULL, error TEXT, updated_at INTEGER NOT NULL);
        ''')

def audit(action, target):
    with connect() as db:
        db.execute('INSERT INTO audit VALUES(?,?,?,?)', (uid(),action,target,now()))

def health(source,status,error=None):
    with connect() as db:
        db.execute('INSERT OR REPLACE INTO source_health VALUES(?,?,?,?)',(source,status,error,now()))

def insert_event(event):
    with connect() as db:
        c=db.execute('INSERT OR IGNORE INTO events VALUES(?,?,?,?,?,?,?,?,?,?)',
          (event.get('id',uid()),event['source'],event['source_id'],event['title'],event['body'],event['url'],event['category'],event['published_at'],now(),int(event.get('demo',False))))
        return bool(c.rowcount)

SEED = [
 ('global','全球宏观观察：利率预期如何影响风险资产？','演示情报，用于展示事件流布局。这不是实时新闻或投资建议。','宏观'),
 ('crypto','比特币生态观察：链上活动与资金流向','演示样例：通过原始来源核验链上指标，关注数据时间与口径。','加密'),
 ('x','X 热门议题观察：AI 与加密基础设施','演示社交事件：热门讨论不能代替事实核验。','社交'),
 ('truth','Truth Social 关注人物动态','演示内容：正式接入需合法的数据源或授权提供商。','政治'),
 ('crypto','以太坊生态：开发进展与应用采用率','演示样例，展示多来源事件聚合。','加密'),
 ('global','全球能源观察：供需变化与市场传导','演示内容，真实事件将保留来源链接和采集时间。','宏观')]

def seed():
    for i,(source,title,body,category) in enumerate(SEED):
        insert_event(dict(id=f'demo-{i}',source=source,source_id=f'demo-{i}',title=title,body=body,url='',category=category,published_at=now()-i*240,demo=True))
