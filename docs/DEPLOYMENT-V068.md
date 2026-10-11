# v0.6.8 acceptance — 2026-10-11

Source: a9869bf on codex/dotnet-rebuild. Includes advanced language/time/importance filters, stored-score lookups and reversible manual event quarantine (migration013). Source scoring remains value-v1; this release does not claim automatic credibility calibration or complete V2 delivery.

CI Dotnet workspace run38140630590 completed successfully: PostgreSQL97, MySQL97, desktop9, mobile9, frontend build and API/Worker/tools image builds. Verify runs38140630591 and38140626499 passed. Local cloud MySQL focused acceptance passed10 tests; PostgreSQL passed9. A local combined browser run had one intermittent mobile moderation failure, followed by a successful four-test desktop/mobile rerun; CI's full18 browser tests passed.

Production upgrade uses private configuration, the existing pinned TLS MySQL connection, release directory releases/v0.6.8 and API/Worker images0.6.8. The preceding release and its database backups remain available. Writers stop before the final pre-migration export. Migration is an explicit tools step. Automatic publishing stays paused.

Public HTTPS acceptance returned live/ok, secure session cookie, CSRF rejection, distinct history pages, keyword CRUD, 95 source configurations (81 enabled), 9,410 sampled events, and Agent retrieval with12 citations. Analytics for a fixed historical24h interval matched the pre-upgrade export:3,782 reports; timeline, source and topic totals matched. Theme assets and read-only quarantine APIs passed. No production event was quarantined/restored and no external post was sent by this acceptance.

Post-upgrade backup restored successfully into isolated autopost_v068_restore_20261011. All18 business tables matched their counts and canonical SHA-256 hashes:9,419 events,991 analyses,993 drafts,994 draft versions,95 sources,57 stored scores and0 deliveries. Evidence: server private/migration/v068-final-restore-report.json. Live API health remained ok after the restore. Full V2 still includes adaptive source quality, automatic filtering, richer analytics and Agent workflows, supported X options, and reference-load testing.
