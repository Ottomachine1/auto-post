// API-level test, no browser automation; use only a disposable local demo database.
import { createRequire } from "node:module";
const require = createRequire(new URL("../web/package.json", import.meta.url));
const { HubConnectionBuilder, LogLevel } = require("@microsoft/signalr");
const base = process.env.LOAD_BASE_URL || "http://127.0.0.1:5080";
const token = process.env.ADMIN_TOKEN;
if (!token) throw new Error("ADMIN_TOKEN required");
const headers = {
  Authorization: "Bearer " + token,
  "Content-Type": "application/json",
};
const before = await fetch(base + "/api/changes?after=0", { headers }).then(
  (r) => r.json(),
);
let cursor = before.at(-1)?.id || 0;
while (true) {
  const rows = await fetch(base + "/api/changes?after=" + cursor, {
    headers,
  }).then((r) => r.json());
  if (rows.length) cursor = rows.at(-1).id;
  if (rows.length < 200) break;
}
const clients = Array.from({ length: 20 }, () =>
  new HubConnectionBuilder()
    .withUrl(base + "/hubs/events", { headers })
    .configureLogging(LogLevel.None)
    .build(),
);
const received = new Set();
let target, started;
const times = [];
clients.forEach((c, i) =>
  c.on("changes", (rows) => {
    if (target && rows.some((r) => r.target === target) && !received.has(i)) {
      received.add(i);
      times.push(performance.now() - started);
    }
  }),
);
try {
  await Promise.all(clients.map((c) => c.start()));
  started = performance.now();
  const response = await fetch(base + "/api/drafts", {
    method: "POST",
    headers,
    body: JSON.stringify({
      content: "Realtime load test " + Date.now(),
      channels: ["telegram"],
    }),
  });
  if (!response.ok) throw new Error("Draft creation failed " + response.status);
  target = (await response.json()).id;
  for (let i = 0; i < 50 && received.size < 20; i++)
    await new Promise((r) => setTimeout(r, 200));
  await clients[0].stop();
  const changes = await fetch(base + "/api/changes?after=" + cursor, {
    headers,
  }).then((r) => r.json());
  const recovered = changes.some((r) => r.target === target);
  console.log(
    JSON.stringify({
      connections: 20,
      received: received.size,
      maxDeliveryMs: Math.max(...times),
      httpReplayRecovered: recovered,
    }),
  );
  if (received.size !== 20 || !recovered) process.exitCode = 1;
} finally {
  await Promise.all(clients.map((c) => c.stop()));
}
