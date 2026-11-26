import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Check, Copy, FileText, GitBranch, X } from 'lucide-react';
import { apiClient } from '../api/client';
import type { LogSummaryDto } from '../types/telemetry';

interface LogDetailDrawerProps {
  logId: number | null;
  onClose: () => void;
  onSelectLog: (id: number) => void;
}

function prettyJson(raw: string | null): string | null {
  if (!raw) return null;
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

export function LogDetailDrawer({ logId, onClose, onSelectLog }: LogDetailDrawerProps) {
  const [activeTab, setActiveTab] = useState<'details' | 'payload' | 'trace'>('details');
  const [copiedKey, setCopiedKey] = useState<string | null>(null);

  const { data: detail, isLoading } = useQuery({
    queryKey: ['log-detail', logId],
    queryFn: () => apiClient.getLogDetail(logId!),
    enabled: logId !== null,
  });

  const { data: traceData, isLoading: traceLoading } = useQuery({
    queryKey: ['log-trace', detail?.traceId],
    queryFn: () => apiClient.getTraceLogs(detail!.traceId!),
    enabled: activeTab === 'trace' && Boolean(detail?.traceId),
  });

  useEffect(() => {
    if (activeTab === 'trace' && detail && !detail.traceId) {
      setActiveTab('details');
    }
  }, [activeTab, detail]);

  const copyText = (key: string, value: string) => {
    navigator.clipboard.writeText(value);
    setCopiedKey(key);
    setTimeout(() => setCopiedKey(null), 1800);
  };

  if (logId === null) {
    return (
      <aside className="w-[768px] max-w-3xl shrink-0 bg-slate-900/70 border-l border-slate-800 h-full flex flex-col items-center justify-center p-8 text-center">
        <div className="w-12 h-12 rounded-xl bg-slate-800/80 border border-slate-700/80 flex items-center justify-center text-slate-400 mb-4">
          <FileText className="w-6 h-6" />
        </div>
        <h2 className="text-sm font-medium text-slate-200">No log selected</h2>
        <p className="text-xs text-slate-400 mt-1.5 max-w-xs leading-relaxed">
          Click any row in the log table to inspect its details, headers, payloads, and correlated trace.
        </p>
        <div className="mt-4 flex items-center gap-2 text-[11px] text-slate-500">
          <kbd className="px-1.5 py-0.5 rounded bg-slate-950 border border-slate-800 font-mono text-slate-400">
            ↑
          </kbd>
          <kbd className="px-1.5 py-0.5 rounded bg-slate-950 border border-slate-800 font-mono text-slate-400">
            ↓
          </kbd>
          <span>Navigate rows</span>
        </div>
      </aside>
    );
  }

  return (
    <aside className="w-[768px] max-w-3xl shrink-0 bg-slate-900 border-l border-slate-800 h-full flex flex-col">
      {/* Header */}
      <div className="px-5 py-4 border-b border-slate-800 flex items-center justify-between gap-4">
        <div className="min-w-0">
          <div className="flex items-center gap-2 text-xs text-slate-400">
            <span className="font-semibold text-emerald-400">{detail?.applicationName ?? 'Loading...'}</span>
            <span>•</span>
            <span>{detail?.eventType}</span>
            <span>•</span>
            <span className="font-mono">{detail ? new Date(detail.timestamp).toLocaleString() : ''}</span>
          </div>
          <h2 className="text-sm font-medium text-slate-100 truncate mt-1">
            {detail?.message || detail?.requestPath || `Log #${logId}`}
          </h2>
        </div>

        <button
          onClick={onClose}
          title="Clear selection (Esc)"
          className="p-1.5 rounded-md text-slate-400 hover:text-slate-100 hover:bg-slate-800 cursor-pointer"
        >
          <X className="w-5 h-5" />
        </button>
      </div>

        {/* Tabs */}
        <div className="px-5 border-b border-slate-800 flex items-center gap-4 text-sm">
          <button
            onClick={() => setActiveTab('details')}
            className={`py-2.5 border-b-2 font-medium transition-colors cursor-pointer ${
              activeTab === 'details'
                ? 'border-emerald-400 text-emerald-400'
                : 'border-transparent text-slate-400 hover:text-slate-200'
            }`}
          >
            Details &amp; Exception
          </button>
          <button
            onClick={() => setActiveTab('payload')}
            className={`py-2.5 border-b-2 font-medium transition-colors cursor-pointer ${
              activeTab === 'payload'
                ? 'border-emerald-400 text-emerald-400'
                : 'border-transparent text-slate-400 hover:text-slate-200'
            }`}
          >
            Headers &amp; Bodies
          </button>
          {detail?.traceId && (
            <button
              onClick={() => setActiveTab('trace')}
              className={`py-2.5 border-b-2 font-medium flex items-center gap-1.5 transition-colors cursor-pointer ${
                activeTab === 'trace'
                  ? 'border-emerald-400 text-emerald-400'
                  : 'border-transparent text-slate-400 hover:text-slate-200'
              }`}
            >
              <GitBranch className="w-3.5 h-3.5" />
              Correlated Trace
            </button>
          )}
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-5 space-y-5 text-sm">
          {isLoading || !detail ? (
            <div className="text-slate-400 py-10 text-center">Loading log details...</div>
          ) : activeTab === 'details' ? (
            <>
              <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 bg-slate-950/70 border border-slate-800 rounded-lg p-3.5 text-xs">
                <MetaField label="Level" value={detail.level} />
                <MetaField label="Event Type" value={detail.eventType} />
                <MetaField label="Environment" value={detail.environment} />
                <MetaField label="HTTP Method" value={detail.httpMethod} />
                <MetaField label="Status Code" value={detail.statusCode?.toString()} />
                <MetaField label="Duration" value={detail.elapsedMs != null ? `${detail.elapsedMs.toFixed(1)} ms` : null} />
                <MetaField label="Client IP" value={detail.clientIp} />
                <MetaField label="Target Host" value={detail.targetHost} />
                <MetaField label="Category" value={detail.category} />
              </div>

              {detail.requestPath && (
                <div>
                  <SectionLabel label="URL / Path" />
                  <div className="font-mono text-xs bg-slate-950 border border-slate-800 rounded-md p-3 break-all text-slate-200">
                    {detail.httpMethod ? `${detail.httpMethod} ` : ''}
                    {detail.targetHost ? `https://${detail.targetHost}` : ''}
                    {detail.requestPath}
                    {detail.queryString ?? ''}
                  </div>
                </div>
              )}

              {detail.traceId && (
                <div>
                  <SectionLabel label="Trace ID" />
                  <div className="flex items-center justify-between gap-2 font-mono text-xs bg-slate-950 border border-slate-800 rounded-md px-3 py-2 text-emerald-300">
                    <span className="truncate">{detail.traceId}</span>
                    <button
                      onClick={() => copyText('trace', detail.traceId!)}
                      className="text-slate-400 hover:text-slate-100 shrink-0 cursor-pointer"
                      title="Copy Trace ID"
                    >
                      {copiedKey === 'trace' ? <Check className="w-4 h-4 text-emerald-400" /> : <Copy className="w-4 h-4" />}
                    </button>
                  </div>
                </div>
              )}

              {detail.message && (
                <div>
                  <SectionLabel label="Message" />
                  <pre className="font-mono text-xs bg-slate-950 border border-slate-800 rounded-md p-3 whitespace-pre-wrap break-words text-slate-200">
                    {detail.message}
                  </pre>
                </div>
              )}

              {(detail.exceptionType || detail.stackTrace) && (
                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="text-xs font-semibold uppercase tracking-wider text-rose-400">
                      Exception: {detail.exceptionType}
                    </span>
                    {detail.stackTrace && (
                      <button
                        onClick={() => copyText('stack', detail.stackTrace!)}
                        className="text-xs text-slate-400 hover:text-slate-100 flex items-center gap-1 cursor-pointer"
                      >
                        {copiedKey === 'stack' ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
                        Copy Stack Trace
                      </button>
                    )}
                  </div>
                  {detail.exceptionMessage && (
                    <div className="text-xs text-rose-300 bg-rose-950/40 border border-rose-900/60 rounded-t-md px-3 py-2 font-medium">
                      {detail.exceptionMessage}
                    </div>
                  )}
                  {detail.stackTrace && (
                    <pre className="font-mono text-xs bg-slate-950 border border-rose-900/50 rounded-b-md p-3 overflow-x-auto text-rose-200/90 leading-relaxed">
                      {detail.stackTrace}
                    </pre>
                  )}
                </div>
              )}

              {detail.propertiesJson && (
                <div>
                  <SectionLabel label="Structured Properties" />
                  <pre className="font-mono text-xs bg-slate-950 border border-slate-800 rounded-md p-3 overflow-x-auto text-sky-200">
                    {prettyJson(detail.propertiesJson)}
                  </pre>
                </div>
              )}
            </>
          ) : activeTab === 'payload' ? (
            <>
              <div>
                <SectionLabel label="Request Headers (Redacted)" />
                {detail.requestHeadersJson ? (
                  <pre className="font-mono text-xs bg-slate-950 border border-slate-800 rounded-md p-3 overflow-x-auto text-slate-200">
                    {prettyJson(detail.requestHeadersJson)}
                  </pre>
                ) : (
                  <p className="text-xs text-slate-500">No headers recorded for this event.</p>
                )}
              </div>

              <div>
                <SectionLabel label="Request Body" />
                {detail.requestBody ? (
                  <pre className="font-mono text-xs bg-slate-950 border border-slate-800 rounded-md p-3 overflow-x-auto whitespace-pre-wrap break-words text-emerald-200">
                    {prettyJson(detail.requestBody)}
                  </pre>
                ) : (
                  <p className="text-xs text-slate-500">No request body captured.</p>
                )}
              </div>

              <div>
                <SectionLabel label="Response Body" />
                {detail.responseBody ? (
                  <pre className="font-mono text-xs bg-slate-950 border border-slate-800 rounded-md p-3 overflow-x-auto whitespace-pre-wrap break-words text-amber-200">
                    {prettyJson(detail.responseBody)}
                  </pre>
                ) : (
                  <p className="text-xs text-slate-500">Response body capture is disabled or empty.</p>
                )}
              </div>
            </>
          ) : (
            <div>
              <div className="mb-3 text-xs text-slate-400">
                Showing all events across your APIs sharing Trace ID{' '}
                <span className="font-mono text-emerald-300">{detail.traceId}</span>
              </div>
              {traceLoading ? (
                <div className="text-slate-400 py-8 text-center">Loading trace timeline...</div>
              ) : (
                <div className="space-y-2">
                  {(traceData?.items ?? []).map((item: LogSummaryDto) => (
                    <button
                      key={item.id}
                      onClick={() => {
                        onSelectLog(item.id);
                        setActiveTab('details');
                      }}
                      className={`w-full text-left p-3 rounded-lg border transition-colors cursor-pointer ${
                        item.id === detail.id
                          ? 'bg-emerald-950/30 border-emerald-700/60'
                          : 'bg-slate-950/70 border-slate-800 hover:border-slate-700'
                      }`}
                    >
                      <div className="flex items-center justify-between gap-2 text-xs">
                        <div className="flex items-center gap-2">
                          <span className="font-semibold text-slate-200">{item.applicationName}</span>
                          <span className="px-1.5 py-0.5 rounded bg-slate-800 text-slate-300 text-[11px]">
                            {item.eventType}
                          </span>
                          <span className="text-slate-400">{item.level}</span>
                        </div>
                        <div className="flex items-center gap-2 font-mono text-slate-400">
                          {item.elapsedMs != null && <span>{item.elapsedMs.toFixed(0)}ms</span>}
                          <span>{new Date(item.timestamp).toLocaleTimeString()}</span>
                        </div>
                      </div>
                      <div className="mt-1 font-mono text-xs text-slate-300 truncate">
                        {item.httpMethod && (
                          <span className="text-sky-400 mr-1.5">{item.httpMethod}</span>
                        )}
                        {item.targetHost
                          ? `${item.targetHost}${item.requestPath ?? ''}${item.queryString ?? ''}`
                          : item.requestPath
                            ? `${item.requestPath}${item.queryString ?? ''}`
                            : item.message}
                      </div>
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
    </aside>
  );
}

function SectionLabel({ label }: { label: string }) {
  return <div className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-1.5">{label}</div>;
}

function MetaField({ label, value }: { label: string; value?: string | null }) {
  return (
    <div className="min-w-0">
      <div className="text-[11px] text-slate-500">{label}</div>
      <div className="text-slate-200 font-medium truncate mt-0.5">{value || '—'}</div>
    </div>
  );
}
