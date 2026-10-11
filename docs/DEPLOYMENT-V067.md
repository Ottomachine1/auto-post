# v0.6.7 acceptance — 2026-10-11

Source0f03a06 on codex/dotnet-rebuild. Includes persistent Light/Dark/System themes and authenticated database-backed analytics. Application binaries match12ff9b4; subsequent commit changes test isolation and documentation only.

CI38138038676 passed PostgreSQL88, MySQL88, desktop7, mobile7 and all3 image builds. Desktop/mobile suites use separate backend lifetimes to avoid a combined test login rate beyond10/minute; production limiter remains unchanged.

Preflight: migration012 applied twice to isolated restored cloud database, then all18 table hashes/counts still matched9100 events/57 score snapshots. A temporary Linux API container queried the same restored cloud database and matched9096 non-demo reports in the selected30-day interval; container removed afterwards. No production data reset or external post was performed.

Deployment and postdeployment restore results will be appended after verification. Automatic publishing must remain paused. Visual live-browser inspection is not covered by these API/asset checks.

Acceptance harness incidents: attempt1 received HTTP403 with the default Python client on the public endpoint; setting the same explicit User-Agent as the already-passing public acceptance client resolved that request. Attempt2 passed analytics counts but failed an incorrect HTML assertion: the head script uses dataset.theme, not a literal data-theme attribute. Both attempts rolled back API-only and stopped Worker for review; backups were retained separately. Corrected harness submitted for retry, without changing production access protection or application code.

Deployment succeeded on the corrected third attempt: current points to releases/v0.6.7, API/Worker images0.6.7, live mode, pinned cloud MySQL. Public secure-session/CSRF/history/Agent checks passed; fixed24-hour analytics matched3791 reports across67 sources with complete bucket totals. Five channel summaries and new theme HTML/CSS assets verified. Automatic publishing paused. Postdeployment backup restore subsequently passed.

Final restore proof: production postdeployment snapshot restored into isolatedautopost_v067_restore_20261011, all18 tables matched counts andSHA-256;9332 events. API health remainslive/ok. Daily backup configuration retained. Full V2 scope is not complete: filtering/quarantine, richer Agent, adaptive source-quality evidence, sentiment and full X options remain pending.
