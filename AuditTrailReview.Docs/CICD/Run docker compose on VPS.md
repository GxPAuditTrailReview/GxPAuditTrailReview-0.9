# Run docker compose on VPS

This page describes how to run GxPAuditTrailReview in Docker on a Linux virtual server (VPS) for **evaluation, demos and functional testing**. These flavors are not the supported production deployment, which uses SQL Server and the SQL scripts described in `Installation Manual.md`. See ADR-0009 for the reasoning.

To compare this with the other deployment targets, and for the facts shared by all of them (worker requirement, encryption key, seed admin), see [Deployment Options](../Tech/Deployment%20Options.md).

## What you can run

| Compose file | What starts | Port | Notes |
|---|---|---|---|
| `docker/docker-compose.sqlite.yml` | WebApp with an embedded SQLite database on a Docker volume | 8080 | Smallest footprint. No Worker Service, so uploads above 50 MB stay queued |
| `docker/docker-compose.postgres.yml` | PostgreSQL 16, WebApp, Integration Hub API, Worker Service | 8080 (WebApp), 8081 (API), 5432 (Postgres) | Full flavor. Large uploads and API uploads are processed. FTP is off by default; enable it under Settings > FTP |
| `docker/docker-compose.yml` | Worker Service only, idle (sleeps, for building and inspecting the image) | none | Not a working deployment |

Each service builds from the repository root using its own Dockerfile (`AuditTrailReview.WebApp/Dockerfile`, `AuditTrailReview.IntegrationHub.API/Dockerfile`, `AuditTrailReview.IntegrationHub.WorkerService/Dockerfile`).

## 1. Prepare the server

1. Connect to the server over SSH with your own user and host: `ssh <user>@<vps-host> -p <ssh-port>`. Prefer a key over a password.
2. Check that Docker and the Compose plugin are installed: `docker --version` and `docker compose version`. Install them first if not.
3. Choose a folder for the checkout, for example `/var/apps`, and create it if needed.

## 2. Get the code

```bash
cd /var/apps
git clone <repository-url> AuditTrailReview
cd AuditTrailReview
```

If the repository is private, use a personal access token as the password when git asks. To update later: `git pull`.

## 3. Choose passwords before the first start

Both compose files contain placeholder credentials. Change them, or override them with environment variables, before exposing the server to anyone:

- `SeedAccount__Email` / `SeedAccount__Password`: the first Admin account, created once against an empty user table.
- PostgreSQL flavor: `POSTGRES_PASSWORD` and the matching password inside every `ConnectionStrings__DefaultConnection`.
- PostgreSQL flavor: `ENCRYPTION_KEY` in a `.env` file in the repository root (not inside `docker/`; the commands below pass `--project-directory .`, which makes the repository root the project folder) (`cp .env.example .env`, then fill it in -- `.env` is gitignored, never commit it). Compose reads it into `Security__EncryptionKey` on `webapp`, `worker`, `api`, and `mcp` automatically (one `.env` value feeds all four, so they're always identical, which this setting requires). It is no longer optional: besides the AI Analysis tab's "store the OpenAI key in the database" and the Email tab's "store the SMTP password in the database" features (which fail with a `500` if it's left empty, see `Technical Manual.md` section 7), `api` and `mcp` now use it to decrypt the API keys generated from `AdminPanel -> Settings -> API`/`MCP` -- left empty, every request to either service is rejected with `401` regardless of the key presented.
- PostgreSQL flavor, `api`/`mcp` services: there is no API key in `appsettings.json`/compose any more. After the first start, sign in to the webapp as the seed Admin and generate a key from `AdminPanel -> Settings -> API` (for `IntegrationHub.API`, port 8081) and/or `AdminPanel -> Settings -> MCP` (for the MCP server, port 8082) -- each tab's key pool is separate, so a key from one does not work on the other. Copy the raw value shown immediately after generating; it is never shown again.
- Optional: `OpenAI__ApiKey` (AI analysis and "Ask the docs") and `SmtpSettings__*` (the "Send Credentials" email). Both features work without them except where noted in the User Manual.

## 4. Build and start

```bash
docker compose --project-directory . -f docker/docker-compose.sqlite.yml up --build -d      # or
docker compose --project-directory . -f docker/docker-compose.postgres.yml up --build -d
```

Drop `-d` to follow the logs in the foreground. The first start builds the images and creates the database schema from the EF Core model; sign in at `http://<server>:8080` with the seed account.

## 5. Day-to-day commands

```bash
docker ps                                   # what is running
docker compose --project-directory . -f docker/docker-compose.postgres.yml logs -f worker
docker compose --project-directory . -f docker/docker-compose.postgres.yml restart webapp
docker compose --project-directory . -f docker/docker-compose.postgres.yml down        # stop and remove containers, keep data volumes
docker compose --project-directory . -f docker/docker-compose.postgres.yml down -v     # also delete the database volume (destroys all data)
```

To deploy a new version: `git pull`, then run the same `up --build -d` command again.

### Database backups (PostgreSQL flavor)

`docker/docker-compose.postgres.yml` includes a `backup` service that runs `pg_dump` every day at 03:00 (Europe/Warsaw time) and writes gzipped SQL files to `./backups/daily/` in the repository root (the project folder). Dumps older than 14 days are deleted automatically.

```bash
docker compose --project-directory . -f docker/docker-compose.postgres.yml exec backup /backup.sh     # take a backup now
ls backups/daily                                                          # list backups
gunzip -c backups/daily/<file>.sql.gz | docker compose --project-directory . -f docker/docker-compose.postgres.yml exec -T postgres psql -U gxpaudittrail -d gxpaudittrail   # restore
```

Restore into an empty database (for example after `down -v` and a fresh `up`), otherwise the dump's `CREATE`/`INSERT` statements collide with existing data. The backups sit on the same disk as the database, so copy `./backups` off the server regularly (`scp` or `rsync`).

## 6. Putting it behind a domain

The containers speak plain HTTP on 8080 (and 8081 for the API, 8082 for the MCP server -- see `AuditTrailReview.Docs/ADR/0010-mcp-server-for-qa-data-exploration.md`). Put a reverse proxy or a tunnel in front of them to get HTTPS and a domain name; see `Cloudflare.md` for the Cloudflare option. Do not publish the PostgreSQL port (5432) to the internet: the compose file publishes it only so you can connect with a local database tool, so restrict it with a firewall or remove the `ports` mapping.

**WebApp requires the proxy to send `X-Forwarded-Proto`, and must be told which proxy to trust.** The Identity/antiforgery cookies are hardened to `Secure`-only (`Cookie.SecurePolicy = Always`), so the WebApp needs `X-Forwarded-Proto: https` to know a request arrived over HTTPS even though the container only sees plain HTTP. Almost every reverse proxy (nginx, Caddy, Traefik, Mikrus's own domain proxy) sends it by default. The WebApp honours `X-Forwarded-Proto`/`X-Forwarded-For` **only from the proxies you configure**: `ForwardedHeaders:KnownProxies` (single IPs) and `ForwardedHeaders:KnownNetworks` (CIDR ranges). With nothing configured only loopback is trusted. Headers from any other sender are ignored, so a client cannot forge the IP written to the user login log or fake HTTPS. The login log records `Connection.RemoteIpAddress` only, after this middleware has run.

What address does the WebApp see the proxy at? With a proxy on the Docker host publishing the port, the connection comes from the compose network's gateway (typically in `172.16.0.0/12`); with a proxy container in the same compose network, its container address; with a tunnel such as `cloudflared` on the same host/network, its address. Check it (a login attempt's log entry shows the connecting IP when the proxy is untrusted) and set it in the compose file, e.g.:

```yaml
    environment:
      ForwardedHeaders__KnownNetworks__0: "172.16.0.0/12"   # or ForwardedHeaders__KnownProxies__0: "172.18.0.1"
```

Symptoms of getting it wrong: if Login (or any page with a form) fails with a 500 and the logs show `AntiforgeryOptions.Cookie.SecurePolicy = Always, but the current request is not an SSL request`, the proxy isn't sending `X-Forwarded-Proto: https` **or its address isn't in `KnownProxies`/`KnownNetworks`**; if the login log shows the proxy's address for every user, the same list is missing the proxy. Only list addresses you control; never `0.0.0.0/0`.

## 7. Limits to remember

- Very large files need a lot of memory; give Docker enough RAM (see *Hardware sizing* in `Installation Manual.md`). On Windows with WSL2, raise the limit in `.wslconfig`.

## 8. Example: a Mikrus VPS

A worked example of sections 1 to 6 on a small Mikrus VPS, using the PostgreSQL flavor. Replace every `<placeholder>` with your own value, and never write real hosts, ports, passwords or keys into this file.

### Connect and check Docker

```bash
ssh root@<vps-host> -p <ssh-port>
docker --version
docker compose version
cd /var
```

### Get the code

Clone from Azure DevOps with a personal access token as the password (Azure DevOps > User settings > Personal access tokens, scope Code: Read).

```bash
mkdir -p /var/AuditTrailReview && cd /var/AuditTrailReview
git clone <repository-url> .
```

To update later: `git pull`.

### Generate the encryption key

Create a random key and store it in `.env` (not inside `docker/`; the commands below pass `--project-directory .`, which makes the repository root the project folder). `.env` is gitignored; never commit it.

```bash
KEY=$(openssl rand -base64 32)
printf 'ENCRYPTION_KEY=%s\n' "$KEY" > .env
```

Keep a copy of the key somewhere safe. If it is lost, the encrypted settings and generated API keys cannot be recovered.

### Build and start

```bash
docker compose --project-directory . -f docker/docker-compose.postgres.yml build --no-cache
docker compose --project-directory . -f docker/docker-compose.postgres.yml up -d
docker ps
```

To start again from an empty database, run `docker compose --project-directory . -f docker/docker-compose.postgres.yml down -v` first. This deletes the database volume, so all data is lost.

To free disk space on a server that runs only this application, `docker system prune -a --volumes` removes all unused containers, images, networks and volumes, including data volumes of stopped containers. Do not run it if anything else you care about lives on that Docker host.

### Domains

The Mikrus domain proxy publishes the WebApp (port 8080) and the MCP server (port 8082, path `/mcp`) on separate subdomains of your Mikrus host, for example `https://<webapp-subdomain>` and `https://<mcp-subdomain>/mcp`. See section 6 for the proxy trust settings the WebApp needs.

### Copy a backup to a Windows machine

The `backup` service writes to `backups/daily/` (see *Database backups* above). From PowerShell on your own computer:

```powershell
New-Item -ItemType Directory -Force -Path C:\Backups | Out-Null
scp -P <ssh-port> root@<vps-host>:/var/AuditTrailReview/backups/daily/gxpaudittrail-latest.sql.gz C:\Backups\
```
