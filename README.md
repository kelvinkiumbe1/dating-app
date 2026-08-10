# dating-app

A dating app in two parts: a **REST API** with real accounts, real users, and real mutual
matching, plus a **console client** that plays against a simulated deck of LLM-driven characters.

| Project | What it is |
| --- | --- |
| `DatingApp.Api` | ASP.NET Core + EF Core/SQLite backend — accounts, discovery, matching, messaging, safety |
| `ConsoleApp1` | Standalone terminal client with hardcoded characters whose replies come from Claude |
| `ConsoleMatch.Core` | Compatibility scoring and geo-distance, shared by both |
| `*.Tests` | 54 tests — 29 API integration tests over real HTTP, 25 unit tests |

## Running the API

A JWT signing key is required outside Development, so the app refuses to boot without one
rather than falling back to something insecure:

```bash
JWT__KEY="a-long-random-signing-key" dotnet run --project DatingApp.Api
curl http://localhost:5199/health          # {"status":"ok"}
```

The SQLite database and photo directory are created on first run.

## API reference

All endpoints except `/health`, `/auth/*`, and `/photos/{id}` require
`Authorization: Bearer <token>`.

### Auth

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/auth/register` | Hashes the password (PBKDF2), enforces 18+, returns a JWT |
| `POST` | `/auth/login` | Identical response for unknown email and wrong password, so accounts can't be enumerated |

### Profile

| Method | Route | Notes |
| --- | --- | --- |
| `GET` | `/me` | Your full profile and preferences |
| `PATCH` | `/me` | Update bio, interests, location, preferences, or hide yourself from discovery |
| `DELETE` | `/me` | Hard-deletes the account plus every match, message, swipe, block, and photo |
| `GET` | `/users/{id}` | Another user's public profile; 404 if either side has blocked the other |

### Discovery and matching

| Method | Route | Notes |
| --- | --- | --- |
| `GET` | `/discover` | The deck, ranked by compatibility then distance |
| `POST` | `/swipes` | Like or pass. A match is created **only** when the other person already liked you |
| `GET` | `/likes` | People who liked you and are still waiting on your answer |

Discovery filters in **both directions** — you only see people whose age, distance, and gender
preferences also accept you, so a like can never be structurally impossible to reciprocate.
Compatibility is the Jaccard index over shared interests; distance is haversine.

### Messaging

| Method | Route | Notes |
| --- | --- | --- |
| `GET` | `/matches` | Matches with last message, timestamp, and unread count |
| `GET` | `/matches/{id}/messages` | The thread; reading it marks the other side's messages read |
| `POST` | `/matches/{id}/messages` | Send a message (2000 char limit) |
| `DELETE` | `/matches/{id}` | Unmatch — the thread becomes read-only for both sides |

### Safety

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/safety/block/{id}` | Deactivates the match, hides both profiles, blocks messaging both ways |
| `DELETE` | `/safety/block/{id}` | Unblock |
| `GET` | `/safety/blocks` | Who you've blocked |
| `POST` | `/safety/report` | File a report against a user; queues for human review |

### Photos

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/me/photos` | Upload (JPEG/PNG/WebP, 5MB, max 6). Filenames are server-generated |
| `GET` | `/photos/{id}` | Serve a photo |
| `DELETE` | `/me/photos/{id}` | Delete; promotes another photo to primary |

## The console client

```bash
dotnet run --project ConsoleApp1
```

This is the original single-player app and does **not** talk to the API — it swipes against ten
hardcoded characters. Their chat replies come from Claude when a key is present:

```bash
export ANTHROPIC_API_KEY=sk-ant-...       # PowerShell: $env:ANTHROPIC_API_KEY = "sk-ant-..."
```

Without a key, or if the network drops, it falls back to canned lines rather than failing.

## Tests

```bash
dotnet test
```

API tests boot the real application against an in-memory SQLite database and exercise the actual
HTTP pipeline — routing, auth, model binding, and EF — rather than calling methods directly.

## Not built yet

Honest list of what a production dating app would still need: email/phone verification, a
moderation queue and admin tooling for the reports this API collects, push notifications and
WebSocket delivery for live messages, image moderation and EXIF stripping, rate limiting, refresh
tokens and revocation, HTTPS/deployment configuration, a real database (Postgres) instead of
SQLite, and a web or mobile front end. There is no client for the API yet — only the console
simulation.
