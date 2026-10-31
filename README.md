# dating-app

A dating app in three parts: a **REST + realtime API** with real accounts, mutual matching,
live chat and moderation; a **web client** for it; and a standalone **console game** that
plays against a simulated deck of LLM-driven characters.

| Project | What it is |
| --- | --- |
| `DatingApp.Api` | ASP.NET Core + EF Core backend: accounts, discovery, matching, messaging, safety, moderation, SignalR |
| `web` | React + TypeScript web client (Vite), served by the API from `/app` |
| `ConsoleApp1` | Standalone terminal game with hardcoded characters whose replies come from Claude |
| `ConsoleMatch.Core` | Compatibility scoring and geo-distance, shared by the API and the console game |
| `*.Tests` | 94 tests: 69 API integration tests over real HTTP and SignalR, 25 unit tests |

## Quick start

```bash
# 1. API with demo data (SQLite + local disk, no external services)
Admin__Emails__0=alice@demo.test dotnet run --project DatingApp.Api -- --seed

# 2. Web client with hot reload, proxying API calls to :5199
cd web && npm install && npm run dev
```

Open http://localhost:5173/app/ and sign in as **alice@demo.test** or **sam@demo.test**
(password `demo-password`). About half the seeded profiles have already liked each demo
account, so liking them back in **Likes** gives instant matches. Sign into the other account
in a private window to watch messages, read receipts and typing indicators arrive live.
Alice is a moderator in the command above, so she also gets the **Moderate** tab.

`--seed` only runs in Development and does nothing if the demo users already exist. Delete
`DatingApp.Api/datingapp.db` to start fresh.

To try the production layout locally, build the client into the API and use one server:

```bash
cd web && npm run build && cd ..        # writes DatingApp.Api/wwwroot/app
dotnet run --project DatingApp.Api      # http://localhost:5199/ → /app/
```

## The web client

Sign up (a 3-step flow with an 18+ check and location from the browser or a city list),
swipe on a ranked deck (drag, buttons, or ← →), see who already liked you, chat in real
time with read receipts and typing indicators, and manage photos, preferences, password,
sessions, blocks and account deletion. Moderators get a report queue with dismiss and ban.

It lives under `/app` so that refreshing a client route like `/app/likes` can never land on
the API's own `/likes`. In production the Dockerfile builds it into the API image, so one
container serves both and no CORS setup is needed.

## Running the API

```bash
dotnet run --project DatingApp.Api
curl http://localhost:5199/health          # Healthy
```

Development defaults to SQLite and local disk, so no external services are needed. The
database and photo directory are created on first run.

Production uses Postgres and S3-compatible object storage. The app **refuses to boot**
without a JWT signing key or object storage, rather than signing tokens with a throwaway
key or writing photos to a disk that gets wiped. See **[DEPLOY.md](DEPLOY.md)** for free-tier
deployment (Fly.io + Neon + Cloudflare R2) and the full configuration reference.

## API reference

All endpoints except `/health`, `/auth/register`, `/auth/login`, `/auth/refresh`,
`/auth/logout` and `/photos/{id}` require `Authorization: Bearer <token>`.

### Auth and sessions

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/auth/register` | Hashes the password (PBKDF2), enforces 18+, returns an access token and a refresh token |
| `POST` | `/auth/login` | Identical response for unknown email and wrong password, so accounts can't be enumerated |
| `POST` | `/auth/refresh` | Exchanges a refresh token for a new pair. The old one is rotated out |
| `POST` | `/auth/logout` | Revokes that device's refresh token |
| `POST` | `/auth/logout-all` | Signs out every device, **including access tokens already issued** |
| `POST` | `/auth/change-password` | Requires the current password; signs out other devices, returns fresh tokens for this one |

Access tokens last 15 minutes and refresh tokens 30 days. Only a SHA-256 of each refresh
token is stored. If a rotated token is presented again more than 30 seconds later, the
server assumes it was stolen and revokes the whole chain. Inside that window it just
returns a 401, because two browser tabs refreshing at once isn't an attack.

Every authenticated request checks that the account still exists, isn't banned, and hasn't
revoked its tokens (a `TokenVersion` claim), so sign-out-everywhere, password changes,
bans and account deletion all take effect on the next request rather than at token expiry.

### Profile

| Method | Route | Notes |
| --- | --- | --- |
| `GET` | `/me` | Your full profile and preferences, including `isAdmin` |
| `PATCH` | `/me` | Update bio, interests, location, preferences, or hide yourself from discovery |
| `DELETE` | `/me` | Hard-deletes the account plus every match, message, swipe, block, session and photo (including the stored image bytes) |
| `GET` | `/users/{id}` | Another user's public profile; 404 if either side has blocked the other or they are banned |

### Discovery and matching

| Method | Route | Notes |
| --- | --- | --- |
| `GET` | `/discover` | The deck, ranked by compatibility then distance |
| `POST` | `/swipes` | Like or pass. A match is created **only** when the other person already liked you |
| `GET` | `/likes` | People who liked you and are still waiting on your answer |

Discovery filters in **both directions**: you only see people whose age, distance, and gender
preferences also accept you, so a like can never be structurally impossible to reciprocate.
Compatibility is the Jaccard index over shared interests; distance is haversine.

### Messaging

| Method | Route | Notes |
| --- | --- | --- |
| `GET` | `/matches` | Matches with last message, timestamp, and unread count |
| `GET` | `/matches/{id}/messages` | The thread; reading it marks the other side's messages read |
| `POST` | `/matches/{id}/messages` | Send a message (2000 char limit) |
| `DELETE` | `/matches/{id}` | Unmatch: the thread becomes read-only for both sides |

### Realtime: `/hubs/chat` (SignalR)

Connect with the access token (`?access_token=` for WebSockets, which is what the
`@microsoft/signalr` client's `accessTokenFactory` does). The hub only delivers what the REST
endpoints already did, so a client that misses an event loses nothing by reloading.

| Event | Sent to | When |
| --- | --- | --- |
| `message` | both participants | A message is sent |
| `read` | the sender | The other side opens the thread |
| `match` | the person who liked first | The like is returned |
| `unmatched` | both participants | Unmatch, block, ban, or account deletion |
| `typing` | the other participant | Client calls the hub's `Typing(matchId)` method |

The server closes a connection when its access token expires, so a revoked session loses its
live feed within one token lifetime. The client reconnects with a fresh token.

### Safety

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/safety/block/{id}` | Deactivates the match, hides both profiles, blocks messaging both ways |
| `DELETE` | `/safety/block/{id}` | Unblock |
| `GET` | `/safety/blocks` | Who you've blocked |
| `POST` | `/safety/report` | File a report against a user; queues for human review |

### Moderation

Moderators are listed in configuration (`Admin__Emails__0`, `Admin__Emails__1`, …) and synced
at startup. There is no API to grant admin, so no endpoint can be abused to escalate.

| Method | Route | Notes |
| --- | --- | --- |
| `GET` | `/admin/reports` | Open reports, oldest first, with reporter, reported profile, and report counts. `?status=all` includes resolved |
| `POST` | `/admin/reports/{id}/resolve` | `{"action": "dismiss" \| "ban", "note": "…"}` |
| `POST` | `/admin/users/{id}/ban` | Bans directly |
| `POST` | `/admin/users/{id}/unban` | Lifts a ban (matches are not restored) |
| `GET` | `/admin/users/{id}` | Account summary with report counts |

A ban takes effect immediately. It blocks sign-in and refresh, invalidates live access
tokens, ends every match, hides the profile, and closes all open reports against the
account. Admins can't be banned, so moderation can't lock itself out.

### Photos

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/me/photos` | Upload (JPEG/PNG/WebP, 5MB, max 6). Magic bytes must match the declared type; storage keys are server-generated |
| `GET` | `/photos/{id}` | Serve a photo |
| `DELETE` | `/me/photos/{id}` | Delete; promotes another photo to primary |

Uploads are stripped of metadata before storage: EXIF (GPS, camera serials, timestamps),
XMP, IPTC, comments, PNG text chunks, and anything appended after the image. Pixel data is
not re-encoded. The EXIF orientation is re-emitted on its own, so portrait phone photos
don't display sideways.

## The console game

```bash
dotnet run --project ConsoleApp1
```

This is the original single-player app. It does **not** talk to the API; it swipes against
ten hardcoded characters. Their chat replies come from Claude when a key is present:

```bash
export ANTHROPIC_API_KEY=sk-ant-...       # PowerShell: $env:ANTHROPIC_API_KEY = "sk-ant-..."
```

Without a key, or if the network drops, it falls back to canned lines rather than failing.

## Tests

```bash
dotnet test                     # API + console tests
cd web && npm run build         # type-checks the web client, then bundles it
```

API tests boot the real application against a throwaway SQLite database and exercise the
actual HTTP pipeline (routing, auth, model binding, EF) plus the SignalR hub, rather than
calling methods directly. CI (`.github/workflows/ci.yml`) runs both, plus a Docker build.

## Not built yet

Still missing before real users: email/phone verification; push notifications for when the
app is closed (live delivery only works while a tab is open); image moderation (nudity and
CSAM scanning, e.g. via a vendor API, before photos go live); automated database backups;
and native mobile apps. The web client stores its refresh token in `localStorage`, which is
readable by any script that runs on the page. The strict Content-Security-Policy limits that
risk, but an `HttpOnly` cookie would be stronger.
