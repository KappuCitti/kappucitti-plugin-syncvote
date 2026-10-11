# SyncVote (Jellyfin plugin)

Decide together what to watch. When a group is in a SyncPlay session, the organizer opens a voting room, everybody swipes
(like / dislike) through the same list of titles, and the winner starts playing for the whole group.

The screens are the Jellyfin ones (same dialogs and components), in English or Italian: the server picks the language.

## Compatibility

Jellyfin plugins are not binary compatible across server generations, so SyncVote ships one build per generation.
The catalog of your server offers the right one by itself.

| Jellyfin | Plugin version | `targetAbi` | Built on |
|---|---|---|---|
| 12.x | `X.Y.Z.2` | 12.0.0.0 | .NET 10 |
| 10.11.x | `X.Y.Z.1` | 10.11.0.0 | .NET 9 |
| 10.10.x | `X.Y.Z.0` | 10.10.0.0 | .NET 8 |

The 4th digit grows with the Jellyfin generation, so a server never offers a build made for an older one.
Jellyfin 10.9 is not tested. Jellyfin 10.8 and older are **not supported**: the plugin needs `IPluginServiceRegistrator`,
which appeared in 10.9.

## Installation

1. Dashboard → Plugins → Repositories → add
   `https://raw.githubusercontent.com/KappuCitti/kappucitti-plugin-syncvote/master/manifest.json`
2. Install **SyncVote** from the catalog and restart the server.

Nothing else is needed: the plugin adds its own script to the web client. If you load it with another plugin (File
Transformation, Custom JavaScript), turn off *Add SyncVote to the web interface* in the plugin settings.

## Using it

1. Everybody joins the same SyncPlay group.
2. Open the SyncPlay menu (top bar) and pick **SyncVote**.
3. The organizer creates the room: name, at least one library folder, optional genres, maximum age rating, time limit and
   order. A voting round has a time limit and ends by itself.
4. Members open the same menu entry and join. The organizer starts the voting when everybody is in (or the server does it
   when all the SyncPlay members joined, if *Auto-start voting* is on).
5. Everybody likes or dislikes the titles: buttons, ← / → keys or a swipe. Titles are the same, in the same order, for
   everyone, and only include what *every* member is allowed to see.
6. When the time is up (or the organizer ends it with the stop icon in the header) the results appear. The organizer can
   play the winner in the SyncPlay group, start a new round or close the room.

## Settings (Dashboard → Plugins → SyncVote)

| Option | Meaning |
|---|---|
| Default time limit, default order | Starting values of the create form |
| Max room members | Limit of members per room |
| Language | `Same as the server` (default), English, Italiano |
| Show every room to everyone | When off, users only see the rooms they belong to or the one of their SyncPlay group |
| Auto-start voting | Start when every SyncPlay member joined the room |
| Users can organize / vote | Defaults; administrators can always do both. Per-user exceptions go in the `UserPermissionOverrides` list of the plugin configuration file |
| Add SyncVote to the web interface | Turn off when another plugin loads the script |

## API

Everything needs a Jellyfin-authenticated user, except the client files.

| Endpoint | |
|---|---|
| `POST /SyncVote/Room` | Create a room for the SyncPlay group of the caller (needs permission, at least one library) |
| `GET /SyncVote/Rooms`, `GET /SyncVote/Room/{id}` | Rooms the caller may see |
| `POST /SyncVote/Room/{id}/Join` | Join (idempotent) |
| `POST /SyncVote/Room/{id}/StartVoting`, `…/EndVoting` | Organizer only |
| `DELETE /SyncVote/Room/{id}` | Close (organizer or administrator) |
| `GET /SyncVote/Room/{id}/Candidates?skip=&limit=` | Titles still to vote on, filtered, visible to all members, same order for all |
| `POST /SyncVote/Vote` | `{ RoomId, ItemId, IsLike }` |
| `GET /SyncVote/Room/{id}/Results` | Ranking and winner; how many members cannot access the winner |
| `POST /SyncVote/Room/{id}/PlayWinner` | Organizer only: queue and play the winner in the SyncPlay group |
| `GET /SyncVote/SyncPlayInfo` | SyncPlay group of the caller |
| `POST /SyncVote/CheckAccess` | Do the other group members see these library folders? (yes/no only) |
| `GET /SyncVote/Permissions[?userId]` | Own permissions (another user's: administrators) |
| `GET /SyncVote/Library/Collections`, `…/Genres`, `…/ParentalRatings` | Data for the create form |
| `GET /SyncVote/Client/syncvote.js`, `…/syncvote.css`, `…/i18n` | Client files and translations (public) |

## How it works

- `Plugin` + `PluginServiceRegistrator`: the plugin and its services (`VotingRoomService`, `LibraryService`, `SyncPlayService`).
- `VotingRoomService`: rooms and votes, in memory (rooms are short-lived), thread-safe. The voting time limit is enforced here.
- `LibraryService`: queries the Jellyfin library (filters, per-member visibility, ordering). `SyncPlayService`: finds the group
  of a user and plays the winner through `ISyncPlayManager`.
- `WebClientInjectionMiddleware`: adds the script to the web client's `index.html` while it is served.
- `Web/syncvote.js`: the client. It adds an entry to the SyncPlay menu and opens Jellyfin-style dialogs.
- `Compat/`: the few types Jellyfin moved between versions, selected at compile time (`JELLYFIN_10_10`, `JELLYFIN_10_11`, `JELLYFIN_12`).

## Development

Requirements: PowerShell 7, the .NET SDKs of the generations you build (8 for 10.10, 9 for 10.11, 10 for 12; the newest SDK builds them all).

```powershell
dotnet test KappuCitti.Plugin.SyncVote.Tests                                     # unit tests, no server needed
dotnet build KappuCitti.Plugin.SyncVote -p:JellyfinVersion=10.10.7               # one generation (10.10.x, 10.11.x, 12.x)
./build.ps1 -ReleaseVersion 0.2.0                                                # tests + all generations -> artifacts/*.zip
./release.ps1 -Version 0.2.0                                                     # build.ps1 + manifest.json (nothing pushed without -Push)
```

`tests/e2e` has end-to-end scripts that drive a real Jellyfin server (API, the real web client in headless Chrome, plugin
options); see its README. Use a test server, never a production one.

Each zip holds only `KappuCitti.Plugin.SyncVote.dll`; Jellyfin provides everything else. Keep the plugin GUID identical in
`Plugin.cs`, `Configuration/configPage.html` and `manifest.json`.

## Roadmap

- [x] Candidate feed with library, genre, age-rating and order filters
- [x] Voting with a server-side time limit, ranking, winner played in SyncPlay
- [x] Access checks (library folders before creating, winner after voting)
- [x] Automatic web client integration, Jellyfin-style UI, English and Italian
- [ ] Real-time updates over WebSocket (the client polls every 2 seconds now)
- [ ] Persistence of rooms across restarts (rooms are short-lived by design)
- [ ] Series and episodes in the voting UI (the server already supports them)
- [ ] Other clients than the web one

## Contributing

Pull requests are welcome: one branch per feature, describe the scenario and add tests when you can. Keep the code in
line with other Jellyfin plugins and avoid needless complexity.

## License

MIT. See `LICENSE`.
