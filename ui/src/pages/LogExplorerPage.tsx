import { useMemo, useState, type FormEvent } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  ChevronLeft,
  ChevronRight,
  Clock,
  Filter,
  RefreshCw,
  Search,
  X,
} from 'lucide-react';
import { apiClient } from '../api/client';
import { LogDetailDrawer } from '../components/LogDetailDrawer';
import type { LogSummaryDto } from '../types/telemetry';

const TIME_PRESETS = [
  { label: 'Last 15m', minutes: 15 },
  { label: 'Last 1h', minutes: 60 },
  { label: 'Last 6h', minutes: 360 },
  { label: 'Last 24h', minutes: 1440 },
  { label: 'Last 7d', minutes: 10080 },
  { label: 'All Time', minutes: 0 },
];

const REFRESH_OPTIONS = [
  { label: 'Off', ms: 0 },
  { label: '5s', ms: 5000 },
  { label: '15s', ms: 15000 },
  { label: '30s', ms: 30000 },
];

const LEVELS = ['Critical', 'Error', 'Warning', 'Information', 'Debug'];
const EVENT_TYPES = ['Request', 'OutgoingHttp', 'Exception', 'Log', 'Lifecycle'];
const STATUS_BUCKETS = ['2xx', '4xx', '5xx'];

export function LogExplorerPage() {
  const [searchInput, setSearchInput] = useState('');
  const [submittedQuery, setSubmittedQuery] = useState('');
  const [selectedAppIds, setSelectedAppIds] = useState<string[]>([]);
  const [selectedLevels, setSelectedLevels] = useState<string[]>([]);
  const [selectedEventTypes, setSelectedEventTypes] = useState<string[]>([]);
  const [statusRange, setStatusRange] = useState<string>('');
  const [timeMinutes, setTimeMinutes] = useState<number>(60);
  const [refreshMs, setRefreshMs] = useState<number>(0);
  const [page, setPage] = useState(1);
  const [selectedLogId, setSelectedLogId] = useState<number | null>(null);

  const { data: apps = [] } = useQuery({
    queryKey: ['applications'],
    queryFn: apiClient.getApplications,
    staleTime: 60_000,
  });

  const fromIso = useMemo(() => {
    if (timeMinutes === 0) return undefined;
    return new Date(Date.now() - timeMinutes * 60_000).toISOString();
  }, [timeMinutes, page, submittedQuery, selectedAppIds, selectedLevels, selectedEventTypes, statusRange]);

  const { data, isLoading, isFetching, refetch } = useQuery({
    queryKey: [
      'logs',
      submittedQuery,
      selectedAppIds,
      selectedLevels,
      selectedEventTypes,
      statusRange,
      timeMinutes,
      page,
    ],
    queryFn: () =>
      apiClient.searchLogs({
        q: submittedQuery || undefined,
        applicationIds: selectedAppIds.length ? selectedAppIds : undefined,
        levels: selectedLevels.length ? selectedLevels : undefined,
        eventTypes: selectedEventTypes.length ? selectedEventTypes : undefined,
        statusRange: statusRange || undefined,
        from: fromIso,
        page,
        pageSize: 50,
      }),
    refetchInterval: refreshMs > 0 ? refreshMs : false,
  });

  const handleSearchSubmit = (e: FormEvent) => {
    e.preventDefault();
    setPage(1);
    setSubmittedQuery(searchInput.trim());
  };

  const toggleItem = (list: string[], value: string, setter: (v: string[]) => void) => {
    setPage(1);
    setter(list.includes(value) ? list.filter((x) => x !== value) : [...list, value]);
  };

  const clearAllFilters = () => {
    setSearchInput('');
    setSubmittedQuery('');
    setSelectedAppIds([]);
    setSelectedLevels([]);
    setSelectedEventTypes([]);
    setStatusRange('');
    setPage(1);
  };

  const hasActiveFilters =
    Boolean(submittedQuery) ||
    selectedAppIds.length > 0 ||
    selectedLevels.length > 0 ||
    selectedEventTypes.length > 0 ||
    Boolean(statusRange);

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <div className="flex-1 flex flex-col">
      {/* Top Search & Controls Bar */}
      <div className="border-b border-slate-800 bg-slate-900/50 px-4 py-3 flex flex-wrap items-center gap-3">
        <form onSubmit={handleSearchSubmit} className="flex-1 min-w-[260px] flex items-center gap-2">
          <div className="relative flex-1">
            <Search className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
            <input
              type="text"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder='Search logs... e.g. status:500 level:Error type:OutgoingHttp "timeout"'
              className="w-full bg-slate-950 border border-slate-700 rounded-md pl-9 pr-8 py-2 text-sm font-mono text-slate-100 placeholder:text-slate-500 focus:outline-none focus:border-emerald-500"
            />
            {searchInput && (
              <button
                type="button"
                onClick={() => {
                  setSearchInput('');
                  setSubmittedQuery('');
                  setPage(1);
                }}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 text-slate-500 hover:text-slate-200 cursor-pointer"
              >
                <X className="w-4 h-4" />
              </button>
            )}
          </div>
          <button
            type="submit"
            className="bg-emerald-600 hover:bg-emerald-500 text-white text-sm font-medium px-4 py-2 rounded-md transition-colors cursor-pointer"
          >
            Search
          </button>
        </form>

        {/* Time Range */}
        <div className="flex items-center gap-1.5 bg-slate-950 border border-slate-800 rounded-md px-2.5 py-1.5 text-xs">
          <Clock className="w-3.5 h-3.5 text-slate-400" />
          <select
            value={timeMinutes}
            onChange={(e) => {
              setTimeMinutes(Number(e.target.value));
              setPage(1);
            }}
            className="bg-transparent text-slate-200 focus:outline-none cursor-pointer"
          >
            {TIME_PRESETS.map((p) => (
              <option key={p.label} value={p.minutes} className="bg-slate-900">
                {p.label}
              </option>
            ))}
          </select>
        </div>

        {/* Auto Refresh */}
        <div className="flex items-center gap-1.5 bg-slate-950 border border-slate-800 rounded-md px-2.5 py-1.5 text-xs">
          <span className="text-slate-400">Auto:</span>
          <select
            value={refreshMs}
            onChange={(e) => setRefreshMs(Number(e.target.value))}
            className="bg-transparent text-slate-200 focus:outline-none cursor-pointer"
          >
            {REFRESH_OPTIONS.map((r) => (
              <option key={r.label} value={r.ms} className="bg-slate-900">
                {r.label}
              </option>
            ))}
          </select>
        </div>

        {/* Manual Refresh */}
        <button
          onClick={() => refetch()}
          className="p-2 rounded-md bg-slate-900 border border-slate-700 hover:bg-slate-800 text-slate-300 cursor-pointer"
          title="Refresh now"
        >
          <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin text-emerald-400' : ''}`} />
        </button>
      </div>

      {/* Main Split: Facet Sidebar + Log Stream */}
      <div className="flex-1 flex min-h-0">
        {/* Left Facet Sidebar */}
        <aside className="w-60 shrink-0 border-r border-slate-800 bg-slate-900/30 p-4 space-y-5 overflow-y-auto hidden md:block">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-slate-400">
              <Filter className="w-3.5 h-3.5" />
              Facets
            </div>
            {hasActiveFilters && (
              <button
                onClick={clearAllFilters}
                className="text-[11px] text-emerald-400 hover:underline cursor-pointer"
              >
                Clear all
              </button>
            )}
          </div>

          {/* Applications */}
          <FacetGroup title="Applications">
            {apps.length === 0 ? (
              <p className="text-xs text-slate-500">No apps registered yet.</p>
            ) : (
              apps.map((app) => (
                <label
                  key={app.id}
                  className="flex items-center gap-2 text-xs text-slate-300 hover:text-white cursor-pointer py-0.5"
                >
                  <input
                    type="checkbox"
                    checked={selectedAppIds.includes(app.id)}
                    onChange={() => toggleItem(selectedAppIds, app.id, setSelectedAppIds)}
                    className="rounded border-slate-700 bg-slate-950 accent-emerald-500"
                  />
                  <span className="truncate">{app.name}</span>
                </label>
              ))
            )}
          </FacetGroup>

          {/* Log Levels */}
          <FacetGroup title="Log Level">
            {LEVELS.map((lvl) => (
              <label
                key={lvl}
                className="flex items-center gap-2 text-xs text-slate-300 hover:text-white cursor-pointer py-0.5"
              >
                <input
                  type="checkbox"
                  checked={selectedLevels.includes(lvl)}
                  onChange={() => toggleItem(selectedLevels, lvl, setSelectedLevels)}
                  className="rounded border-slate-700 bg-slate-950 accent-emerald-500"
                />
                <LevelDot level={lvl} />
                <span>{lvl}</span>
              </label>
            ))}
          </FacetGroup>

          {/* Event Types */}
          <FacetGroup title="Event Type">
            {EVENT_TYPES.map((t) => (
              <label
                key={t}
                className="flex items-center gap-2 text-xs text-slate-300 hover:text-white cursor-pointer py-0.5"
              >
                <input
                  type="checkbox"
                  checked={selectedEventTypes.includes(t)}
                  onChange={() => toggleItem(selectedEventTypes, t, setSelectedEventTypes)}
                  className="rounded border-slate-700 bg-slate-950 accent-emerald-500"
                />
                <span>{t}</span>
              </label>
            ))}
          </FacetGroup>

          {/* HTTP Status */}
          <FacetGroup title="HTTP Status">
            <div className="flex flex-wrap gap-1.5">
              {STATUS_BUCKETS.map((bucket) => (
                <button
                  key={bucket}
                  onClick={() => {
                    setPage(1);
                    setStatusRange(statusRange === bucket ? '' : bucket);
                  }}
                  className={`px-2.5 py-1 rounded text-xs font-mono border transition-colors cursor-pointer ${
                    statusRange === bucket
                      ? 'bg-emerald-500/20 border-emerald-500 text-emerald-300'
                      : 'bg-slate-950 border-slate-800 text-slate-400 hover:text-slate-200'
                  }`}
                >
                  {bucket}
                </button>
              ))}
            </div>
          </FacetGroup>
        </aside>

        {/* Log Table Area */}
        <div className="flex-1 flex flex-col min-w-0">
          {/* Status Summary Bar */}
          <div className="px-4 py-2 border-b border-slate-800/80 bg-slate-950 flex items-center justify-between text-xs text-slate-400">
            <div>
              {data ? (
                <span>
                  Showing <strong className="text-slate-200">{data.items.length}</strong> of{' '}
                  <strong className="text-slate-200">{data.totalCount.toLocaleString()}</strong> events
                </span>
              ) : (
                <span>Loading events...</span>
              )}
            </div>
            <div className="flex items-center gap-2">
              <button
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="p-1 rounded border border-slate-800 hover:bg-slate-800 disabled:opacity-40 cursor-pointer"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>
              <span className="font-mono">
                Page {page} / {totalPages}
              </span>
              <button
                disabled={page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
                className="p-1 rounded border border-slate-800 hover:bg-slate-800 disabled:opacity-40 cursor-pointer"
              >
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          </div>

          {/* High-Density Table */}
          <div className="flex-1 overflow-auto">
            <table className="w-full text-left border-collapse text-xs">
              <thead className="sticky top-0 bg-slate-900 border-b border-slate-800 text-slate-400 uppercase tracking-wider text-[11px]">
                <tr>
                  <th className="py-2.5 px-3 w-40">Timestamp</th>
                  <th className="py-2.5 px-3 w-32">App</th>
                  <th className="py-2.5 px-3 w-24">Level</th>
                  <th className="py-2.5 px-3 w-24">Type</th>
                  <th className="py-2.5 px-3 w-20">Status</th>
                  <th className="py-2.5 px-3 w-20 text-right">Latency</th>
                  <th className="py-2.5 px-3">Message / Endpoint</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60 font-mono">
                {isLoading ? (
                  <tr>
                    <td colSpan={7} className="py-12 text-center text-slate-400 font-sans">
                      Searching logs...
                    </td>
                  </tr>
                ) : !data || data.items.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="py-12 text-center text-slate-500 font-sans">
                      No logs match your current filters.
                    </td>
                  </tr>
                ) : (
                  data.items.map((row: LogSummaryDto) => (
                    <tr
                      key={row.id}
                      onClick={() => setSelectedLogId(row.id)}
                      className="hover:bg-slate-800/60 cursor-pointer transition-colors"
                    >
                      <td className="py-2 px-3 text-slate-400 whitespace-nowrap">
                        {new Date(row.timestamp).toLocaleString()}
                      </td>
                      <td className="py-2 px-3 whitespace-nowrap">
                        <span className="px-2 py-0.5 rounded bg-slate-800 text-slate-200 font-sans font-medium text-[11px]">
                          {row.applicationName}
                        </span>
                      </td>
                      <td className="py-2 px-3 whitespace-nowrap">
                        <LevelBadge level={row.level} />
                      </td>
                      <td className="py-2 px-3 text-slate-400 whitespace-nowrap font-sans">
                        {row.eventType}
                      </td>
                      <td className="py-2 px-3 whitespace-nowrap">
                        {row.statusCode != null ? <StatusBadge code={row.statusCode} /> : '—'}
                      </td>
                      <td className="py-2 px-3 text-right text-slate-400 whitespace-nowrap">
                        {row.elapsedMs != null ? `${row.elapsedMs.toFixed(0)}ms` : '—'}
                      </td>
                      <td className="py-2 px-3 text-slate-200 max-w-xl truncate">
                        {row.httpMethod && (
                          <span className="text-sky-400 font-semibold mr-1.5">{row.httpMethod}</span>
                        )}
                        {row.targetHost ? (
                          <span className="text-indigo-300 mr-1">{row.targetHost}</span>
                        ) : null}
                        {row.requestPath && !row.message?.includes(row.requestPath) ? (
                          <span className="text-slate-300 mr-2">
                            {row.requestPath}
                            {row.queryString ?? ''}
                          </span>
                        ) : null}
                        <span className="text-slate-300">{row.message || row.exceptionType || '—'}</span>
                        {row.queryString && row.message && !row.message.includes(row.queryString) ? (
                          <span className="text-slate-400 ml-1.5">{row.queryString}</span>
                        ) : null}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <LogDetailDrawer
        logId={selectedLogId}
        onClose={() => setSelectedLogId(null)}
        onSelectLog={(id) => setSelectedLogId(id)}
      />
    </div>
  );
}

function FacetGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 mb-2">
        {title}
      </div>
      <div className="space-y-1">{children}</div>
    </div>
  );
}

function LevelDot({ level }: { level: string }) {
  const color =
    level === 'Critical' || level === 'Error'
      ? 'bg-rose-500'
      : level === 'Warning'
        ? 'bg-amber-400'
        : level === 'Information'
          ? 'bg-sky-400'
          : 'bg-slate-500';
  return <span className={`w-2 h-2 rounded-full ${color}`} />;
}

function LevelBadge({ level }: { level: string }) {
  const styles =
    level === 'Critical'
      ? 'bg-rose-500/20 text-rose-300 border-rose-500/40'
      : level === 'Error'
        ? 'bg-rose-500/15 text-rose-400 border-rose-500/30'
        : level === 'Warning'
          ? 'bg-amber-500/15 text-amber-300 border-amber-500/30'
          : level === 'Information'
            ? 'bg-sky-500/15 text-sky-300 border-sky-500/30'
            : 'bg-slate-800 text-slate-400 border-slate-700';

  return (
    <span className={`px-1.5 py-0.5 rounded border text-[11px] font-sans font-medium ${styles}`}>
      {level}
    </span>
  );
}

function StatusBadge({ code }: { code: number }) {
  const styles =
    code >= 500
      ? 'text-rose-400'
      : code >= 400
        ? 'text-amber-400'
        : code >= 200 && code < 300
          ? 'text-emerald-400'
          : 'text-slate-300';
  return <span className={`font-semibold ${styles}`}>{code}</span>;
}
