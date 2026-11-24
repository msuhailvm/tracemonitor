# Trace Monitor

A lightweight, Splunk-inspired log, trace, and health monitoring platform for your **5 .NET APIs** (.NET 8 & .NET 10) hosted on **SharkASP.Net**, backed by **PostgreSQL**, with a ReactJS UI deployed on **Netlify** behind **Cloudflare**.

## Structure

- `db/` — PostgreSQL schema (`users`, `applications`, `log_events`, `app_metrics`), GIN full-text & trigram indexes, and retention cleanup function.
- `core/TraceMonitor.Client/` — Reusable `.NET 8 & .NET 10` client SDK (HTTP middleware, sensitive body/header redaction, outgoing `HttpClient` tracing with `X-Trace-Id` propagation, `ILogger` provider, lifecycle & unhandled exception hooks, 15-minute CPU/Memory heartbeats, and Gzipped async batch shipper).
- `api/TraceMonitor.Api/` — `.NET 10` ingestion, search, trace correlation, JWT/API-key auth, and automated retention API.
- `ui/` — ReactJS + TypeScript + Tailwind CSS Log Explorer & Applications Health dashboard (includes `netlify.toml` SPA redirects).

## Quick Local Start

1. **PostgreSQL (`db/`)**
   - Create a database named `trace_monitor` in **pgAdmin 4**.
   - Run `db/001_initial_schema.sql`, `db/002_indexes_and_search.sql`, and `db/003_retention_cleanup.sql`.
2. **Backend API (`api/TraceMonitor.Api/`)**
   - Update `ConnectionStrings:PostgreSQL` and `Jwt:SecretKey` in `appsettings.json`.
   - Run:
     ```powershell
     dotnet run --project api/TraceMonitor.Api
     ```
   - Default admin credentials on first boot: `admin` / `ChangeMe123!` (configured under `SeedAdmin`).
3. **Frontend UI (`ui/`)**
   - Copy `ui/.env.example` to `ui/.env` (points to `http://localhost:5080`).
   - Run:
     ```powershell
     cd ui
     npm install
     npm run dev
     ```
4. **Plug Into Your 5 Existing .NET 8 / .NET 10 APIs (`core/TraceMonitor.Client/`)**
   - Reference `TraceMonitor.Client` (via project reference, compiled DLL, or `dotnet pack` local NuGet package).
   - Add 2 lines in `Program.cs`:
     ```csharp
     builder.AddTraceMonitor(builder.Configuration.GetSection("TraceMonitor"));
     // ...
     app.UseTraceMonitor();
     ```
   - Register each API in the **Applications & Health** page to get its `tm_live_...` key and paste it into `appsettings.json`.
