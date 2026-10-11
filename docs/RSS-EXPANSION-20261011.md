# RSS expansion 2026-10-11

Added 8 bilingual RTHK candidates: Local, Greater China, World and Finance, in Chinese and English. Catalog now contains 95 candidates. All additions default disabled and must pass the existing HTTP/XML/freshness/network validation before enabling. Chinese and English channels from the same publisher are not independent corroboration.

Official directories: https://news.rthk.hk/rthk/ch/rss.htm and https://news.rthk.hk/rthk/en/rss.htm . Reading feed metadata does not authorize republication of full copyrighted stories.

Deployed to production on 2026-10-11. All eight RTHK candidates passed normal validation and were enabled; total sources95, enabled81 at the v0.6.6 acceptance check. Validation does not establish complete coverage or republication rights.

Server preflight found the official directory URLs return301 to http://rthk9.rthk.hk/. HTTP downgrade was not followed. The same RTHK host was checked explicitly over HTTPS with normal certificate verification and returned200. Catalog URLs now use https://rthk9.rthk.hk/; runtime changes require the normal XML/freshness validation before activation. IDs remain stable.
