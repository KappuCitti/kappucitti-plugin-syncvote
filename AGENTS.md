# SyncVote – Agent Notes

## Goal
- A Jellyfin plugin for group decisions on what to watch during SyncPlay, with swipe-style voting.

## Current state
- Backend (`KappuCitti.Plugin.SyncVote`): rooms, join, start/end voting, votes, results, permissions, candidate feed, library
  data for the create form, SyncPlay group lookup, access checks, play the winner through `ISyncPlayManager`.
- Services are registered by `PluginServiceRegistrator` (singletons). `VotingRoomService` is thread-safe, keeps rooms in memory
  (short-lived by design) and enforces the time limit itself. `LibraryService` and `SyncPlayService` are the only classes that
  talk to Jellyfin; `VotingRoomService` depends on `IItemInfoProvider` and `IPluginSettings`, which keeps it unit-testable.
- Web client: `Web/syncvote.js` + `Web/syncvote.css`, served by `ClientAssetsController` and added to `index.html` by
  `WebClientInjectionMiddleware` (no File Transformation / Custom JavaScript needed). Entry point is the SyncPlay menu only.
  Dialogs reuse Jellyfin's markup (`formDialog`, `emby-*`); `Dashboard.alert` cannot be used (it forces a single OK button).
- Language: the server picks it (`ServerConfiguration.UICulture`, or the plugin option `Language`). `GET /SyncVote/Client/i18n`
  returns English merged with that language. A new language = a new `Web/i18n/<code>.json`.
- Admin page: `Configuration/configPage.html`. Its `<script>` must stay **inside** the `data-role="page"` element, otherwise
  Jellyfin drops it.
- Compatibility: Jellyfin 10.10, 10.11 and 12.x. One source, one build per generation (`-p:JellyfinVersion=…`, see
  `Directory.Build.props` and the csproj). Type differences live in `Compat/` behind `JELLYFIN_10_10|10_11|12`.
  10.8 and older cannot work (`IPluginServiceRegistrator` appeared in 10.9).
- Versions: `X.Y.Z.<generation>` (`.0` = 10.10, `.1` = 10.11, `.2` = 12) so every server's catalog picks its own build.

## Build, test, release
- `dotnet test KappuCitti.Plugin.SyncVote.Tests` – unit tests (the sources under test are linked in, no Jellyfin version needed).
- `./build.ps1 -ReleaseVersion X.Y.Z` – tests + a zip per generation (the plugin dll only) in `artifacts/`;
  `-UpdateManifest -DownloadBaseUrl …` adds them to `manifest.json`. `release.ps1` wraps it (nothing is pushed without `-Push`).
- CI: `.github/workflows/ci.yml` (tests + build), `release.yml` (tag → release + manifest).
- `tests/e2e` – PowerShell scripts against a real server (API, real web client in headless Chrome, options). Use a **test**
  server only. See its README.

## Rules of the house
- Security: every endpoint requires Jellyfin authentication except the client files; organizer-only actions are checked in the
  service; the SyncPlay group of a room is the one the server sees for the user, never what the client claims.
- `Dashboard`-style globals are not stable across web client versions: the client only relies on `ApiClient`, the DOM of the
  SyncPlay menu (`.syncPlayGroupMenu`, `data-id="leave-group"`) and Jellyfin's CSS classes.
- Keep the GUID identical in `Plugin.cs`, `Configuration/configPage.html` and `manifest.json`.
- Code comments and Markdown are written in English.

## Remaining work
1. Real-time updates over WebSocket (the client polls every 2 s).
2. Optional persistence of rooms across restarts.
3. Series/episode voting in the UI (server side is ready: `ItemTypes`, episodes queued for a series winner).
4. Check the MUI menu entry (Jellyfin web 10.11+/12) on more themes and clients; other clients than the web one.
5. More languages (add `Web/i18n/<code>.json` and an option in the settings page).

## Non-goals (for now)
- Complex tournament modes.
- Cross-library recommendations and external rating integrations.
