# Deploying for free

The API needs three things a free tier can provide: somewhere to run the container,
managed Postgres, and S3-compatible object storage for photos. Local disk is not an
option — every free host wipes it on restart, which is exactly why photo storage is
abstracted behind `IPhotoStorage`.

The app **refuses to start** in Production if `Jwt:Key` or `Storage:S3` is missing, rather
than silently falling back to something that loses data or signs tokens with a throwaway key.

## Recommended: Fly.io + Neon + Cloudflare R2

| Piece | Service | Free tier |
| --- | --- | --- |
| Container | Fly.io | 3 shared-cpu VMs; card required, not charged |
| Database | Neon | 0.5GB Postgres, no expiry |
| Photos | Cloudflare R2 | 10GB storage, zero egress fees |

### 1. Postgres (Neon)

Create a project at [neon.tech](https://neon.tech) and copy the connection string. Either
format works — the app converts `postgres://…` URLs to Npgsql format automatically.

### 2. Object storage (Cloudflare R2)

In the Cloudflare dashboard: **R2 → Create bucket**, then **Manage R2 API Tokens** →
create a token with **Object Read & Write**. You need the bucket name, access key ID,
secret access key, and your account ID (the endpoint is
`https://<account-id>.r2.cloudflarestorage.com`).

### 3. Deploy

`fly.toml` is already set up with the app name `kelvinkiumbe1-dating-app` and region `lhr`.
Change either before the first deploy if you want something different — `fly platform regions`
lists the options.

```bash
fly auth signup
fly apps create kelvinkiumbe1-dating-app
```

Use `fly apps create`, not `fly launch` — `launch` runs its own framework detection and will
try to rewrite the `Dockerfile` and `fly.toml` in this repo.

Set the secrets (never commit these):

```bash
fly secrets set \
  JWT__KEY="$(openssl rand -base64 48)" \
  ConnectionStrings__Default="postgresql://user:pass@host/dbname?sslmode=require" \
  Storage__S3__Bucket="dating-app-photos" \
  Storage__S3__ServiceUrl="https://<account-id>.r2.cloudflarestorage.com" \
  Storage__S3__AccessKey="<r2-access-key-id>" \
  Storage__S3__SecretKey="<r2-secret-access-key>" \
  Cors__AllowedOrigins__0="https://your-frontend.example.com"

fly deploy
curl https://<your-app>.fly.dev/health
```

Migrations run automatically at startup, so the schema is created on first boot.

### Configuration reference

Double underscores map to nested configuration keys.

| Variable | Required | Notes |
| --- | --- | --- |
| `JWT__KEY` | yes | Signing key. Startup fails without it outside Development |
| `Database__Provider` | yes | `postgres` in production, `sqlite` locally |
| `ConnectionStrings__Default` | yes | Postgres connection string or URL |
| `Storage__S3__Bucket` / `AccessKey` / `SecretKey` | yes | Startup fails without these outside Development |
| `Storage__S3__ServiceUrl` | for R2 | Omit for real AWS S3 |
| `Storage__S3__Region` | no | Defaults to `auto` (correct for R2) |
| `Cors__AllowedOrigins__0`, `__1`, … | for browsers | Explicit origins only; no wildcard, since the API is credentialed |
| `RateLimiting__AuthPerMinute` | no | Default 10 per IP on `/auth/*` |
| `RateLimiting__GlobalPerMinute` | no | Default 300 per user or IP |

## Alternative: Render

Easier signup, but two real drawbacks: free web services **spin down after 15 minutes idle**
(~30s cold start), and free Postgres is **deleted after 30 days**. Use Neon for the database
even if you host the app on Render. Render reads the `Dockerfile` automatically; set the same
environment variables in the dashboard.

## Local development

```bash
dotnet run --project DatingApp.Api
```

Development defaults to SQLite and local disk storage, so no external services are needed.

## Before real users

Free-tier hosting gives no backups, no uptime guarantee, and no data-residency control —
fine for a demo, not for strangers' real dating profiles. Still outstanding:

- Email/phone verification, and an admin surface to review the reports the API already collects
- Push notifications and WebSocket delivery for live messages
- Image moderation and EXIF stripping (uploads currently keep their metadata, including GPS)
- Refresh tokens and revocation — tokens are valid for 24 hours with no way to invalidate one
- Automated database backups
- A front end; there is no client for this API yet

`app.UseForwardedHeaders` currently trusts any proxy, which is required behind Fly/Render but
means a directly-exposed instance could have `X-Forwarded-For` spoofed to dodge per-IP limits.
Restrict `KnownProxies` if you ever run this without a proxy in front.
