SyncVote – Agent Notes

Goal
- Deliver a Jellyfin plugin enabling group decisions on what to watch during SyncPlay using swipe-style voting.

Current State
- Backend API: rooms, join, start voting, vote (like/dislike), results, permissions.
- Models: VotingRoom, Vote, UserPermissions.
- Config page (admin): default time limit, public rooms, auto-start, default sort.
- Frontend assets (mock): Web/syncvote.js, Web/syncvote.css not wired into Jellyfin UI.

Key Gaps / TODO (priority)
1) Content feed + filtering
   - Build queries to Jellyfin library (collections/genres/sort) via ILibraryManager/queries.
   - Expose API to fetch next candidate item(s) for a room.
2) SortBy alignment
   - Align enum values with UI/config and library sorting capabilities.
3) SyncPlay integration (server-side)
   - On winner, queue/play in current SyncPlay group via Jellyfin services.
4) Access checks
   - Detect members lacking access to candidate/winner; inform organizer.
5) Real-time updates
   - Push room/vote state via WebSocket for responsive UI.
6) Persistence
   - Store rooms/votes/permissions beyond runtime (e.g., IApplicationPaths data file or DB).
7) UI wiring
   - Decide supported client(s) and delivery method for injecting buttons/UI.

Design Notes
- Security: All endpoints require Jellyfin auth; validate organizer for privileged actions.
- Data: VotingRoom exposes read-only collections with mutators to prevent accidental external writes.
- Extensibility: Keep VotingRoomService small; consider interfaces for storage and SyncPlay control.

Open Questions
- Which Jellyfin client(s) to target initially for UI (Web, Android TV, etc.)?
- Preferred sort semantics: A-Z/Z-A, rating, date added, or release date?
- Max room size and time limit bounds?

Packaging/Metadata
- Keep GUID consistent across:
  - KappuCitti.Plugin.SyncVote/Plugin.cs:32
  - KappuCitti.Plugin.SyncVote/Configuration/configPage.html (pluginId const)
  - manifest.json (guid/targetAbi/framework)

Testing Plan
- Unit tests for VotingRoomService (room lifecycle, voting rules, result ordering).
- Integration tests for API controller (auth required; use test host if feasible).
- Manual test against Jellyfin 10.9+ for SyncPlay interoperability.

Non-Goals (for first MVP)
- Complex tournament modes.
- Cross-library recommendations and external rating integrations.
