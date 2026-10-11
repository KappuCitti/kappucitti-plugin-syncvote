# Contributing

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Report a problem

Open an [issue](https://github.com/KappuCitti/kappucitti-plugin-syncvote/issues) with:

- your Jellyfin version and the SyncVote package version (Dashboard → Plugins);
- what you did, what you expected and what happened;
- the browser console (F12) and the Jellyfin server log if something failed.

## Send a change

1. Fork, then create a branch for one change.
2. Build and test it as described in the [development guide](docs/DEVELOPMENT.md): `dotnet test KappuCitti.Plugin.SyncVote.Tests`,
   and build **all** Jellyfin generations with `./build.ps1`, because a change that works for one can break another.
3. Add a test when you can: a unit test for rules, an end-to-end check for behavior ([tests/e2e](tests/e2e/README.md)).
4. Keep texts in the translation files (`Web/i18n/en.json` and `it.json` with the same keys).
5. Open the pull request and say what it changes and how you tried it.

> [!NOTE]
> Code comments, commit messages and documentation are in English.

Releases are made by the maintainer.
