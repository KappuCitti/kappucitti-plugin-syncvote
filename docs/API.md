# API reference

The plugin's HTTP API, under `/SyncVote`. The web client uses it; you can too (bots, other front-ends).

Every endpoint needs a Jellyfin-authenticated user, like the rest of the Jellyfin API: send the header
`Authorization: MediaBrowser Client="…", Device="…", DeviceId="…", Version="…", Token="<access token>"`. The only exception is
`/SyncVote/Client/*`, the files the web page loads before anybody is logged in.

> [!NOTE]
> JSON properties are `PascalCase`, like everywhere in Jellyfin's API. Ids are GUIDs.

## Rooms

| Method and path | Who | What |
|:---|:---|:---|
| `POST /SyncVote/Room` | A user allowed to organize | Create a room for the SyncPlay group of the caller. Body: [`CreateRoomRequest`](#createroomrequest) |
| `GET /SyncVote/Rooms` | Anyone | The rooms the caller may see |
| `GET /SyncVote/Room/{id}` | Anyone who may see it | One room |
| `POST /SyncVote/Room/{id}/Join` | A member of the room's SyncPlay group | Join. Joining twice is fine. A room with a [code](#room-code) needs it in the body: `{ "Code": "123456" }` |
| `POST /SyncVote/Room/{id}/StartVoting[?onlyLiked=true]` | The organizer | Start a round (a new round drops the previous votes). With `onlyLiked=true` it is a [runoff](#runoff) |
| `POST /SyncVote/Room/{id}/EndVoting` | The organizer | End the round now |
| `DELETE /SyncVote/Room/{id}` | The organizer or an administrator | Close the room |
| `GET /SyncVote/Room/{id}/Candidates?skip=0&limit=20` | A member | The titles the caller has not voted on yet |
| `POST /SyncVote/Vote` | A member | `{ "RoomId": "…", "ItemId": "…", "IsLike": true }`. Voting again on a title replaces the vote |
| `GET /SyncVote/Room/{id}/Results` | Anyone who may see it | The ranking, the winner and how many members cannot access the winner |
| `POST /SyncVote/Room/{id}/PlayWinner` | The organizer | Queue and play the winner in the SyncPlay group |

A room has `Id`, `Name`, `SyncPlayGroupId`, `OrganizerId`, `MemberIds`, `IsVotingActive`, `IsVotingFinished`, `TimeLimit`
(minutes), `SecondsRemaining` (only while voting), `SortBy`, `SelectedCollections`, `SelectedGenres`, `MaxParentalRating`,
`TitleFilter`, `ItemTypes`, `IsRunoff`, `RunoffItemIds`, `RequiresCode`, `CodeScope`, `CreatedAt` and `VotingStartedAt`; `Code` is
there only for the organizer and the administrators (see [room code](#room-code)).

The ranking orders titles by likes, then by fewer dislikes, then by who was liked first.

### Room code

A room can ask for a code, so that only the people who were given it can vote, however many people are in the SyncPlay group
(anybody who can use SyncPlay can join a group, and a group has no password).

- Ask for it with `RequireCode: true` in [`CreateRoomRequest`](#createroomrequest). The server makes a random 6-digit code.
- Everybody can see `RequiresCode`. The code itself (`Code`) is in the answers for the organizer and for administrators only; it is
  never in the list of rooms, in the SyncPlay info or in the logs of anybody else.
- A user who is not a member joins with `POST /SyncVote/Room/{id}/Join` and `{ "Code": "123456" }` (in the body, so that it stays
  out of the addresses that get logged; spaces and dashes are ignored). Administrators do not need it. A member does not need it
  again.
- Without being a member there are no `Candidates`, no `Results` and no votes (`403 NotMember`).
- Errors: `403 CodeRequired` (none given), `403 WrongCode`, `429 TooManyAttempts` with a `Retry-After` header and a
  `RetryAfterSeconds` property in the body.
- Guessing is limited: after 7 wrong codes (`WrongCodeAttempts` in the plugin settings, 1 to 20) the user has to wait 30 seconds,
  then 1, 2, 4… minutes up to 30, per user and room; the right code does not skip the wait. A code has a million values and
  is never one of the obvious ones (`111111`, `123456`, `121212`…), nor the code of another room open at the same time.
- The body of a join is read up to 2 KB (`413 TooLarge` beyond that); the code is the only thing in it.
- A code works at any time: somebody who joins while a round is running can vote in it at once.
- `CodeScope: "Session"` (the default): the code belongs to the SyncPlay group, every room the group opens gets the same one, and
  whoever entered it does not enter it again. The group forgets it a day after it was last used.
  `CodeScope: "Round"`: the room has a code of its own, and a new round after a finished one (also a runoff) changes it and
  leaves only the organizer in: everybody else enters the new code to vote again.

### Runoff

A runoff is a round about the titles that got at least one like in the round that just ended, so the group can settle a close
call. `POST /SyncVote/Room/{id}/StartVoting?onlyLiked=true` starts it:

- It needs a finished round with **at least two** liked titles; otherwise the answer is `400 NotEnoughLikedTitles` and nothing
  changes (the results stay as they were).
- The room then has `IsRunoff: true` and the titles in `RunoffItemIds`. `Candidates` only offers those, and a vote for any
  other title is refused (`400 UnknownItem`). The votes of the previous round are dropped, like in any new round.
- The next `StartVoting` without `onlyLiked` goes back to every title. A runoff after a runoff narrows it down again.

### CreateRoomRequest

| Property | |
|:---|:---|
| `Name` | Required |
| `SelectedCollections` | Required, at least one id: library folders and/or collections (box sets) |
| `SelectedGenres` | Optional |
| `MaxParentalRating` | Optional, a value from `/SyncVote/Library/ParentalRatings` |
| `TitleFilter` | Optional, up to 60 characters; case, accents, spaces and punctuation do not matter |
| `TimeLimit` | Minutes, 1 to 120; 0 or missing means the plugin default |
| `SortBy` | `Random`, `Title`, `CommunityRating` or `PremiereDate` |
| `ItemTypes` | `Movie` (default), `Series`, `Episode` |
| `RequireCode` | `true` to ask for a 6-digit [room code](#room-code) to vote; default `false` |
| `CodeScope` | `Session` (default, the code lasts for the whole SyncPlay session) or `Round` (a new code for each round) |
| `SyncPlayGroupId` | Ignored: the room always belongs to the SyncPlay group the server sees for the caller |

## Around the room

| Method and path | What |
|:---|:---|
| `GET /SyncVote/SyncPlayInfo` | The SyncPlay group of the caller (`GroupId` is null outside a group), its members, whether the caller organizes its room, and the state of that room (`RoomState`: `lobby`, `voting` or `results`, null when there is none) |
| `POST /SyncVote/CheckAccess` | `{ "CollectionIds": [...] }`: can the other members of the group see them? Only yes or no, never who |
| `GET /SyncVote/Permissions[?userId=]` | Whether a user can organize and vote. Another user's permissions: administrators only |
| `GET /SyncVote/Library/Collections` | Library folders and collections the caller can see, with their size |
| `GET /SyncVote/Library/Genres` | The genres in the library |
| `GET /SyncVote/Library/ParentalRatings` | Age ratings known to the server |

## Client files (public)

| Path | |
|:---|:---|
| `GET /SyncVote/Client/syncvote.js`, `…/syncvote.css` | The web client script and style |
| `GET /SyncVote/Client/i18n` | `{ Language, Available, Strings }`: the texts in the language of the server (or the plugin option), English for what is missing |

## Errors

Failures answer with a status and `{ "Error": "<code>" }`.

| Status | Codes |
|:---|:---|
| 400 | `Invalid`, `NoCollectionSelected`, `VotingNotActive`, `UnknownItem` (also a title outside the runoff), `NotEnoughLikedTitles` |
| 401 | not authenticated |
| 403 | `Forbidden`, `NotOrganizer`, `NotMember` |
| 404 | `NotFound` (also for a room the caller may not see) |
| 409 | `RoomFull`, `VotingAlreadyActive`, `GroupHasRoom` (somebody else already has a room for the group) |
