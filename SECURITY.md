# Security policy

## Supported versions

SyncVote is young. Only the **latest release** (the package for your Jellyfin generation, see the
[compatibility table](README.md#compatibility)) gets security fixes: update from the plugin catalog.

## Reporting a vulnerability

Please **do not open a public issue** for a security problem: it would show the problem to everybody before it is fixed.
Use GitHub's private reporting instead:

1. Open the [Security tab](https://github.com/KappuCitti/jellyfin-plugin-syncvote/security) of the repository.
2. Choose **Report a vulnerability**.

Only the maintainer can see the report. Please say:

- the Jellyfin version and the SyncVote package version (Dashboard → Plugins);
- what you did, what happened and what you expected;
- if you can, a few steps or a short script that show it.

## What to expect

This is a small project kept up in spare time, so there is no guaranteed response time. The maintainer will answer as soon as
possible, usually within a week, say whether it is a vulnerability, fix it, publish a release and, if you want, credit you.

## What counts

For example:

- seeing the code of a [room](README.md#room-code) without being its organizer or an administrator;
- joining, voting or reading the results of a room that asks for a code without knowing it, or getting around the wait after
  wrong codes;
- acting as another user, or doing what only the organizer or an administrator can do;
- reaching the API without being logged in (every endpoint except the files of the web client needs a Jellyfin login);
- anything that shows data of the Jellyfin server that the user should not see.

Things that are **not** vulnerabilities of SyncVote:

- the room code is not encryption: over plain `http` somebody who can see the network can see it, like a password (the README
  says so; use `https`);
- a problem of Jellyfin itself: report it to the Jellyfin project;
- bugs without a security effect: open a [bug report](https://github.com/KappuCitti/jellyfin-plugin-syncvote/issues/new/choose).
