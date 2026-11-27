import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import {
  Activity,
  AlertTriangle,
  BarChart3,
  Clock,
  Cpu,
  ExternalLink,
  Gauge,
  MemoryStick,
  RefreshCw,
  Server,
} from 'lucide-react';
import { apiClient } from '../api/client';
import {
  MultiLineChart,
  RequestsErrorsChart,
  SERIES_PALETTE,
  type LineSeries,
} from '../components/AnalyticsCharts';
import type { SlowEndpointDto, TopExceptionDto } from '../types/telemetry';

const TIME_RANGES = [
  { label: 'Last 1h', hours: 1 },
  { label: 'Last 6h', hours: 6 },
  { label: 'Last 24h', hours: 24 },
  { label: 'Last 7d', hours: 24 * 7 },
  { label: 'Last 30d', hours: 24 * 30 },
];

type ResourceMetricTab = 'workingSetMb' | 'gcHeapMb' | 'threadCount';
type SlowTableTab = 'endpoints' | 'dependencies';

export function AnalyticsPage() {
  const navigate = useNavigate();
  const [hours, setHours] = useState<number>(24);
  const [selectedAppId, setSelectedAppId] = useState<string>('');
  const [resourceTab, setResourceTab] = useState<ResourceMetricTab>('workingSetMb');
  const [slowTab, setSlowTab] = useState<SlowTableTab>('endpoints');
  const [refreshTick, setRefreshTick] = useState(0);

  const { data: apps = [] } = useQuery({
    queryKey: ['applications'],
    queryFn: apiClient.getApplications,
    staleTime: 60_000,
  });

  const { fromIso, toIso } = useMemo(() => {
    const now = Date.now();
    return {
      fromIso: new Date(now - hours * 3_600_000).toISOString(),
      toIso: new Date(now).toISOString(),
    };
  }, [hours, refreshTick]);

  const filterParams = useMemo(
    () => ({
      applicationIds: selectedAppId ? [selectedAppId] : undefined,
      from: fromIso,
      to: toIso,
    }),
    [selectedAppId, fromIso, toIso]
  );

  const errorRateQuery = useQuery({
    queryKey: ['analytics-error-rate', filterParams],
    queryFn: () => apiClient.getErrorRateAnalytics(filterParams),
  });

  const latencyQuery = useQuery({
    queryKey: ['analytics-latency', filterParams],
    queryFn: () => apiClient.getLatencyAnalytics({ ...filterParams, top: 10 }),
  });

  const resourcesQuery = useQuery({
    queryKey: ['analytics-resources', filterParams],
    queryFn: () => apiClient.getResourceAnalytics(filterParams),
  });

  const topExceptionsQuery = useQuery({
    queryKey: ['analytics-top-exceptions', filterParams],
    queryFn: () => apiClient.getTopExceptions({ ...filterParams, limit: 15 }),
  });

  const isAnyFetching =
    errorRateQuery.isFetching ||
    latencyQuery.isFetching ||
    resourcesQuery.isFetching ||
    topExceptionsQuery.isFetching;

  const handleRefreshAll = () => {
    setRefreshTick((t) => t + 1);
  };

  // Build Latency P50/P95/P99 line series
  const latencySeries: LineSeries[] = useMemo(() => {
    const timeline = latencyQuery.data?.timeline ?? [];
    if (timeline.length === 0) return [];
    return [
      {
        id: 'p50',
        label: 'P50 (Median)',
        color: '#38bdf8',
        points: timeline.map((b) => ({ timestamp: b.timestamp, value: b.p50Ms })),
      },
      {
        id: 'p95',
        label: 'P95',
        color: '#fbbf24',
        points: timeline.map((b) => ({ timestamp: b.timestamp, value: b.p95Ms })),
      },
      {
        id: 'p99',
        label: 'P99',
        color: '#fb7185',
        points: timeline.map((b) => ({ timestamp: b.timestamp, value: b.p99Ms })),
      },
    ];
  }, [latencyQuery.data]);

  // Build CPU % series per API
  const cpuSeries: LineSeries[] = useMemo(() => {
    const list = resourcesQuery.data?.series ?? [];
    return list.map((s, idx) => ({
      id: s.applicationId,
      label: s.applicationName,
      color: SERIES_PALETTE[idx % SERIES_PALETTE.length],
      points: s.points.map((pt) => ({ timestamp: pt.timestamp, value: pt.cpuPercent })),
    }));
  }, [resourcesQuery.data]);

  // Build Memory / GC Heap / Thread series per API
  const memorySeries: LineSeries[] = useMemo(() => {
    const list = resourcesQuery.data?.series ?? [];
    return list.map((s, idx) => ({
      id: s.applicationId,
      label: s.applicationName,
      color: SERIES_PALETTE[idx % SERIES_PALETTE.length],
      points: s.points.map((pt) => ({
        timestamp: pt.timestamp,
        value:
          resourceTab === 'workingSetMb'
            ? pt.workingSetMb
            : resourceTab === 'gcHeapMb'
              ? pt.gcHeapMb
              : pt.threadCount,
      })),
    }));
  }, [resourcesQuery.data, resourceTab]);

  const openExceptionInLogs = (item: TopExceptionDto) => {
    const qs = new URLSearchParams();
    qs.set('q', item.exceptionType);
    qs.set('appId', item.applicationId);
    qs.set('level', 'Error,Critical');
    qs.set('from', fromIso);
    qs.set('to', toIso);
    navigate(`/?${qs.toString()}`);
  };

  const openEndpointInLogs = (ep: SlowEndpointDto, isDependency: boolean) => {
    const qs = new URLSearchParams();
    qs.set('q', ep.requestPath);
    qs.set('appId', ep.applicationId);
    qs.set('eventType', isDependency ? 'OutgoingHttp' : 'Request');
    qs.set('from', fromIso);
    qs.set('to', toIso);
    navigate(`/?${qs.toString()}`);
  };

  const openAppErrorsInLogs = (appId: string) => {
    const qs = new URLSearchParams();
    qs.set('appId', appId);
    qs.set('level', 'Error,Critical');
    qs.set('from', fromIso);
    qs.set('to', toIso);
    navigate(`/?${qs.toString()}`);
  };

  const selectedAppName = useMemo(() => {
    if (!selectedAppId) return 'All Applications';
    return apps.find((a) => a.id === selectedAppId)?.name ?? 'Selected Application';
  }, [apps, selectedAppId]);

  const errSummary = errorRateQuery.data?.summary;
  const latSummary = latencyQuery.data?.summary;
  const appBreakdown = errorRateQuery.data?.byApplication ?? [];
  const maxAppErrors = Math.max(1, ...appBreakdown.map((a) => a.totalErrors));
  const slowRows =
    slowTab === 'endpoints'
      ? (latencyQuery.data?.slowestEndpoints ?? [])
      : (latencyQuery.data?.slowestDependencies ?? []);

  return (
    <div className="h-[calc(100vh-3.5rem)] flex overflow-hidden">
      {/* Fixed, Non-Scrollable Left Sidebar */}
      <aside className="w-64 shrink-0 border-r border-slate-800 bg-slate-900/30 p-5 flex flex-col gap-6 overflow-hidden text-sm">
        {/* Time Range & Refresh */}
        <div className="space-y-2.5">
          <div className="text-xs font-semibold uppercase tracking-wider text-slate-400">
            Time Range
          </div>
          <div className="flex items-center gap-2">
            <div className="flex-1 flex items-center gap-1.5 bg-slate-950 border border-slate-800 rounded-md px-2.5 py-2 text-xs">
              <Clock className="w-3.5 h-3.5 text-slate-400 shrink-0" />
              <select
                value={hours}
                onChange={(e) => setHours(Number(e.target.value))}
                className="w-full bg-transparent text-slate-200 focus:outline-none cursor-pointer"
              >
                {TIME_RANGES.map((r) => (
                  <option key={r.label} value={r.hours} className="bg-slate-900">
                    {r.label}
                  </option>
                ))}
              </select>
            </div>
            <button
              onClick={handleRefreshAll}
              className="p-2 rounded-md bg-slate-950 border border-slate-800 hover:bg-slate-800 text-slate-300 cursor-pointer shrink-0"
              title="Refresh analytics"
            >
              <RefreshCw className={`w-4 h-4 ${isAnyFetching ? 'animate-spin text-emerald-400' : ''}`} />
            </button>
          </div>
        </div>

        {/* Applications List */}
        <div className="space-y-2.5">
          <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-slate-400">
            <Server className="w-3.5 h-3.5" />
            <span>Applications</span>
          </div>
          <div className="space-y-1">
            <button
              type="button"
              onClick={() => setSelectedAppId('')}
              className={`w-full text-left px-3 py-2 rounded-md text-sm transition-colors cursor-pointer flex items-center justify-between ${
                selectedAppId === ''
                  ? 'bg-emerald-500/15 text-emerald-300 border border-emerald-500/30 font-medium'
                  : 'text-slate-300 hover:text-slate-100 hover:bg-slate-800/60 border border-transparent'
              }`}
            >
              <span className="truncate">All Applications</span>
              <span className="text-[11px] font-mono text-slate-500">{apps.length}</span>
            </button>

            {apps.map((app) => {
              const isSelected = selectedAppId === app.id;
              return (
                <button
                  key={app.id}
                  type="button"
                  onClick={() => setSelectedAppId(app.id)}
                  className={`w-full text-left px-3 py-2 rounded-md text-sm transition-colors cursor-pointer flex items-center ${
                    isSelected
                      ? 'bg-emerald-500/15 text-emerald-300 border border-emerald-500/30 font-medium'
                      : 'text-slate-300 hover:text-slate-100 hover:bg-slate-800/60 border border-transparent'
                  }`}
                >
                  <span className="truncate">{app.name}</span>
                </button>
              );
            })}
          </div>
        </div>
      </aside>

      {/* Full-Width Scrollable Main Content */}
      <div className="flex-1 min-w-0 overflow-y-auto px-6 py-6 space-y-6">
        {/* Header */}
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h1 className="text-xl font-semibold text-slate-100 flex items-center gap-2">
              <BarChart3 className="w-5 h-5 text-emerald-400" />
              Analytics &amp; Performance
              <span className="text-xs font-normal text-slate-400 bg-slate-900 border border-slate-800 px-2.5 py-0.5 rounded-full">
                {selectedAppName}
              </span>
            </h1>
            <p className="text-sm text-slate-400 mt-0.5">
              Traffic volume, error rates, latency percentiles, resource trends, and recurring exceptions.
            </p>
          </div>
        </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <KpiCard
          title="Total Requests"
          value={errSummary ? errSummary.totalRequests.toLocaleString() : '—'}
          subtitle={
            errSummary ? `${errSummary.totalEvents.toLocaleString()} total telemetry events` : 'Loading...'
          }
          icon={<Activity className="w-4 h-4 text-emerald-400" />}
        />
        <KpiCard
          title="Error Rate"
          value={errSummary ? `${errSummary.errorRatePercent.toFixed(2)}%` : '—'}
          valueClassName={
            errSummary
              ? errSummary.errorRatePercent >= 5
                ? 'text-rose-400'
                : errSummary.errorRatePercent >= 1
                  ? 'text-amber-400'
                  : 'text-emerald-400'
              : 'text-slate-100'
          }
          subtitle={
            errSummary
              ? `${errSummary.totalErrors.toLocaleString()} errors (${errSummary.httpErrorCount.toLocaleString()} HTTP 5xx)`
              : 'Loading...'
          }
          icon={<AlertTriangle className="w-4 h-4 text-rose-400" />}
        />
        <KpiCard
          title="Average Latency"
          value={latSummary && latSummary.sampleCount > 0 ? `${latSummary.avgMs.toFixed(0)} ms` : '—'}
          subtitle={
            latSummary && latSummary.sampleCount > 0
              ? `P50 median: ${latSummary.p50Ms.toFixed(0)} ms (${latSummary.sampleCount.toLocaleString()} samples)`
              : 'No latency samples'
          }
          icon={<Gauge className="w-4 h-4 text-sky-400" />}
        />
        <KpiCard
          title="P95 Latency"
          value={latSummary && latSummary.sampleCount > 0 ? `${latSummary.p95Ms.toFixed(0)} ms` : '—'}
          valueClassName={
            latSummary && latSummary.p95Ms >= 1000
              ? 'text-rose-400'
              : latSummary && latSummary.p95Ms >= 400
                ? 'text-amber-400'
                : 'text-slate-100'
          }
          subtitle={
            latSummary && latSummary.sampleCount > 0
              ? `P99 tail: ${latSummary.p99Ms.toFixed(0)} ms`
              : 'No latency samples'
          }
          icon={<Clock className="w-4 h-4 text-amber-400" />}
        />
      </div>

      {/* Row 1: Requests & Errors Over Time + Error Breakdown by API */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <div className="lg:col-span-2 bg-slate-900/70 border border-slate-800 rounded-xl p-4">
          <div className="flex items-center justify-between mb-3">
            <div>
              <h2 className="text-sm font-semibold text-slate-100">Requests &amp; Errors Over Time</h2>
              <p className="text-xs text-slate-500">Stacked request volume vs error volume per bucket</p>
            </div>
            <div className="flex items-center gap-3 text-[11px] text-slate-400">
              <span className="inline-flex items-center gap-1">
                <span className="w-2.5 h-2.5 rounded-xs bg-emerald-500/80" />
                Requests
              </span>
              <span className="inline-flex items-center gap-1">
                <span className="w-2.5 h-2.5 rounded-xs bg-rose-500/90" />
                Errors
              </span>
            </div>
          </div>
          <RequestsErrorsChart
            data={errorRateQuery.data?.timeline ?? []}
            isLoading={errorRateQuery.isLoading}
          />
        </div>

        <div className="bg-slate-900/70 border border-slate-800 rounded-xl p-4 flex flex-col">
          <div className="mb-3">
            <h2 className="text-sm font-semibold text-slate-100">Error Breakdown by API</h2>
            <p className="text-xs text-slate-500">Click an API to inspect its errors in Log Explorer</p>
          </div>
          {errorRateQuery.isLoading && appBreakdown.length === 0 ? (
            <div className="flex-1 flex items-center justify-center text-xs text-slate-500">
              Loading breakdown...
            </div>
          ) : appBreakdown.length === 0 ? (
            <div className="flex-1 flex items-center justify-center text-xs text-slate-500">
              No API traffic in this window.
            </div>
          ) : (
            <div className="space-y-3 overflow-y-auto max-h-56 pr-1">
              {appBreakdown.map((app) => {
                const widthPct = app.totalErrors > 0 ? Math.max(4, (app.totalErrors / maxAppErrors) * 100) : 0;
                return (
                  <button
                    key={app.applicationId}
                    type="button"
                    onClick={() => openAppErrorsInLogs(app.applicationId)}
                    className="w-full text-left group p-2 rounded-lg hover:bg-slate-800/70 transition-colors cursor-pointer"
                  >
                    <div className="flex items-center justify-between text-xs mb-1">
                      <span className="font-medium text-slate-200 group-hover:text-emerald-400 truncate pr-2">
                        {app.applicationName}
                      </span>
                      <span className="font-mono text-slate-400 shrink-0">
                        <span className={app.totalErrors > 0 ? 'text-rose-400 font-semibold' : 'text-slate-400'}>
                          {app.totalErrors.toLocaleString()} err
                        </span>
                        {' · '}
                        {app.errorRatePercent.toFixed(1)}%
                      </span>
                    </div>
                    <div className="h-2 rounded-full bg-slate-800 overflow-hidden">
                      <div
                        className="h-full rounded-full bg-rose-500/80 transition-all"
                        style={{ width: `${widthPct}%` }}
                      />
                    </div>
                    <div className="mt-1 text-[11px] font-mono text-slate-500">
                      {app.totalRequests.toLocaleString()} requests
                    </div>
                  </button>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Row 2: CPU % Over Time & Memory / GC / Threads Over Time */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="bg-slate-900/70 border border-slate-800 rounded-xl p-4">
          <div className="flex items-center justify-between mb-3">
            <div>
              <h2 className="text-sm font-semibold text-slate-100 flex items-center gap-1.5">
                <Cpu className="w-4 h-4 text-emerald-400" />
                CPU % Over Time per API
              </h2>
              <p className="text-xs text-slate-500">Periodic process heartbeats from app_metrics</p>
            </div>
          </div>
          <MultiLineChart
            series={cpuSeries}
            unit="%"
            isLoading={resourcesQuery.isLoading}
            emptyText="No CPU heartbeats recorded in this time range."
          />
        </div>

        <div className="bg-slate-900/70 border border-slate-800 rounded-xl p-4">
          <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
            <div>
              <h2 className="text-sm font-semibold text-slate-100 flex items-center gap-1.5">
                <MemoryStick className="w-4 h-4 text-sky-400" />
                Memory &amp; Runtime Health per API
              </h2>
              <p className="text-xs text-slate-500">Spot leaks, GC pressure, and app pool recycles</p>
            </div>
            <div className="flex items-center rounded-md border border-slate-800 bg-slate-950 p-0.5 text-[11px]">
              {(
                [
                  { id: 'workingSetMb', label: 'RAM (MB)' },
                  { id: 'gcHeapMb', label: 'GC Heap' },
                  { id: 'threadCount', label: 'Threads' },
                ] as const
              ).map((tab) => (
                <button
                  key={tab.id}
                  type="button"
                  onClick={() => setResourceTab(tab.id)}
                  className={`px-2 py-1 rounded transition-colors cursor-pointer ${
                    resourceTab === tab.id
                      ? 'bg-slate-800 text-emerald-400 font-medium'
                      : 'text-slate-400 hover:text-slate-200'
                  }`}
                >
                  {tab.label}
                </button>
              ))}
            </div>
          </div>
          <MultiLineChart
            series={memorySeries}
            unit={resourceTab === 'threadCount' ? '' : ' MB'}
            isLoading={resourcesQuery.isLoading}
            emptyText="No memory/runtime heartbeats recorded in this time range."
          />
        </div>
      </div>

      {/* Row 3: Latency Trends (P50 / P95 / P99) & Slowest Endpoints / Dependencies */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="bg-slate-900/70 border border-slate-800 rounded-xl p-4 flex flex-col">
          <div className="mb-3 shrink-0">
            <h2 className="text-sm font-semibold text-slate-100">Latency Trends (P50 / P95 / P99)</h2>
            <p className="text-xs text-slate-500">Incoming HTTP request response times in milliseconds</p>
          </div>
          <MultiLineChart
            series={latencySeries}
            unit=" ms"
            isLoading={latencyQuery.isLoading}
            emptyText="No latency data recorded in this time range."
            minHeight={220}
            fillHeight
          />
        </div>

        <div className="bg-slate-900/70 border border-slate-800 rounded-xl p-4 flex flex-col">
          <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
            <div>
              <h2 className="text-sm font-semibold text-slate-100">
                Slowest Endpoints &amp; Outgoing Dependencies
              </h2>
              <p className="text-xs text-slate-500">Ranked by P95 latency · click a row to view logs</p>
            </div>
            <div className="flex items-center rounded-md border border-slate-800 bg-slate-950 p-0.5 text-[11px]">
              <button
                type="button"
                onClick={() => setSlowTab('endpoints')}
                className={`px-2.5 py-1 rounded transition-colors cursor-pointer ${
                  slowTab === 'endpoints'
                    ? 'bg-slate-800 text-emerald-400 font-medium'
                    : 'text-slate-400 hover:text-slate-200'
                }`}
              >
                Incoming Endpoints
              </button>
              <button
                type="button"
                onClick={() => setSlowTab('dependencies')}
                className={`px-2.5 py-1 rounded transition-colors cursor-pointer ${
                  slowTab === 'dependencies'
                    ? 'bg-slate-800 text-emerald-400 font-medium'
                    : 'text-slate-400 hover:text-slate-200'
                }`}
              >
                Outgoing Dependencies
              </button>
            </div>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse text-xs">
              <thead className="border-b border-slate-800 text-slate-400 uppercase text-[10px] tracking-wider">
                <tr>
                  <th className="py-2 pr-3">API</th>
                  <th className="py-2 pr-3">{slowTab === 'dependencies' ? 'Target / Route' : 'Endpoint'}</th>
                  <th className="py-2 px-2 text-right">Calls</th>
                  <th className="py-2 px-2 text-right">Avg</th>
                  <th className="py-2 px-2 text-right">P95</th>
                  <th className="py-2 pl-2 text-right">Max</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60 font-mono">
                {latencyQuery.isLoading ? (
                  <tr>
                    <td colSpan={6} className="py-8 text-center text-slate-500 font-sans">
                      Loading latency breakdown...
                    </td>
                  </tr>
                ) : slowRows.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="py-8 text-center text-slate-500 font-sans">
                      No {slowTab === 'dependencies' ? 'outgoing HTTP calls' : 'incoming endpoints'} found.
                    </td>
                  </tr>
                ) : (
                  slowRows.map((ep, idx) => (
                    <tr
                      key={`${ep.applicationId}-${ep.targetHost ?? ''}-${ep.httpMethod ?? ''}-${ep.requestPath}-${idx}`}
                      onClick={() => openEndpointInLogs(ep, slowTab === 'dependencies')}
                      className="hover:bg-slate-800/60 cursor-pointer transition-colors"
                    >
                      <td className="py-2 pr-3 whitespace-nowrap font-sans text-slate-300">
                        {ep.applicationName}
                      </td>
                      <td className="py-2 pr-3 max-w-xs truncate text-slate-200">
                        {ep.httpMethod && (
                          <span className="text-sky-400 font-semibold mr-1.5">{ep.httpMethod}</span>
                        )}
                        {ep.targetHost && <span className="text-indigo-300 mr-1">{ep.targetHost}</span>}
                        <span>{ep.requestPath}</span>
                      </td>
                      <td className="py-2 px-2 text-right text-slate-400">
                        {ep.count.toLocaleString()}
                      </td>
                      <td className="py-2 px-2 text-right text-slate-300">{ep.avgMs.toFixed(0)}ms</td>
                      <td
                        className={`py-2 px-2 text-right font-semibold ${
                          ep.p95Ms >= 1000
                            ? 'text-rose-400'
                            : ep.p95Ms >= 400
                              ? 'text-amber-400'
                              : 'text-emerald-400'
                        }`}
                      >
                        {ep.p95Ms.toFixed(0)}ms
                      </td>
                      <td className="py-2 pl-2 text-right text-slate-400">{ep.maxMs.toFixed(0)}ms</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {/* Row 4: Top Recurring Exceptions */}
      <div className="bg-slate-900/70 border border-slate-800 rounded-xl p-4">
        <div className="flex items-center justify-between mb-3">
          <div>
            <h2 className="text-sm font-semibold text-slate-100 flex items-center gap-1.5">
              <AlertTriangle className="w-4 h-4 text-rose-400" />
              Top Recurring Exceptions
            </h2>
            <p className="text-xs text-slate-500">
              Click any exception row to open Log Explorer pre-filtered to that error
            </p>
          </div>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse text-xs">
            <thead className="border-b border-slate-800 text-slate-400 uppercase text-[10px] tracking-wider">
              <tr>
                <th className="py-2.5 pr-4">Exception Type</th>
                <th className="py-2.5 pr-4">Application</th>
                <th className="py-2.5 pr-4">Route</th>
                <th className="py-2.5 pr-4">Sample Message</th>
                <th className="py-2.5 px-3 text-right">Occurrences</th>
                <th className="py-2.5 pl-3 text-right">Last Seen</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/60 font-mono">
              {topExceptionsQuery.isLoading ? (
                <tr>
                  <td colSpan={6} className="py-8 text-center text-slate-500 font-sans">
                    Loading recurring exceptions...
                  </td>
                </tr>
              ) : !topExceptionsQuery.data || topExceptionsQuery.data.items.length === 0 ? (
                <tr>
                  <td colSpan={6} className="py-8 text-center text-slate-500 font-sans">
                    No exceptions recorded in this time window.
                  </td>
                </tr>
              ) : (
                topExceptionsQuery.data.items.map((item, idx) => (
                  <tr
                    key={`${item.applicationId}-${item.exceptionType}-${item.requestPath ?? ''}-${idx}`}
                    onClick={() => openExceptionInLogs(item)}
                    className="hover:bg-slate-800/60 cursor-pointer transition-colors group"
                  >
                    <td className="py-2.5 pr-4 text-rose-300 font-semibold whitespace-nowrap">
                      <span className="inline-flex items-center gap-1.5">
                        <span>{item.exceptionType}</span>
                        <ExternalLink className="w-3 h-3 opacity-0 group-hover:opacity-100 text-emerald-400 transition-opacity" />
                      </span>
                    </td>
                    <td className="py-2.5 pr-4 whitespace-nowrap font-sans">
                      <span className="px-2 py-0.5 rounded bg-slate-800 text-slate-200 text-[11px]">
                        {item.applicationName}
                      </span>
                    </td>
                    <td className="py-2.5 pr-4 text-slate-300 max-w-[200px] truncate">
                      {item.requestPath || '—'}
                    </td>
                    <td className="py-2.5 pr-4 text-slate-400 max-w-md truncate font-sans">
                      {item.sampleMessage || '—'}
                    </td>
                    <td className="py-2.5 px-3 text-right">
                      <span className="px-2 py-0.5 rounded-full bg-rose-500/15 border border-rose-500/30 text-rose-300 font-semibold">
                        {item.count.toLocaleString()}
                      </span>
                    </td>
                    <td className="py-2.5 pl-3 text-right text-slate-400 whitespace-nowrap">
                      {new Date(item.lastSeen).toLocaleString()}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
      </div>
    </div>
  );
}

function KpiCard({
  title,
  value,
  subtitle,
  icon,
  valueClassName = 'text-slate-100',
}: {
  title: string;
  value: string;
  subtitle: string;
  icon: React.ReactNode;
  valueClassName?: string;
}) {
  return (
    <div className="bg-slate-900/70 border border-slate-800 rounded-xl p-4">
      <div className="flex items-center justify-between text-xs text-slate-400 mb-2">
        <span className="font-medium uppercase tracking-wider text-[11px]">{title}</span>
        {icon}
      </div>
      <div className={`text-2xl font-bold font-mono tracking-tight ${valueClassName}`}>{value}</div>
      <div className="text-xs text-slate-500 mt-1 truncate">{subtitle}</div>
    </div>
  );
}
