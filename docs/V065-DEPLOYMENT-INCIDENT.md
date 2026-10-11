# v0.6.5 deployment attempt

2026-10-11: commit51ef18f CI succeeded: PostgreSQL85, MySQL85, browser10, three image builds. Isolated restore of production backup verified all18 business tables, including defaulted new scoring tables and settings;9047 events.

Deployment backed up production to autopost-20261011T113055Z.json and applied additive migrations010/011. Before starting the new API/Worker, RSS preflight failed: MySqlConnection Cannot Open when State is Connecting. Tools feed-catalog shared one DbContextOptions with an externally supplied MySQL connection across parallel Store instances. Each validator now creates its own configured options/connection; the benchmark parallel contexts are corrected likewise.

Rollback restored v0.6.4 API and private release configuration. Worker remained stopped until an18-table snapshot proved no archived/deleted drafts, no sending/unknown deliveries, zero deliveries overall, no scoring records, automatic publishing paused. Then the v0.6.4 Worker resumed. New additive columns/tables remain compatible and were not dropped. Candidate source imports remain disabled unless individually validated.

Regression evidence: repaired CLI validated all8 RTHK candidates concurrently against isolated cloud MySQL without connection-state errors (local feed requests timed out and stayed disabled). Server connectivity still needs separate validation; this is not successful feed collection evidence. v0.6.5 was not accepted; next release uses a new version.
