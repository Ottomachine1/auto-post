# Intelligence analytics

Authenticated read-only GET /api/analytics supports range=1h|24h|7d|30d|custom. Custom requires Unix-second from/to, positive past interval, maximum90days, [from,to). Event statistics use collectedAt; delivery statistics use deliveryCreatedAt. UI timestamps display Asia/Shanghai; custom datetime inputs use the browser timezone and are converted to Unix seconds.

Repeatable-read snapshot queries aggregate in the database: total sampled reports, existing analyses, sources, original categories, processing states, time buckets, delivery channel/status. Latest persisted score per event is selected by CreatedAt thenId; missing scores are excluded from averages and coverage is shown. Reading never creates score snapshots or model/translation tasks. Timeline includes zero buckets. Raw stories and repeated score snapshots are not loaded into the browser.

Channel metadata distinguishes configured credentials, missing credentials and manual export. Configuration is not successful integration evidence; zero records are not proof of zero news on an external platform. Sentiment is explicitly unavailable because the existing validated analysis schema contains no sentiment field. Sample volume is not global popularity, independent evidence or verified truth.

Migration012 adds (Demo,CollectedAt) and (Demo,EventId,CreatedAt,Id) indexes, repeatably for MySQL and through normal EF migrations for PostgreSQL. No data is deleted and no new business tables are introduced.

Initial local validation: PostgreSQL3 and pinned cloud MySQL3 targeted tests passed; desktop/mobile browser2 passed against the real API and seeded isolated PostgreSQL. Final channel-inclusive checks passed: PostgreSQL3, pinned cloud MySQL3, connected desktop/mobile analytics and theme browser4, plus production frontend build. All tests used isolated databases; no production rows were reset. Full CI and production deployment remain pending.

Remaining analytics scope: cross-source event-group first-seen history, validated sentiment classifier/asset scope, actual value trend visualization, filtering/quarantine feedback and larger workload measurements. The current report is a first implementation, not completion of all V2 analytics requirements.

Average-score trends currently use latest persisted score snapshots grouped by event collection time, not historical as-of score reconstruction. Dedicated value trend visualization and score freshness/coverage explanations require further work.
