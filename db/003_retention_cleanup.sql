-- 003_retention_cleanup.sql
-- Reusable stored function to purge logs and metrics older than N days.
-- Called automatically by TraceMonitor.Api's LogRetentionBackgroundService,
-- or manually via: SELECT * FROM purge_expired_telemetry(30);

CREATE OR REPLACE FUNCTION purge_expired_telemetry(retention_days INT DEFAULT 30)
RETURNS TABLE(deleted_logs BIGINT, deleted_metrics BIGINT)
LANGUAGE plpgsql
AS $$
DECLARE
    v_cutoff TIMESTAMPTZ := NOW() - make_interval(days => retention_days);
    v_deleted_logs BIGINT := 0;
    v_deleted_metrics BIGINT := 0;
BEGIN
    DELETE FROM log_events WHERE timestamp < v_cutoff;
    GET DIAGNOSTICS v_deleted_logs = ROW_COUNT;

    DELETE FROM app_metrics WHERE timestamp < v_cutoff;
    GET DIAGNOSTICS v_deleted_metrics = ROW_COUNT;

    RETURN QUERY SELECT v_deleted_logs, v_deleted_metrics;
END;
$$;
