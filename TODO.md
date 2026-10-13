# To do

What is left, roughly in order of usefulness. Ideas are welcome as [issues](https://github.com/KappuCitti/kappucitti-plugin-syncvote/issues).

## Next

- [ ] **Real-time updates.** The client asks the server for the state of the room every 2 seconds. A WebSocket message
  (Jellyfin's session messages can carry it) would make the lobby, the end of the round and the results instant and cheaper.
- [ ] **Per-user permissions in the settings page.** Today the exceptions are edited in the configuration file
  (see the [README](README.md#settings)).
- [ ] **Series and episodes in the create form.** The server already accepts `Series` and `Episode` (a series winner is queued as
  its episodes); only the form offers movies.
- [ ] **Other Jellyfin clients.** Only the web client shows the SyncVote entry. Native apps would need their own integration.

## Later

- [ ] More languages. Add `KappuCitti.Plugin.SyncVote/Web/i18n/<code>.json` with the same keys as `en.json` (a test checks
  it) and an entry in the Language list of `Configuration/configPage.html`.
- [ ] Keep rooms across server restarts (today they live in memory on purpose: a room is a movie night).
- [ ] Choose how ties are broken. The runoff covers every liked title; a runoff only between the titles that tie for the first
  place could be an option of it.
- [ ] Jellyfin 10.9: build against its packages and test, if there is demand.

## Not planned

- Tournament brackets and other complex voting modes.
- Recommendations across libraries and external rating services.
- Jellyfin 10.8 and older: the plugin needs `IPluginServiceRegistrator`, which appeared in 10.9.

> [!NOTE]
> Done so far: voting rooms, filters (library, collection, genre, age rating, title), server-side time limit, ranking, runoff on the
> liked titles, playing the winner in SyncPlay, access checks, permissions, English and Italian, Jellyfin 10.10, 10.11 and 12
> (classic and new web interface).
