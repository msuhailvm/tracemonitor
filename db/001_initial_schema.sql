-- 001_initial_schema.sql
-- Run this against your `trace_monitor` PostgreSQL database.

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- 1. Dashboard users (JWT auth)
CREATE TABLE IF NOT EXISTS users (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username        TEXT NOT NULL UNIQUE,
    password_hash   TEXT NOT NULL,
    role            TEXT NOT NULL DEFAULT 'Admin',
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- 2. Registered .NET APIs (API key auth for ingestion)
CREATE TABLE IF NOT EXISTS applications (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name            TEXT NOT NULL,
    slug            TEXT NOT NULL UNIQUE,
    environment     TEXT NOT NULL DEFAULT 'Production',
    api_key_hash    TEXT NOT NULL UNIQUE,
    api_key_prefix  TEXT NOT NULL,
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- 3. Unified log & trace events (Incoming Requests, Outgoing HTTP, ILogger Logs, Exceptions, Lifecycle)
CREATE TABLE IF NOT EXISTS log_events (
    id                  BIGSERIAL PRIMARY KEY,
    application_id      UUID NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
    timestamp           TIMESTAMPTZ NOT NULL,
    event_type          TEXT NOT NULL,          -- Request | OutgoingHttp | Log | Exception | Lifecycle
    level               TEXT NOT NULL,          -- Debug | Information | Warning | Error | Critical
    trace_id            TEXT NULL,
    span_id             TEXT NULL,
    environment         TEXT NULL,

    -- HTTP context (Incoming Request or OutgoingHttp)
    http_method         TEXT NULL,
    request_path        TEXT NULL,
    query_string        TEXT NULL,
    target_host         TEXT NULL,              -- Populated for OutgoingHttp calls
    status_code         INT NULL,
    elapsed_ms          DOUBLE PRECISION NULL,
    client_ip           TEXT NULL,
    user_agent          TEXT NULL,

    -- Log / Exception details
    category            TEXT NULL,
    message             TEXT NULL,
    exception_type      TEXT NULL,
    exception_message   TEXT NULL,
    stack_trace         TEXT NULL,

    -- Structured payloads
    request_headers     JSONB NULL,
    request_body        TEXT NULL,
    response_body       TEXT NULL,
    properties          JSONB NULL,

    -- Full-text search vector
    search_vector       TSVECTOR GENERATED ALWAYS AS (
        setweight(to_tsvector('simple', coalesce(message, '')), 'A') ||
        setweight(to_tsvector('simple', coalesce(exception_type, '') || ' ' || coalesce(exception_message, '')), 'A') ||
        setweight(to_tsvector('simple', coalesce(request_path, '') || ' ' || coalesce(target_host, '')), 'B') ||
        setweight(to_tsvector('simple', coalesce(category, '')), 'C')
    ) STORED
);

-- 4. Periodic process health heartbeats (default every 15 minutes per API)
CREATE TABLE IF NOT EXISTS app_metrics (
    id                  BIGSERIAL PRIMARY KEY,
    application_id      UUID NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
    timestamp           TIMESTAMPTZ NOT NULL,
    machine_name        TEXT NULL,
    process_id          INT NULL,
    cpu_percent         DOUBLE PRECISION NOT NULL,
    working_set_mb      DOUBLE PRECISION NOT NULL,
    gc_heap_mb          DOUBLE PRECISION NOT NULL,
    gen0_collections    INT NOT NULL DEFAULT 0,
    gen1_collections    INT NOT NULL DEFAULT 0,
    gen2_collections    INT NOT NULL DEFAULT 0,
    thread_count        INT NOT NULL DEFAULT 0,
    pending_work_items  BIGINT NOT NULL DEFAULT 0
);
