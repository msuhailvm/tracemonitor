-- 002_indexes_and_search.sql
-- High-performance B-Tree, GIN Full-Text Search, and pg_trgm substring indexes.

-- Time-ordered log browsing & facet filters
CREATE INDEX IF NOT EXISTS idx_log_events_timestamp_desc
    ON log_events (timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_log_events_app_timestamp_desc
    ON log_events (application_id, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_log_events_level_timestamp_desc
    ON log_events (level, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_log_events_type_timestamp_desc
    ON log_events (event_type, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_log_events_status_timestamp_desc
    ON log_events (status_code, timestamp DESC)
    WHERE status_code IS NOT NULL;

-- Distributed trace lookup (incoming + outgoing + ILogger correlation)
CREATE INDEX IF NOT EXISTS idx_log_events_trace_id
    ON log_events (trace_id, timestamp ASC)
    WHERE trace_id IS NOT NULL;

-- Full-text search index
CREATE INDEX IF NOT EXISTS idx_log_events_search_vector_gin
    ON log_events USING GIN (search_vector);

-- Trigram indexes for fast ILIKE '%substring%' searches on paths and messages
CREATE INDEX IF NOT EXISTS idx_log_events_request_path_trgm
    ON log_events USING GIN (request_path gin_trgm_ops)
    WHERE request_path IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_log_events_message_trgm
    ON log_events USING GIN (message gin_trgm_ops)
    WHERE message IS NOT NULL;

-- Metrics lookup (latest heartbeat & Phase 2 time-series charts)
CREATE INDEX IF NOT EXISTS idx_app_metrics_app_timestamp_desc
    ON app_metrics (application_id, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_app_metrics_timestamp_desc
    ON app_metrics (timestamp DESC);
