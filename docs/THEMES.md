# Workspace appearance

System status → Appearance offers Light, Dark and System. The preference is browser-local (atlas-theme), independent of administrator credentials and business records. Default is System; when storage is unavailable the page remains usable. An early head script resolves the palette before the React entry loads. A root listener handles OS changes and cross-tab storage updates. Manual Light/Dark overrides the OS.

The workspace, composer and Agent share explicit dark/light CSS token tables; images and attachments are not inverted. Native form controls use the matching color-scheme. No network or model calls are needed for theme changes.

2026-10-11 local acceptance: production TypeScript/build passed; desktop and mobile browser tests passed for preference persistence, system changes, manual override, base text contrast >=4.5:1 and no horizontal overflow. Tests ran against the frontend in disconnected fallback mode; full connected CI remains the regression gate. This change has not yet been deployed to production.

The contrast check covers base text only, not a complete accessibility audit. Further visual review of every component and status color remains required.
