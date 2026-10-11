# Reversible event quarantine — manual v1

Migration013 adds IsQuarantined, ModerationVersion, ModerationReason and QuarantinedAt to Events plus a mode/visibility/publication index. No new business tables or deletion. Old JSON backups normalize added event fields to visible/version0 during typed import.

Authenticated POST /api/events/{id}/moderation takes quarantine, expectedVersion, a bounded reason and required note. Existing write transaction/lock and CSRF apply. GET returns current state and latest100 audit decisions. Full decisions remain in Audits with previous state, new version, human stage/manual-v1, reason, note and affected draft IDs. Wrong mode, stale version and repeated state changes are rejected.

Ordinary /events, /events/page and SSE event lists exclude quarantined events; page supports visibility=visible|quarantined|all. Explicit event detail remains available for review. Agent retrieval excludes isolated sources and checks again under the final commit lock, so mid-flight quarantine cannot commit a new draft. Already-saved sessions/analyses remain historical records; they are not erased.

Quarantine cancels pending/running analysis and queued publication tasks, blocks queued deliveries and revokes approvals of unpublished related drafts. Collectors keep running. If any associated delivery is sending/unknown, quarantine rejects until the operator verifies it. Publication checks the source state again before sending. Model calls already reserved/in flight may finish; the analysis is retained but cannot create or publish a draft after quarantine. No platform delete call is made.

Restore changes visibility only: canceled jobs are not requeued, blocked deliveries are not resent and prior approvals are not restored. The operator must review/retry explicitly. Ordinary statistics retain all sampled reports and separately show quarantined count. Quarantine applies only to this event, not every similar report or every story from its publisher.

UI supports reason/note, explicit quarantine/restore, audit history and advanced-list isolated/all views. It uses the existing light/dark palette and retains raw content for review. Existing scoring is not factual verification.

Local initial acceptance: PostgreSQL6 and desktop/mobile2 passed, including queued cancellation, stale version, in-flight/unknown refusal, raw/versions preservation, model-completion and Agent-completion races. Final MySQL, migration-upgrade, analytics and CI gates still pending; not deployed yet.

Remaining filter scope: configurable automatic rule/semantic/model decisions, black/white lists, full categorical user feedback adaptation, sentiment and credibility predicates. Current manual-v1 is not an automatic classifier and is not completion of all V2 filtering.
