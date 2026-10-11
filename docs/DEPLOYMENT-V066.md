# v0.6.6 production acceptance — 2026-10-11

Source3355523, branchcodex/dotnet-rebuild. API and Worker run live on the designated server; cloud MySQL remains TLS identity pinned. Automatic publishing remains paused.

- Additive migrations010/011 applied. Public HTTPS health/session/CSRF/history/Agent checks passed.
- 50 production events returned bounded importance scores; repeat batch cache stable. Draft restore creates a new version and requires review. No external posts sent.
-95 source configurations,81 enabled; all8 bilingual RTHK candidates validated using explicit HTTPS targets.
- Postdeployment backup restored into isolatedautopost_v066_restore_20261011:18 tables,9100 events,57 score snapshots, all table counts and hashes matched. Daily backup schedule retained.
- Main CI38136100125 passed PostgreSQL85, MySQL85, browser10 and image builds.
- Live browser automation timed out; this is not recorded as successful production visual acceptance.

The first v0.6.6 attempt rolled back because the acceptance harness omitted the required approve revision. Backend correctly rejected it. Harness corrected to revision1, test fixtures soft-trashed, retry passed. The earlier v0.6.5 tools concurrency incident is documented separately.

Pending V2 scope: full X option integration/authorized platform test, themes, analytics, richer Agent tools, score calibration and expanded source management. Current score is an explainable heuristic, not verified factual confidence or publishing authorization.
