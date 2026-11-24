import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Check,
  Copy,
  Cpu,
  HardDrive,
  KeyRound,
  Plus,
  RefreshCw,
  Server,
} from 'lucide-react';
import { apiClient } from '../api/client';
import type { ApplicationHealthDto } from '../types/telemetry';

export function ApplicationsPage() {
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [environment, setEnvironment] = useState('Production');
  const [newlyIssuedKey, setNewlyIssuedKey] = useState<{ appName: string; key: string } | null>(null);
  const [copied, setCopied] = useState(false);

  const { data: apps = [], isLoading } = useQuery({
    queryKey: ['applications-health'],
    queryFn: apiClient.getApplicationsHealth,
    refetchInterval: 60_000,
  });

  const createMutation = useMutation({
    mutationFn: () => apiClient.createApplication(name, slug, environment),
    onSuccess: (res) => {
      setNewlyIssuedKey({ appName: res.application.name, key: res.apiKey });
      setName('');
      setSlug('');
      queryClient.invalidateQueries({ queryKey: ['applications-health'] });
      queryClient.invalidateQueries({ queryKey: ['applications'] });
    },
  });

  const rotateMutation = useMutation({
    mutationFn: (id: string) => apiClient.rotateApiKey(id),
    onSuccess: (res) => {
      setNewlyIssuedKey({ appName: res.application.name, key: res.apiKey });
      queryClient.invalidateQueries({ queryKey: ['applications-health'] });
    },
  });

  const handleCreate = (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;
    createMutation.mutate();
  };

  const copyKey = (val: string) => {
    navigator.clipboard.writeText(val);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="max-w-6xl w-full mx-auto p-6 space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold text-slate-100">Applications &amp; Health</h1>
          <p className="text-xs text-slate-400 mt-1">
            Manage API keys for your 5 .NET APIs and monitor 15-minute CPU &amp; Memory heartbeats.
          </p>
        </div>
      </div>

      {/* Newly Generated API Key Banner */}
      {newlyIssuedKey && (
        <div className="bg-emerald-950/50 border border-emerald-600/60 rounded-lg p-4 space-y-2">
          <div className="flex items-center justify-between">
            <div className="text-sm font-medium text-emerald-300">
              API Key generated for <strong>{newlyIssuedKey.appName}</strong> — copy it now (shown only once)
            </div>
            <button
              onClick={() => setNewlyIssuedKey(null)}
              className="text-xs text-slate-400 hover:text-slate-200 cursor-pointer"
            >
              Dismiss
            </button>
          </div>
          <div className="flex items-center justify-between gap-2 bg-slate-950 border border-slate-800 rounded-md px-3 py-2 font-mono text-xs text-emerald-300">
            <span className="break-all">{newlyIssuedKey.key}</span>
            <button
              onClick={() => copyKey(newlyIssuedKey.key)}
              className="shrink-0 px-2.5 py-1 rounded bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-sans flex items-center gap-1 cursor-pointer"
            >
              {copied ? <Check className="w-3.5 h-3.5" /> : <Copy className="w-3.5 h-3.5" />}
              {copied ? 'Copied' : 'Copy'}
            </button>
          </div>
        </div>
      )}

      {/* Register New Application Form */}
      <form
        onSubmit={handleCreate}
        className="bg-slate-900 border border-slate-800 rounded-xl p-4 flex flex-wrap items-end gap-3"
      >
        <div className="flex-1 min-w-[180px]">
          <label className="block text-xs text-slate-400 mb-1">API Display Name</label>
          <input
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Orders API"
            className="w-full bg-slate-950 border border-slate-700 rounded-md px-3 py-2 text-sm text-slate-100 focus:outline-none focus:border-emerald-500"
            required
          />
        </div>
        <div className="w-48">
          <label className="block text-xs text-slate-400 mb-1">Slug (optional)</label>
          <input
            type="text"
            value={slug}
            onChange={(e) => setSlug(e.target.value)}
            placeholder="orders-api"
            className="w-full bg-slate-950 border border-slate-700 rounded-md px-3 py-2 text-sm font-mono text-slate-100 focus:outline-none focus:border-emerald-500"
          />
        </div>
        <div className="w-40">
          <label className="block text-xs text-slate-400 mb-1">Environment</label>
          <select
            value={environment}
            onChange={(e) => setEnvironment(e.target.value)}
            className="w-full bg-slate-950 border border-slate-700 rounded-md px-3 py-2 text-sm text-slate-100 focus:outline-none focus:border-emerald-500"
          >
            <option value="Production">Production</option>
            <option value="Staging">Staging</option>
            <option value="Development">Development</option>
          </select>
        </div>
        <button
          type="submit"
          disabled={createMutation.isPending}
          className="bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white text-sm font-medium px-4 py-2 rounded-md flex items-center gap-1.5 cursor-pointer"
        >
          <Plus className="w-4 h-4" />
          Register API
        </button>
        {createMutation.isError && (
          <div className="w-full text-xs text-rose-400">
            {(createMutation.error as Error).message}
          </div>
        )}
      </form>

      {/* Registered Apps & Live Health Grid */}
      {isLoading ? (
        <div className="text-slate-400 text-sm py-8 text-center">Loading applications...</div>
      ) : apps.length === 0 ? (
        <div className="bg-slate-900/50 border border-slate-800 rounded-xl p-8 text-center text-slate-400 text-sm">
          No APIs registered yet. Register your first .NET API above to generate an ingestion API key.
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {apps.map((app: ApplicationHealthDto) => (
            <div
              key={app.id}
              className="bg-slate-900 border border-slate-800 rounded-xl p-4 flex flex-col justify-between gap-4"
            >
              <div className="flex items-start justify-between gap-3">
                <div className="flex items-center gap-2.5">
                  <div className="w-9 h-9 rounded-lg bg-slate-800 flex items-center justify-center text-slate-300">
                    <Server className="w-4 h-4" />
                  </div>
                  <div>
                    <div className="font-semibold text-slate-100 flex items-center gap-2">
                      {app.name}
                      <HealthPill status={app.status} />
                    </div>
                    <div className="text-xs text-slate-400 font-mono mt-0.5">
                      app:{app.slug} • {app.environment}
                    </div>
                  </div>
                </div>

                <button
                  onClick={() => rotateMutation.mutate(app.id)}
                  className="text-xs text-slate-400 hover:text-slate-100 border border-slate-700 hover:border-slate-600 px-2.5 py-1 rounded-md flex items-center gap-1 cursor-pointer"
                  title="Rotate ingestion API key"
                >
                  <RefreshCw className="w-3 h-3" />
                  Rotate Key
                </button>
              </div>

              {/* Health Metrics */}
              <div className="grid grid-cols-3 gap-2 bg-slate-950/70 border border-slate-800/80 rounded-lg p-3 text-xs">
                <div>
                  <div className="text-slate-500 flex items-center gap-1">
                    <Cpu className="w-3 h-3" /> CPU
                  </div>
                  <div className="font-mono text-slate-200 font-medium mt-0.5">
                    {app.cpuPercent != null ? `${app.cpuPercent.toFixed(1)}%` : '—'}
                  </div>
                </div>
                <div>
                  <div className="text-slate-500 flex items-center gap-1">
                    <HardDrive className="w-3 h-3" /> Memory
                  </div>
                  <div className="font-mono text-slate-200 font-medium mt-0.5">
                    {app.workingSetMb != null ? `${app.workingSetMb.toFixed(0)} MB` : '—'}
                  </div>
                </div>
                <div>
                  <div className="text-slate-500">GC Heap</div>
                  <div className="font-mono text-slate-200 font-medium mt-0.5">
                    {app.gcHeapMb != null ? `${app.gcHeapMb.toFixed(0)} MB` : '—'}
                  </div>
                </div>
              </div>

              <div className="flex items-center justify-between text-[11px] text-slate-500 font-mono">
                <span className="flex items-center gap-1">
                  <KeyRound className="w-3 h-3" />
                  {app.apiKeyPrefix}••••••••
                </span>
                <span>
                  {app.lastHeartbeatAt
                    ? `Heartbeat: ${new Date(app.lastHeartbeatAt).toLocaleString()}`
                    : 'Awaiting first heartbeat'}
                </span>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Quick Integration Snippet */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 space-y-3">
        <h2 className="text-sm font-semibold text-slate-200">
          Quick .NET 8 / .NET 10 Integration (2 Lines in Program.cs)
        </h2>
        <pre className="font-mono text-xs bg-slate-950 border border-slate-800 rounded-lg p-4 overflow-x-auto text-slate-300 leading-relaxed">{`// 1. Reference TraceMonitor.Client (.csproj / .dll / local NuGet)
// 2. Program.cs:
builder.AddTraceMonitor(builder.Configuration.GetSection("TraceMonitor"));
// ...
app.UseTraceMonitor();

// 3. appsettings.json:
"TraceMonitor": {
  "Endpoint": "${apiClient.baseUrl}",
  "ApiKey": "tm_live_YOUR_GENERATED_KEY",
  "ApplicationName": "orders-api",
  "Environment": "Production",
  "MinimumLogLevel": "Warning",
  "CaptureRequestBody": true,
  "CaptureResponseBody": false,
  "CaptureOutgoingHttp": true,
  "CaptureMetrics": true,
  "MetricsIntervalSeconds": 900
}`}</pre>
      </div>
    </div>
  );
}

function HealthPill({ status }: { status: ApplicationHealthDto['status'] }) {
  const styles =
    status === 'Online'
      ? 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30'
      : status === 'Stale'
        ? 'bg-amber-500/15 text-amber-300 border-amber-500/30'
        : 'bg-slate-800 text-slate-400 border-slate-700';

  return (
    <span className={`px-2 py-0.5 rounded-full border text-[10px] font-medium ${styles}`}>
      {status}
    </span>
  );
}
