# Trace Monitor — PostgreSQL Setup (`db/`)

## Quick Setup in pgAdmin 4

1. Open **pgAdmin 4** and connect to your local PostgreSQL server.
2. Right-click **Databases** → **Create** → **Database...** and name it `trace_monitor`.
3. Select `trace_monitor`, open the **Query Tool** (`Alt+Shift+Q`), and run the scripts in order:
   1. `001_initial_schema.sql` — Creates extensions (`pgcrypto`, `pg_trgm`) and tables (`users`, `applications`, `log_events`, `app_metrics`).
   2. `002_indexes_and_search.sql` — Creates B-Tree, GIN Full-Text Search, and trigram indexes.
   3. `003_retention_cleanup.sql` — Creates the `purge_expired_telemetry(retention_days)` cleanup function.

> **Admin User:** On first startup, `TraceMonitor.Api` automatically creates the initial admin user configured under `SeedAdmin` in `appsettings.json` (if the `users` table is empty) using ASP.NET Core's PBKDF2 password hasher.

## Manual Retention Cleanup
```sql
SELECT * FROM purge_expired_telemetry(30);
```
