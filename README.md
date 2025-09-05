# SyncVote (Jellyfin Plugin)

Plugin open source per decidere in gruppo cosa guardare con SyncPlay, tramite votazioni stile swipe (tipo Tinder). Questo README copre obiettivi, flusso, API, sviluppo e roadmap, allineati ad AGENTS.md.

## Obiettivo

- Scegliere insieme: gli utenti in una sessione SyncPlay votano film/serie da una lista filtrata.
- Organizzazione semplice: l’organizzatore crea/aggancia la stanza, avvia la votazione e il vincitore parte in SyncPlay.
- Permessi e filtri: controlli su chi può organizzare/votare, filtri per raccolte/genere/ordinamento.

## Flusso (Schema “puro SyncPlay”)

- Crea gruppo → `POST /SyncPlay/New` (salva `groupId`).
- Gli utenti entrano → `POST /SyncPlay/Join`.
- Il plugin gestisce la votazione “swipe” su titoli filtrati (raccolta/genere/ordinamento).
- Al termine: queue/play del vincitore sul gruppo SyncPlay; gestione stato con `SyncPlay/Playstate`.

UI attesa:
- Dopo aver cliccato SyncPlay, l’organizzatore vede “Organizza una votazione”.
- Dopo l’avvio, tutti vedono “Unisciti al voto” e il pannello di swipe.

## Stato attuale (MVP API + modello dati)

- API: creazione stanza, join, start voting, vote like/dislike, results, permissions.
- Modelli: `VotingRoom`, `Vote`, `UserPermissions`.
- Config: pagina impostazioni con tempo limite, stanze pubbliche, auto-start, ordinamento default.
- Frontend: mock `Web/syncvote.js` e `Web/syncvote.css` (non ancora collegati alla UI Jellyfin).

## Cosa manca (priorità)

1) Feed contenuti + filtri
   - Query a libreria Jellyfin (raccolte/genere/sort) via `ILibraryManager`/queries.
   - API per ottenere il prossimo candidato per la stanza.
2) Allineamento SortBy
   - Uniformare enum/valori tra UI/config e capacità di sorting di Jellyfin.
3) Integrazione SyncPlay (server-side)
   - Su vincitore, queue/play nel gruppo SyncPlay corrente.
4) Controlli accesso
   - Rilevare membri senza accesso al candidato/vincitore; avvisare l’organizzatore.
5) Aggiornamenti real-time
   - WebSocket per stato stanza, voti e risultati.
6) Persistenza
   - Salvare stanze/voti/permessi oltre il runtime (file in `IApplicationPaths` o DB).
7) UI wiring
   - Target iniziale: Web Client; iniezione pulsanti e overlay di voto.

## Ordinamenti e filtri (proposta MVP)

- Ordinamenti: Random, Title A‑Z, Title Z‑A, CommunityRating, CriticRating, DateAdded, PremiereDate.
- Filtri: per libreria/collezione selezionata e/o generi; tipo contenuti iniziale Movies/Series.

## API (bozza)

- `POST /SyncVote/Room`: crea stanza.
- `GET /SyncVote/Rooms`: stanze attive.
- `GET /SyncVote/Room/{roomId}`: dettagli stanza.
- `POST /SyncVote/Room/{roomId}/Join`: join utente.
- `POST /SyncVote/Room/{roomId}/StartVoting`: avvia votazione (solo organizzatore).
- `GET /SyncVote/Room/{roomId}/NextItem`: prossimo candidato secondo filtri/sort.
- `POST /SyncVote/Vote`: like/dislike su un item.
- `GET /SyncVote/Room/{roomId}/Results`: stato e vincitore/classifica.
- `GET /SyncVote/Permissions[?userId]`: permessi utente.

Sicurezza: tutte le rotte richiedono auth Jellyfin (`[Authorize]`, header client/device/version/token).

## Modelli dati (principali)

- `VotingRoom`: id, nome, syncPlayGroupId, organizerId, membri, stato, timer, `SortBy`, filtri (raccolte/generi).
- `Vote`: id, roomId, userId, itemId, isLike, timestamp.
- `UserPermissions`: userId, `CanOrganize`, `CanVote`.

## Architettura

- `SyncVoteController`: espone API e usa `VotingRoomService`.
- `VotingRoomService`: gestisce stanze/voti, feed candidati (via `ILibraryManager`), regole e risultati.
- `Plugin`: implementa `IHasWebPages` e fornisce pagina di configurazione.
- Asset web (mock): `Web/syncvote.js` e `Web/syncvote.css`.

## Integrazione SyncPlay (server-side)

- Associare un `groupId` SyncPlay alla stanza.
- Su vincitore: enqueue/play nel gruppo; eventuale gestione pause/seek via `SyncPlay/Playstate`.

## Persistenza

- Interfaccia storage per stanze/voti/permessi.
- MVP: file JSON nel percorso dati di `IApplicationPaths` del plugin (snapshot + write‑through).

## Configurazione e permessi

- Server: tempo limite (default 5 min), ordinamento predefinito, stanze pubbliche, auto‑start.
- Per utente (plugin): “Può organizzare votazioni”, “Può votare”.
- Coerenza GUID tra plugin e manifest: mantenere lo stesso GUID in `KappuCitti.Plugin.SyncVote/Plugin.cs`, in `Configuration/configPage.html` (costante `pluginId`) e in `manifest.json`.

## Roadmap (sintesi)

- [ ] Feed contenuti + `NextItem`.
- [ ] Allineamento `SortBy` UI/Config/Backend.
- [ ] WebSocket real‑time.
- [ ] Integrazione SyncPlay (enqueue/play vincitore).
- [ ] Controlli accesso e warning organizer.
- [ ] Persistenza dati su disco.
- [ ] UI Web: pulsanti + overlay swipe.
- [ ] Test unitari/integrati di base.

## Sviluppo

- Requisiti: .NET 8 SDK, Jellyfin 10.9+ per test manuali.
- Build: `dotnet build`
- Test: `dotnet test` (da aggiungere).

## Contributi

- PR benvenute. Usa branch per feature, descrivi scenario e aggiungi test quando possibile.
- Stile: mantieni coerenza con i plugin Jellyfin, evita complessità non necessaria.

## Licenza

MIT. Vedi `LICENSE`.
