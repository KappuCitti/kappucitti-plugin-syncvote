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
| `POST /SyncVote/Room/{id}/Join` | A member of the room's SyncPlay group | Join. Joining twice is fine |
| `POST /SyncVote/Room/{id}/StartVoting[?onlyLiked=true]` | The organizer | Start a round (a new round drops the previous votes). With `onlyLiked=true` it is a [runoff](#runoff) |
| `POST /SyncVote/Room/{id}/EndVoting` | The organizer | End the round now |
| `DELETE /SyncVote/Room/{id}` | The organizer or an administrator | Close the room |
| `GET /SyncVote/Room/{id}/Candidates?skip=0&limit=20` | A member | The titles the caller has not voted on yet |
| `POST /SyncVote/Vote` | A member | `{ "RoomId": "…", "ItemId": "…", "IsLike": true }`. Voting again on a title replaces the vote |
| `GET /SyncVote/Room/{id}/Results` | Anyone who may see it | The ranking, the winner and how many members cannot access the winner |
| `POST /SyncVote/Room/{id}/PlayWinner` | The organizer | Queue and play the winner in the SyncPlay group |

A room has `Id`, `Name`, `SyncPlayGroupId`, `OrganizerId`, `MemberIds`, `IsVotingActive`, `IsVotingFinished`, `TimeLimit`
(minutes), `SecondsRemaining` (only while voting), `SortBy`, `SelectedCollections`, `SelectedGenres`, `MaxParentalRating`,
`TitleFilter`, `ItemTypes`, `IsRunoff`, `RunoffItemIds`, `CreatedAt` and `VotingStartedAt`.

The ranking orders titles by likes, then by fewer dislikes, then by who was liked first.

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
| `SyncPlayGroupId` | Ignored: the room always belongs to the SyncPlay group the server sees for the caller |

## Around the room

| Method and path | What |
|:---|:---|
| `GET /SyncVote/SyncPlayInfo` | The SyncPlay group of the caller (`GroupId` is null outside a group), its members, and whether the caller organizes its room |
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
