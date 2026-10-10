"""Production acceptance; reads private credentials without printing them."""
import json
import time
import urllib.request
from pathlib import Path

values = dict(line.split("=", 1) for line in Path("/mnt/storage/auto-post/private.env").read_text().splitlines() if "=" in line and not line.startswith("#"))


def request(path, method="GET"):
    req = urllib.request.Request("https://auto-post.maxson.cc/api" + path, method=method,
        data=b"{}" if method == "POST" else None,
        headers={"Authorization": "Bearer " + values["ADMIN_TOKEN"], "Content-Type": "application/json", "User-Agent": "AutoPostAcceptance/0.3 (+https://auto-post.maxson.cc)"})
    with urllib.request.urlopen(req, timeout=30) as response:
        return json.load(response)


page = request("/events/page?q=Bitcoin")
item = next(e for e in page["items"] if not e["demo"] and e.get("language", "").lower().startswith("en") and len(e["body"]) < 2000)
original_title = item["title"]
request("/events/" + item["id"] + "/translation", "POST")
for attempt in range(120):
    item = request("/events/" + item["id"])["item"]
    if item["translationStatus"] in ("completed", "failed"):
        break
    time.sleep(1)
assert item["translationStatus"] == "completed", item["translationStatus"]
assert item["chineseTitle"] and item["translationEngine"] == "argos-offline"
assert item["title"] == original_title
assert any("\u4e00" <= c <= "\u9fff" for c in item["chineseTitle"])
print(json.dumps({"eventId": item["id"], "status": item["translationStatus"], "engine": item["translationEngine"], "originalPreserved": True, "chineseTitle": item["chineseTitle"]}, ensure_ascii=False))
