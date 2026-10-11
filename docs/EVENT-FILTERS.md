# Event advanced filters

/api/events/page retains source, search, category and publication-time cursor ordering. Optional language is normalized case-insensitively; from/to filter collection time as [from,to). minScore/maxScore are inclusive integers0–100. scoreState is all/scored/unscored; unscored cannot combine with score bounds. Invalid ranges/codes/combinations reject the request.

Score predicates select the latest stored snapshot per event by CreatedAt thenId. Earlier high scores cannot match when a later snapshot is low. Missing scores are not0 and are excluded from numeric ranges. Mode isolation is applied by the normal event query and score subquery. PostgreSQL/MySQL queries remain server-side and use existing indexes.

Authenticated GET /api/scoring/snapshots?ids=... returns saved details only for1–200 unique authorized-mode event IDs; missing snapshots are omitted. It creates no score/model/translation jobs. Advanced score filtering uses this read-only endpoint instead of the normal viewport score-calculation request, preserving unscored records. Ordinary browsing keeps the existing score calculation behavior. The source/score database is live; concurrent changes can move results between refreshes, and cursor pagination is not a frozen export.

UI: expandable advanced controls, local-time input conversion, explicit apply/clear, score state and bounds, normalized language code. Changing filters resets the query/pagination key. Criteria-specific score cache prevents reusing a snapshot request across different applied filters. No source content is deleted or quarantined by these controls.

Local verification: PostgreSQL2 targeted tests passed, including52-result pagination, latest-score precedence, unscored behavior, mode rejection, invalid input and zero job/score writes. Desktop/mobile browser2 passed, asserting no score POST during filtered reads and clear/reset behavior. Pinned cloud MySQL5 targeted tests (filters plus shared analytics latest-snapshot regression) passed. Final desktop/mobile rerun2 and frontend build passed. Full CI pending. No production deployment yet.

Remaining filtering scope: audited rule/semantic/AI/manual decisions, reversible quarantine, configurable black/white lists, sentiment/credibility and cross-platform filters. This increment does not claim those mechanisms are finished.
