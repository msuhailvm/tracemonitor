using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using TraceMonitor.Api.Models;
using TraceMonitor.Api.Services;

namespace TraceMonitor.Api.Data;

public sealed class TraceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DbConnectionFactory _db;

    public TraceRepository(DbConnectionFactory db)
    {
        _db = db;
    }

    // ─── Users & Auth ──────────────────────────────────────────────

    public async Task<UserRow?> GetUserByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        const string sql = """
            SELECT id AS Id, username AS Username, password_hash AS PasswordHash,
                   role AS Role, is_active AS IsActive, created_at AS CreatedAt
            FROM users
            WHERE LOWER(username) = LOWER(@Username)
            LIMIT 1;
            """;
        return await conn.QuerySingleOrDefaultAsync<UserRow>(sql, new { Username = username });
    }

    public async Task EnsureSeedAdminAsync(string username, string passwordHash, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        const string countSql = "SELECT COUNT(*) FROM users;";
        var count = await conn.ExecuteScalarAsync<long>(countSql);
        if (count > 0)
        {
            return;
        }

        const string insertSql = """
            INSERT INTO users (username, password_hash, role, is_active)
            VALUES (@Username, @PasswordHash, 'Admin', TRUE)
            ON CONFLICT (username) DO NOTHING;
            """;
        await conn.ExecuteAsync(insertSql, new { Username = username, PasswordHash = passwordHash });
    }

    // ─── Applications & Health ─────────────────────────────────────

    public async Task<Guid?> FindActiveApplicationIdByKeyHashAsync(string keyHash, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        const string sql = """
            SELECT id FROM applications
            WHERE api_key_hash = @KeyHash AND is_active = TRUE
            LIMIT 1;
            """;
        return await conn.ExecuteScalarAsync<Guid?>(sql, new { KeyHash = keyHash });
    }

    public async Task<IReadOnlyList<ApplicationDto>> GetApplicationsAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        const string sql = """
            SELECT id AS Id, name AS Name, slug AS Slug, environment AS Environment,
                   api_key_prefix AS ApiKeyPrefix, is_active AS IsActive, created_at AS CreatedAt
            FROM applications
            ORDER BY name ASC;
            """;
        var rows = await conn.QueryAsync<ApplicationDto>(sql);
        return rows.ToList();
    }

    public async Task<ApplicationDto> CreateApplicationAsync(
        string name,
        string slug,
        string environment,
        string apiKeyHash,
        string apiKeyPrefix,
        CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        const string sql = """
            INSERT INTO applications (name, slug, environment, api_key_hash, api_key_prefix)
            VALUES (@Name, @Slug, @Environment, @ApiKeyHash, @ApiKeyPrefix)
            RETURNING id AS Id, name AS Name, slug AS Slug, environment AS Environment,
                      api_key_prefix AS ApiKeyPrefix, is_active AS IsActive, created_at AS CreatedAt;
            """;
        return await conn.QuerySingleAsync<ApplicationDto>(sql, new
        {
            Name = name,
            Slug = slug,
            Environment = environment,
            ApiKeyHash = apiKeyHash,
            ApiKeyPrefix = apiKeyPrefix
        });
    }

    public async Task<(ApplicationDto? App, string? OldKeyHash)> RotateApiKeyAsync(
        Guid id,
        string newKeyHash,
        string newKeyPrefix,
        CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        const string findSql = "SELECT api_key_hash FROM applications WHERE id = @Id;";
        var oldHash = await conn.ExecuteScalarAsync<string?>(findSql, new { Id = id });
        if (oldHash is null)
        {
            return (null, null);
        }

        const string updateSql = """
            UPDATE applications
            SET api_key_hash = @NewKeyHash,
                api_key_prefix = @NewKeyPrefix,
                updated_at = NOW()
            WHERE id = @Id
            RETURNING id AS Id, name AS Name, slug AS Slug, environment AS Environment,
                      api_key_prefix AS ApiKeyPrefix, is_active AS IsActive, created_at AS CreatedAt;
            """;
        var updated = await conn.QuerySingleAsync<ApplicationDto>(updateSql, new
        {
            Id = id,
            NewKeyHash = newKeyHash,
            NewKeyPrefix = newKeyPrefix
        });

        return (updated, oldHash);
    }

    public async Task<IReadOnlyList<ApplicationHealthDto>> GetApplicationsHealthAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        const string sql = """
            SELECT
                a.id AS Id,
                a.name AS Name,
                a.slug AS Slug,
                a.environment AS Environment,
                a.api_key_prefix AS ApiKeyPrefix,
                a.is_active AS IsActive,
                m.timestamp AS LastHeartbeatAt,
                m.cpu_percent AS CpuPercent,
                m.working_set_mb AS WorkingSetMb,
                m.gc_heap_mb AS GcHeapMb,
                m.thread_count AS ThreadCount,
                m.machine_name AS MachineName,
                m.process_id AS ProcessId
            FROM applications a
            LEFT JOIN LATERAL (
                SELECT timestamp, cpu_percent, working_set_mb, gc_heap_mb, thread_count, machine_name, process_id
                FROM app_metrics
                WHERE application_id = a.id
                ORDER BY timestamp DESC
                LIMIT 1
            ) m ON TRUE
            ORDER BY a.name ASC;
            """;

        var rows = (await conn.QueryAsync<ApplicationHealthDto>(sql)).ToList();
        var now = DateTimeOffset.UtcNow;

        // With 15-minute heartbeats, mark Online if seen within 25 minutes, otherwise Stale
        foreach (var row in rows)
        {
            if (row.LastHeartbeatAt is null)
            {
                row.Status = "Unknown";
            }
            else if ((now - row.LastHeartbeatAt.Value) <= TimeSpan.FromMinutes(25))
            {
                row.Status = "Online";
            }
            else
            {
                row.Status = "Stale";
            }
        }

        return rows;
    }

    // ─── Ingestion ─────────────────────────────────────────────────

    public async Task<int> InsertLogBatchAsync(
        Guid applicationId,
        IReadOnlyList<LogEventIngestDto> events,
        CancellationToken ct = default)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string sql = """
            INSERT INTO log_events (
                application_id, timestamp, event_type, level, trace_id, span_id, environment,
                http_method, request_path, query_string, target_host, status_code, elapsed_ms,
                client_ip, user_agent, category, message, exception_type, exception_message,
                stack_trace, request_headers, request_body, response_body, properties
            ) VALUES (
                @application_id, @timestamp, @event_type, @level, @trace_id, @span_id, @environment,
                @http_method, @request_path, @query_string, @target_host, @status_code, @elapsed_ms,
                @client_ip, @user_agent, @category, @message, @exception_type, @exception_message,
                @stack_trace, @request_headers, @request_body, @response_body, @properties
            );
            """;

        await using var batch = new NpgsqlBatch(conn, tx);
        foreach (var e in events)
        {
            var cmd = new NpgsqlBatchCommand(sql);
            cmd.Parameters.AddWithValue("application_id", applicationId);
            cmd.Parameters.AddWithValue("timestamp", (e.Timestamp ?? DateTimeOffset.UtcNow).UtcDateTime);
            cmd.Parameters.AddWithValue("event_type", string.IsNullOrWhiteSpace(e.EventType) ? "Log" : e.EventType);
            cmd.Parameters.AddWithValue("level", string.IsNullOrWhiteSpace(e.Level) ? "Information" : e.Level);
            cmd.Parameters.AddWithValue("trace_id", (object?)e.TraceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("span_id", (object?)e.SpanId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("environment", (object?)e.Environment ?? DBNull.Value);
            cmd.Parameters.AddWithValue("http_method", (object?)e.HttpMethod ?? DBNull.Value);
            cmd.Parameters.AddWithValue("request_path", (object?)e.RequestPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("query_string", (object?)e.QueryString ?? DBNull.Value);
            cmd.Parameters.AddWithValue("target_host", (object?)e.TargetHost ?? DBNull.Value);
            cmd.Parameters.AddWithValue("status_code", (object?)e.StatusCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("elapsed_ms", (object?)e.ElapsedMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("client_ip", (object?)e.ClientIp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("user_agent", (object?)e.UserAgent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("category", (object?)e.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("message", (object?)e.Message ?? DBNull.Value);
            cmd.Parameters.AddWithValue("exception_type", (object?)e.ExceptionType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("exception_message", (object?)e.ExceptionMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("stack_trace", (object?)e.StackTrace ?? DBNull.Value);

            var headersParam = new NpgsqlParameter("request_headers", NpgsqlDbType.Jsonb)
            {
                Value = e.RequestHeaders is { Count: > 0 }
                    ? JsonSerializer.Serialize(e.RequestHeaders, JsonOptions)
                    : DBNull.Value
            };
            cmd.Parameters.Add(headersParam);

            cmd.Parameters.AddWithValue("request_body", (object?)e.RequestBody ?? DBNull.Value);
            cmd.Parameters.AddWithValue("response_body", (object?)e.ResponseBody ?? DBNull.Value);

            var propsParam = new NpgsqlParameter("properties", NpgsqlDbType.Jsonb)
            {
                Value = e.Properties is { Count: > 0 }
                    ? JsonSerializer.Serialize(e.Properties, JsonOptions)
                    : DBNull.Value
            };
            cmd.Parameters.Add(propsParam);

            batch.BatchCommands.Add(cmd);
        }

        await batch.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return events.Count;
    }

    public async Task InsertMetricAsync(
        Guid applicationId,
        MetricSnapshotIngestDto metric,
        CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        const string sql = """
            INSERT INTO app_metrics (
                application_id, timestamp, machine_name, process_id,
                cpu_percent, working_set_mb, gc_heap_mb,
                gen0_collections, gen1_collections, gen2_collections,
                thread_count, pending_work_items
            ) VALUES (
                @ApplicationId, @Timestamp, @MachineName, @ProcessId,
                @CpuPercent, @WorkingSetMb, @GcHeapMb,
                @Gen0Collections, @Gen1Collections, @Gen2Collections,
                @ThreadCount, @PendingWorkItems
            );
            """;

        await conn.ExecuteAsync(sql, new
        {
            ApplicationId = applicationId,
            Timestamp = (metric.Timestamp ?? DateTimeOffset.UtcNow).UtcDateTime,
            metric.MachineName,
            metric.ProcessId,
            metric.CpuPercent,
            metric.WorkingSetMb,
            metric.GcHeapMb,
            metric.Gen0Collections,
            metric.Gen1Collections,
            metric.Gen2Collections,
            metric.ThreadCount,
            metric.PendingWorkItems
        });
    }

    // ─── Search & Trace Queries ────────────────────────────────────

    private static (string WhereSql, DynamicParameters Params) BuildLogWhereClause(LogSearchRequest req)
    {
        var parsed = SearchQueryParser.Parse(req.Q);
        var where = new StringBuilder("WHERE 1=1");
        var p = new DynamicParameters();

        if (req.From.HasValue)
        {
            where.Append(" AND l.timestamp >= @From");
            p.Add("From", req.From.Value.UtcDateTime);
        }

        if (req.To.HasValue)
        {
            where.Append(" AND l.timestamp <= @To");
            p.Add("To", req.To.Value.UtcDateTime);
        }

        if (req.ApplicationIds is { Length: > 0 })
        {
            where.Append(" AND l.application_id = ANY(@ApplicationIds)");
            p.Add("ApplicationIds", req.ApplicationIds);
        }

        if (!string.IsNullOrWhiteSpace(parsed.AppSlug))
        {
            where.Append(" AND LOWER(a.slug) = LOWER(@AppSlug)");
            p.Add("AppSlug", parsed.AppSlug);
        }

        var levels = req.Levels?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
        if (!string.IsNullOrWhiteSpace(parsed.Level))
        {
            levels.Add(parsed.Level);
        }
        if (levels.Count > 0)
        {
            where.Append(" AND l.level = ANY(@Levels)");
            p.Add("Levels", levels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        var eventTypes = req.EventTypes?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
        if (!string.IsNullOrWhiteSpace(parsed.EventType))
        {
            eventTypes.Add(parsed.EventType);
        }
        if (eventTypes.Count > 0)
        {
            where.Append(" AND l.event_type = ANY(@EventTypes)");
            p.Add("EventTypes", eventTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        if (!string.IsNullOrWhiteSpace(parsed.HttpMethod))
        {
            where.Append(" AND UPPER(l.http_method) = @HttpMethod");
            p.Add("HttpMethod", parsed.HttpMethod);
        }

        var statusFilter = !string.IsNullOrWhiteSpace(req.StatusRange) ? req.StatusRange.Trim().ToLowerInvariant() : parsed.StatusFilter;
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            if (statusFilter == "2xx")
            {
                where.Append(" AND l.status_code BETWEEN 200 AND 299");
            }
            else if (statusFilter == "3xx")
            {
                where.Append(" AND l.status_code BETWEEN 300 AND 399");
            }
            else if (statusFilter == "4xx")
            {
                where.Append(" AND l.status_code BETWEEN 400 AND 499");
            }
            else if (statusFilter == "5xx")
            {
                where.Append(" AND l.status_code >= 500");
            }
            else if (int.TryParse(statusFilter, out var exactCode))
            {
                where.Append(" AND l.status_code = @ExactStatusCode");
                p.Add("ExactStatusCode", exactCode);
            }
        }

        var traceId = !string.IsNullOrWhiteSpace(req.TraceId) ? req.TraceId.Trim() : parsed.TraceId;
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            where.Append(" AND l.trace_id = @TraceId");
            p.Add("TraceId", traceId);
        }

        var minMs = req.MinElapsedMs ?? parsed.MinElapsedMs;
        if (minMs.HasValue)
        {
            where.Append(" AND l.elapsed_ms >= @MinElapsedMs");
            p.Add("MinElapsedMs", minMs.Value);
        }

        var freeText = parsed.CombinedFreeText;
        if (!string.IsNullOrWhiteSpace(freeText))
        {
            where.Append("""
                 AND (
                    l.search_vector @@ websearch_to_tsquery('simple', @FreeText)
                    OR l.request_path ILIKE @LikeText
                    OR l.query_string ILIKE @LikeText
                    OR l.message ILIKE @LikeText
                    OR l.exception_message ILIKE @LikeText
                    OR l.trace_id ILIKE @LikeText
                )
                """);
            p.Add("FreeText", freeText);
            p.Add("LikeText", $"%{freeText}%");
        }

        return (where.ToString(), p);
    }

    public async Task<LogSearchResponse> SearchLogsAsync(LogSearchRequest req, CancellationToken ct = default)
    {
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 10, 200);
        var offset = (page - 1) * pageSize;

        var (where, p) = BuildLogWhereClause(req);

        p.Add("Limit", pageSize);
        p.Add("Offset", offset);

        var countSql = $"""
            SELECT COUNT(*)
            FROM log_events l
            INNER JOIN applications a ON a.id = l.application_id
            {where};
            """;

        var dataSql = $"""
            SELECT
                l.id AS Id,
                l.application_id AS ApplicationId,
                a.name AS ApplicationName,
                a.slug AS ApplicationSlug,
                l.timestamp AS Timestamp,
                l.event_type AS EventType,
                l.level AS Level,
                l.trace_id AS TraceId,
                l.environment AS Environment,
                l.http_method AS HttpMethod,
                l.request_path AS RequestPath,
                l.query_string AS QueryString,
                l.target_host AS TargetHost,
                l.status_code AS StatusCode,
                l.elapsed_ms AS ElapsedMs,
                l.category AS Category,
                l.message AS Message,
                l.exception_type AS ExceptionType
            FROM log_events l
            INNER JOIN applications a ON a.id = l.application_id
            {where}
            ORDER BY l.timestamp DESC, l.id DESC
            LIMIT @Limit OFFSET @Offset;
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>(countSql, p);
        var items = (await conn.QueryAsync<LogSummaryDto>(dataSql, p)).ToList();

        return new LogSearchResponse
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<LogDetailDto?> GetLogByIdAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        const string sql = """
            SELECT
                l.id AS Id,
                l.application_id AS ApplicationId,
                a.name AS ApplicationName,
                a.slug AS ApplicationSlug,
                l.timestamp AS Timestamp,
                l.event_type AS EventType,
                l.level AS Level,
                l.trace_id AS TraceId,
                l.span_id AS SpanId,
                l.environment AS Environment,
                l.http_method AS HttpMethod,
                l.request_path AS RequestPath,
                l.query_string AS QueryString,
                l.target_host AS TargetHost,
                l.status_code AS StatusCode,
                l.elapsed_ms AS ElapsedMs,
                l.client_ip AS ClientIp,
                l.user_agent AS UserAgent,
                l.category AS Category,
                l.message AS Message,
                l.exception_type AS ExceptionType,
                l.exception_message AS ExceptionMessage,
                l.stack_trace AS StackTrace,
                l.request_headers::text AS RequestHeadersJson,
                l.request_body AS RequestBody,
                l.response_body AS ResponseBody,
                l.properties::text AS PropertiesJson
            FROM log_events l
            INNER JOIN applications a ON a.id = l.application_id
            WHERE l.id = @Id
            LIMIT 1;
            """;
        return await conn.QuerySingleOrDefaultAsync<LogDetailDto>(sql, new { Id = id });
    }

    public async Task<IReadOnlyList<LogSummaryDto>> GetLogsByTraceIdAsync(string traceId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        const string sql = """
            SELECT
                l.id AS Id,
                l.application_id AS ApplicationId,
                a.name AS ApplicationName,
                a.slug AS ApplicationSlug,
                l.timestamp AS Timestamp,
                l.event_type AS EventType,
                l.level AS Level,
                l.trace_id AS TraceId,
                l.environment AS Environment,
                l.http_method AS HttpMethod,
                l.request_path AS RequestPath,
                l.query_string AS QueryString,
                l.target_host AS TargetHost,
                l.status_code AS StatusCode,
                l.elapsed_ms AS ElapsedMs,
                l.category AS Category,
                l.message AS Message,
                l.exception_type AS ExceptionType
            FROM log_events l
            INNER JOIN applications a ON a.id = l.application_id
            WHERE l.trace_id = @TraceId
            ORDER BY l.timestamp ASC, l.id ASC
            LIMIT 250;
            """;
        var rows = await conn.QueryAsync<LogSummaryDto>(sql, new { TraceId = traceId });
        return rows.ToList();
    }

    // ─── Analytics Queries (Phase 2) ───────────────────────────────

    public async Task<VolumeAnalyticsResponse> GetVolumeAnalyticsAsync(
        LogSearchRequest req,
        string? groupBy = null,
        string? interval = null,
        CancellationToken ct = default)
    {
        var resolvedGroupBy = string.Equals(groupBy, "app", StringComparison.OrdinalIgnoreCase) ? "app" : "level";
        var groupColumn = resolvedGroupBy == "app" ? "a.name" : "l.level";

        var (where, p) = BuildLogWhereClause(req);
        await using var conn = await _db.OpenConnectionAsync(ct);

        DateTimeOffset resolvedFrom;
        DateTimeOffset resolvedTo;
        if (req.From.HasValue)
        {
            resolvedFrom = req.From.Value;
            resolvedTo = req.To ?? DateTimeOffset.UtcNow;
        }
        else
        {
            var boundsSql = $"""
                SELECT MIN(l.timestamp) AS MinTs, MAX(l.timestamp) AS MaxTs
                FROM log_events l
                INNER JOIN applications a ON a.id = l.application_id
                {where};
                """;
            var bounds = await conn.QuerySingleAsync<(DateTimeOffset? MinTs, DateTimeOffset? MaxTs)>(boundsSql, p);
            resolvedTo = req.To ?? bounds.MaxTs ?? DateTimeOffset.UtcNow;
            resolvedFrom = bounds.MinTs ?? resolvedTo.AddHours(-24);
        }

        if (resolvedFrom > resolvedTo)
        {
            (resolvedFrom, resolvedTo) = (resolvedTo, resolvedFrom);
        }

        var bucketSeconds = AnalyticsBucketHelper.ResolveBucketSeconds(resolvedFrom, resolvedTo, interval);
        var bucketExpr = AnalyticsBucketHelper.BuildBucketExpression("l.timestamp", bucketSeconds);

        var sql = $"""
            SELECT
                {bucketExpr} AS Bucket,
                {groupColumn} AS GroupKey,
                COUNT(*) AS Count
            FROM log_events l
            INNER JOIN applications a ON a.id = l.application_id
            {where}
            GROUP BY 1, 2
            ORDER BY 1 ASC;
            """;

        var rows = (await conn.QueryAsync<VolumeRow>(sql, p)).ToList();

        var bucketMap = new Dictionary<DateTimeOffset, VolumeBucketDto>();

        // Pre-populate zero buckets for a continuous time axis when span is reasonable
        var startBucket = AnalyticsBucketHelper.FloorToBucket(resolvedFrom, bucketSeconds);
        var endBucket = AnalyticsBucketHelper.FloorToBucket(resolvedTo, bucketSeconds);
        var estimatedBuckets = (long)((endBucket - startBucket).TotalSeconds / bucketSeconds) + 1;
        if (estimatedBuckets is > 0 and <= 180)
        {
            for (var ts = startBucket; ts <= endBucket; ts = ts.AddSeconds(bucketSeconds))
            {
                bucketMap[ts] = new VolumeBucketDto { Timestamp = ts };
            }
        }

        long totalEvents = 0;
        foreach (var row in rows)
        {
            var key = AnalyticsBucketHelper.FloorToBucket(row.Bucket.ToUniversalTime(), bucketSeconds);
            if (!bucketMap.TryGetValue(key, out var dto))
            {
                dto = new VolumeBucketDto { Timestamp = key };
                bucketMap[key] = dto;
            }

            var groupKey = string.IsNullOrWhiteSpace(row.GroupKey) ? "Unknown" : row.GroupKey;
            dto.Counts[groupKey] = dto.Counts.GetValueOrDefault(groupKey) + row.Count;
            dto.Total += row.Count;
            totalEvents += row.Count;
        }

        return new VolumeAnalyticsResponse
        {
            BucketSeconds = bucketSeconds,
            From = resolvedFrom,
            To = resolvedTo,
            GroupBy = resolvedGroupBy,
            TotalEvents = totalEvents,
            Buckets = bucketMap.Values.OrderBy(b => b.Timestamp).ToList()
        };
    }

    public async Task<ErrorRateAnalyticsResponse> GetErrorRateAnalyticsAsync(
        Guid[]? applicationIds,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? interval = null,
        CancellationToken ct = default)
    {
        var (resolvedFrom, resolvedTo) = AnalyticsBucketHelper.ResolveTimeWindow(from, to, TimeSpan.FromHours(24));
        var bucketSeconds = AnalyticsBucketHelper.ResolveBucketSeconds(resolvedFrom, resolvedTo, interval);
        var bucketExpr = AnalyticsBucketHelper.BuildBucketExpression("l.timestamp", bucketSeconds);

        var (where, p) = BuildAnalyticsWhereClause(resolvedFrom, resolvedTo, applicationIds, "l");

        const string isRequestSql = "(l.event_type = 'Request' OR (l.event_type = 'Exception' AND l.http_method IS NOT NULL))";
        const string isErrorSql = "(l.level IN ('Error', 'Critical') OR l.status_code >= 500)";

        var summarySql = $"""
            SELECT
                COUNT(*) FILTER (WHERE {isRequestSql}) AS TotalRequests,
                COUNT(*) AS TotalEvents,
                COUNT(*) FILTER (WHERE {isErrorSql}) AS TotalErrors,
                COUNT(*) FILTER (WHERE {isRequestSql} AND {isErrorSql}) AS HttpErrorCount
            FROM log_events l
            {where};
            """;

        var timelineSql = $"""
            SELECT
                {bucketExpr} AS Timestamp,
                COUNT(*) FILTER (WHERE {isRequestSql}) AS Requests,
                COUNT(*) FILTER (WHERE {isErrorSql}) AS Errors
            FROM log_events l
            {where}
            GROUP BY 1
            ORDER BY 1 ASC;
            """;

        var byAppSql = $"""
            SELECT
                a.id AS ApplicationId,
                a.name AS ApplicationName,
                a.slug AS ApplicationSlug,
                COUNT(*) FILTER (WHERE {isRequestSql}) AS TotalRequests,
                COUNT(*) FILTER (WHERE {isErrorSql}) AS TotalErrors,
                COUNT(*) FILTER (WHERE {isRequestSql} AND {isErrorSql}) AS HttpErrorCount
            FROM log_events l
            INNER JOIN applications a ON a.id = l.application_id
            {where}
            GROUP BY a.id, a.name, a.slug
            ORDER BY TotalErrors DESC, TotalRequests DESC, a.name ASC;
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);
        var summaryRow = await conn.QuerySingleAsync<ErrorRateSummaryDto>(summarySql, p);
        summaryRow.ErrorRatePercent = ComputeErrorRate(summaryRow.TotalRequests, summaryRow.HttpErrorCount, summaryRow.TotalEvents, summaryRow.TotalErrors);

        var rawTimeline = (await conn.QueryAsync<ErrorRateBucketDto>(timelineSql, p)).ToList();
        var timelineMap = new Dictionary<DateTimeOffset, ErrorRateBucketDto>();

        var startBucket = AnalyticsBucketHelper.FloorToBucket(resolvedFrom, bucketSeconds);
        var endBucket = AnalyticsBucketHelper.FloorToBucket(resolvedTo, bucketSeconds);
        var estimatedBuckets = (long)((endBucket - startBucket).TotalSeconds / bucketSeconds) + 1;
        if (estimatedBuckets is > 0 and <= 180)
        {
            for (var ts = startBucket; ts <= endBucket; ts = ts.AddSeconds(bucketSeconds))
            {
                timelineMap[ts] = new ErrorRateBucketDto { Timestamp = ts };
            }
        }

        foreach (var row in rawTimeline)
        {
            var key = AnalyticsBucketHelper.FloorToBucket(row.Timestamp.ToUniversalTime(), bucketSeconds);
            row.Timestamp = key;
            row.ErrorRatePercent = row.Requests > 0
                ? Math.Round(Math.Min(100.0, row.Errors * 100.0 / row.Requests), 2)
                : 0;
            timelineMap[key] = row;
        }

        var rawApps = (await conn.QueryAsync<AppErrorRow>(byAppSql, p)).ToList();
        var byApp = rawApps.Select(r => new AppErrorBreakdownDto
        {
            ApplicationId = r.ApplicationId,
            ApplicationName = r.ApplicationName,
            ApplicationSlug = r.ApplicationSlug,
            TotalRequests = r.TotalRequests,
            TotalErrors = r.TotalErrors,
            ErrorRatePercent = ComputeErrorRate(r.TotalRequests, r.HttpErrorCount, r.TotalRequests + r.TotalErrors, r.TotalErrors)
        }).ToList();

        return new ErrorRateAnalyticsResponse
        {
            BucketSeconds = bucketSeconds,
            From = resolvedFrom,
            To = resolvedTo,
            Summary = summaryRow,
            Timeline = timelineMap.Values.OrderBy(t => t.Timestamp).ToList(),
            ByApplication = byApp
        };
    }

    public async Task<LatencyAnalyticsResponse> GetLatencyAnalyticsAsync(
        Guid[]? applicationIds,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? interval = null,
        int top = 10,
        CancellationToken ct = default)
    {
        var (resolvedFrom, resolvedTo) = AnalyticsBucketHelper.ResolveTimeWindow(from, to, TimeSpan.FromHours(24));
        var bucketSeconds = AnalyticsBucketHelper.ResolveBucketSeconds(resolvedFrom, resolvedTo, interval);
        var bucketExpr = AnalyticsBucketHelper.BuildBucketExpression("l.timestamp", bucketSeconds);
        var limit = Math.Clamp(top, 1, 50);

        var (where, p) = BuildAnalyticsWhereClause(resolvedFrom, resolvedTo, applicationIds, "l");
        p.Add("TopLimit", limit);

        const string incomingFilter = "(l.event_type = 'Request' OR (l.event_type = 'Exception' AND l.http_method IS NOT NULL)) AND l.elapsed_ms IS NOT NULL";

        var summarySql = $"""
            SELECT
                COUNT(*) AS SampleCount,
                COALESCE(ROUND(AVG(l.elapsed_ms)::numeric, 2), 0)::double precision AS AvgMs,
                COALESCE(ROUND((percentile_cont(0.50) WITHIN GROUP (ORDER BY l.elapsed_ms))::numeric, 2), 0)::double precision AS P50Ms,
                COALESCE(ROUND((percentile_cont(0.95) WITHIN GROUP (ORDER BY l.elapsed_ms))::numeric, 2), 0)::double precision AS P95Ms,
                COALESCE(ROUND((percentile_cont(0.99) WITHIN GROUP (ORDER BY l.elapsed_ms))::numeric, 2), 0)::double precision AS P99Ms
            FROM log_events l
            {where} AND {incomingFilter};
            """;

        var timelineSql = $"""
            SELECT
                {bucketExpr} AS Timestamp,
                COUNT(*) AS Count,
                COALESCE(ROUND(AVG(l.elapsed_ms)::numeric, 2), 0)::double precision AS AvgMs,
                COALESCE(ROUND((percentile_cont(0.50) WITHIN GROUP (ORDER BY l.elapsed_ms))::numeric, 2), 0)::double precision AS P50Ms,
                COALESCE(ROUND((percentile_cont(0.95) WITHIN GROUP (ORDER BY l.elapsed_ms))::numeric, 2), 0)::double precision AS P95Ms,
                COALESCE(ROUND((percentile_cont(0.99) WITHIN GROUP (ORDER BY l.elapsed_ms))::numeric, 2), 0)::double precision AS P99Ms
            FROM log_events l
            {where} AND {incomingFilter}
            GROUP BY 1
            ORDER BY 1 ASC;
            """;

        var slowestEndpointsSql = $"""
            SELECT
                a.id AS ApplicationId,
                a.name AS ApplicationName,
                a.slug AS ApplicationSlug,
                l.http_method AS HttpMethod,
                NULL::text AS TargetHost,
                l.request_path AS RequestPath,
                COUNT(*) AS Count,
                COUNT(*) FILTER (WHERE l.status_code >= 500 OR l.level IN ('Error', 'Critical')) AS ErrorCount,
                COALESCE(ROUND(AVG(l.elapsed_ms)::numeric, 2), 0)::double precision AS AvgMs,
                COALESCE(ROUND((percentile_cont(0.95) WITHIN GROUP (ORDER BY l.elapsed_ms))::numeric, 2), 0)::double precision AS P95Ms,
                COALESCE(ROUND(MAX(l.elapsed_ms)::numeric, 2), 0)::double precision AS MaxMs
            FROM log_events l
            INNER JOIN applications a ON a.id = l.application_id
            {where} AND {incomingFilter} AND l.request_path IS NOT NULL
            GROUP BY a.id, a.name, a.slug, l.http_method, l.request_path
            ORDER BY P95Ms DESC, AvgMs DESC
            LIMIT @TopLimit;
            """;

        var slowestDependenciesSql = $"""
            SELECT
                a.id AS ApplicationId,
                a.name AS ApplicationName,
                a.slug AS ApplicationSlug,
                l.http_method AS HttpMethod,
                l.target_host AS TargetHost,
                COALESCE(l.request_path, '/') AS RequestPath,
                COUNT(*) AS Count,
                COUNT(*) FILTER (WHERE l.status_code >= 500 OR l.level IN ('Error', 'Critical')) AS ErrorCount,
                COALESCE(ROUND(AVG(l.elapsed_ms)::numeric, 2), 0)::double precision AS AvgMs,
                COALESCE(ROUND((percentile_cont(0.95) WITHIN GROUP (ORDER BY l.elapsed_ms))::numeric, 2), 0)::double precision AS P95Ms,
                COALESCE(ROUND(MAX(l.elapsed_ms)::numeric, 2), 0)::double precision AS MaxMs
            FROM log_events l
            INNER JOIN applications a ON a.id = l.application_id
            {where} AND l.event_type = 'OutgoingHttp' AND l.elapsed_ms IS NOT NULL
            GROUP BY a.id, a.name, a.slug, l.target_host, l.http_method, COALESCE(l.request_path, '/')
            ORDER BY P95Ms DESC, AvgMs DESC
            LIMIT @TopLimit;
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);
        var summary = await conn.QuerySingleAsync<LatencySummaryDto>(summarySql, p);
        var timeline = (await conn.QueryAsync<LatencyBucketDto>(timelineSql, p)).ToList();
        var slowestEndpoints = (await conn.QueryAsync<SlowEndpointDto>(slowestEndpointsSql, p)).ToList();
        var slowestDependencies = (await conn.QueryAsync<SlowEndpointDto>(slowestDependenciesSql, p)).ToList();

        return new LatencyAnalyticsResponse
        {
            BucketSeconds = bucketSeconds,
            From = resolvedFrom,
            To = resolvedTo,
            Summary = summary,
            Timeline = timeline,
            SlowestEndpoints = slowestEndpoints,
            SlowestDependencies = slowestDependencies
        };
    }

    public async Task<ResourceAnalyticsResponse> GetResourceAnalyticsAsync(
        Guid[]? applicationIds,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var (resolvedFrom, resolvedTo) = AnalyticsBucketHelper.ResolveTimeWindow(from, to, TimeSpan.FromHours(24));
        var (where, p) = BuildAnalyticsWhereClause(resolvedFrom, resolvedTo, applicationIds, "m");

        var sql = $"""
            SELECT
                a.id AS ApplicationId,
                a.name AS ApplicationName,
                a.slug AS ApplicationSlug,
                m.timestamp AS Timestamp,
                m.cpu_percent AS CpuPercent,
                m.working_set_mb AS WorkingSetMb,
                m.gc_heap_mb AS GcHeapMb,
                m.thread_count AS ThreadCount,
                m.pending_work_items AS PendingWorkItems,
                m.gen2_collections AS Gen2Collections
            FROM app_metrics m
            INNER JOIN applications a ON a.id = m.application_id
            {where}
            ORDER BY a.name ASC, m.timestamp ASC;
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);
        var rows = (await conn.QueryAsync<ResourceMetricRow>(sql, p)).ToList();

        var series = rows
            .GroupBy(r => (r.ApplicationId, r.ApplicationName, r.ApplicationSlug))
            .Select(g => new AppResourceSeriesDto
            {
                ApplicationId = g.Key.ApplicationId,
                ApplicationName = g.Key.ApplicationName,
                ApplicationSlug = g.Key.ApplicationSlug,
                Points = g.Select(r => new ResourcePointDto
                {
                    Timestamp = r.Timestamp,
                    CpuPercent = Math.Round(r.CpuPercent, 2),
                    WorkingSetMb = Math.Round(r.WorkingSetMb, 2),
                    GcHeapMb = Math.Round(r.GcHeapMb, 2),
                    ThreadCount = r.ThreadCount,
                    PendingWorkItems = r.PendingWorkItems,
                    Gen2Collections = r.Gen2Collections
                }).ToList()
            })
            .ToList();

        return new ResourceAnalyticsResponse
        {
            From = resolvedFrom,
            To = resolvedTo,
            Series = series
        };
    }

    public async Task<TopExceptionsResponse> GetTopExceptionsAsync(
        Guid[]? applicationIds,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit = 15,
        CancellationToken ct = default)
    {
        var (resolvedFrom, resolvedTo) = AnalyticsBucketHelper.ResolveTimeWindow(from, to, TimeSpan.FromHours(24));
        var (where, p) = BuildAnalyticsWhereClause(resolvedFrom, resolvedTo, applicationIds, "l");
        p.Add("Limit", Math.Clamp(limit, 1, 50));

        var sql = $"""
            SELECT
                a.id AS ApplicationId,
                a.name AS ApplicationName,
                a.slug AS ApplicationSlug,
                l.exception_type AS ExceptionType,
                l.request_path AS RequestPath,
                MAX(l.exception_message) AS SampleMessage,
                COUNT(*) AS Count,
                MIN(l.timestamp) AS FirstSeen,
                MAX(l.timestamp) AS LastSeen
            FROM log_events l
            INNER JOIN applications a ON a.id = l.application_id
            {where} AND l.exception_type IS NOT NULL AND l.exception_type <> ''
            GROUP BY a.id, a.name, a.slug, l.exception_type, l.request_path
            ORDER BY Count DESC, LastSeen DESC
            LIMIT @Limit;
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);
        var items = (await conn.QueryAsync<TopExceptionDto>(sql, p)).ToList();

        return new TopExceptionsResponse
        {
            From = resolvedFrom,
            To = resolvedTo,
            Items = items
        };
    }

    private static (string WhereSql, DynamicParameters Params) BuildAnalyticsWhereClause(
        DateTimeOffset from,
        DateTimeOffset to,
        Guid[]? applicationIds,
        string alias)
    {
        var where = new StringBuilder($"WHERE {alias}.timestamp >= @From AND {alias}.timestamp <= @To");
        var p = new DynamicParameters();
        p.Add("From", from.UtcDateTime);
        p.Add("To", to.UtcDateTime);

        if (applicationIds is { Length: > 0 })
        {
            where.Append($" AND {alias}.application_id = ANY(@ApplicationIds)");
            p.Add("ApplicationIds", applicationIds);
        }

        return (where.ToString(), p);
    }

    private static double ComputeErrorRate(long totalRequests, long httpErrorCount, long totalEvents, long totalErrors)
    {
        if (totalRequests > 0)
        {
            return Math.Round(httpErrorCount * 100.0 / totalRequests, 2);
        }

        if (totalEvents > 0)
        {
            return Math.Round(totalErrors * 100.0 / totalEvents, 2);
        }

        return 0;
    }

    private sealed class VolumeRow
    {
        public DateTimeOffset Bucket { get; set; }
        public string? GroupKey { get; set; }
        public long Count { get; set; }
    }

    private sealed class AppErrorRow
    {
        public Guid ApplicationId { get; set; }
        public string ApplicationName { get; set; } = string.Empty;
        public string ApplicationSlug { get; set; } = string.Empty;
        public long TotalRequests { get; set; }
        public long TotalErrors { get; set; }
        public long HttpErrorCount { get; set; }
    }

    private sealed class ResourceMetricRow
    {
        public Guid ApplicationId { get; set; }
        public string ApplicationName { get; set; } = string.Empty;
        public string ApplicationSlug { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
        public double CpuPercent { get; set; }
        public double WorkingSetMb { get; set; }
        public double GcHeapMb { get; set; }
        public int ThreadCount { get; set; }
        public long PendingWorkItems { get; set; }
        public int Gen2Collections { get; set; }
    }

    // ─── Retention ─────────────────────────────────────────────────

    public async Task<(long DeletedLogs, long DeletedMetrics)> PurgeExpiredTelemetryAsync(
        int retentionDays,
        CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        const string sql = "SELECT deleted_logs AS DeletedLogs, deleted_metrics AS DeletedMetrics FROM purge_expired_telemetry(@Days);";
        var res = await conn.QuerySingleAsync<(long DeletedLogs, long DeletedMetrics)>(sql, new { Days = Math.Max(1, retentionDays) });
        return res;
    }
}
