import { useMemo, useState } from 'react';
import { BarChart3, ChevronDown, ChevronUp, RotateCcw } from 'lucide-react';
import type { VolumeAnalyticsResponse, VolumeBucketDto } from '../types/telemetry';

interface EventVolumeHistogramProps {
  data?: VolumeAnalyticsResponse;
  isLoading: boolean;
  isZoomed: boolean;
  onZoomRange: (fromIso: string, toIso: string) => void;
  onResetZoom: () => void;
}

const LEVEL_ORDER = ['Debug', 'Information', 'Warning', 'Error', 'Critical'] as const;

const LEVEL_COLORS: Record<string, string> = {
  Critical: '#f43f5e',
  Error: '#fb7185',
  Warning: '#fbbf24',
  Information: '#38bdf8',
  Debug: '#64748b',
};

function formatBucketSize(seconds: number): string {
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.round(seconds / 60)}m`;
  if (seconds < 86400) return `${Math.round(seconds / 3600)}h`;
  return `${Math.round(seconds / 86400)}d`;
}

function formatAxisLabel(iso: string, bucketSeconds: number): string {
  const d = new Date(iso);
  if (bucketSeconds >= 86400) {
    return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  }
  if (bucketSeconds >= 3600) {
    return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  }
  return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

function formatTooltipRange(iso: string, bucketSeconds: number): string {
  const start = new Date(iso);
  const end = new Date(start.getTime() + bucketSeconds * 1000);
  const sameDay = start.toDateString() === end.toDateString();
  if (sameDay) {
    return `${start.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} ${start.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })} – ${end.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}`;
  }
  return `${start.toLocaleString()} – ${end.toLocaleString()}`;
}

export function EventVolumeHistogram({
  data,
  isLoading,
  isZoomed,
  onZoomRange,
  onResetZoom,
}: EventVolumeHistogramProps) {
  const [collapsed, setCollapsed] = useState(false);
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null);
  const [dragStartIdx, setDragStartIdx] = useState<number | null>(null);
  const [dragCurrentIdx, setDragCurrentIdx] = useState<number | null>(null);

  const buckets = data?.buckets ?? [];
  const bucketSeconds = data?.bucketSeconds ?? 60;

  const maxTotal = useMemo(() => {
    let max = 0;
    for (const b of buckets) {
      if (b.total > max) max = b.total;
    }
    return Math.max(1, max);
  }, [buckets]);

  const selectionRange = useMemo(() => {
    if (dragStartIdx === null || dragCurrentIdx === null) return null;
    return {
      min: Math.min(dragStartIdx, dragCurrentIdx),
      max: Math.max(dragStartIdx, dragCurrentIdx),
    };
  }, [dragStartIdx, dragCurrentIdx]);

  const commitSelection = (startIdx: number, endIdx: number) => {
    if (buckets.length === 0) return;
    const min = Math.max(0, Math.min(startIdx, endIdx));
    const max = Math.min(buckets.length - 1, Math.max(startIdx, endIdx));
    const fromMs = new Date(buckets[min].timestamp).getTime();
    const toMs = new Date(buckets[max].timestamp).getTime() + bucketSeconds * 1000;
    onZoomRange(new Date(fromMs).toISOString(), new Date(toMs).toISOString());
  };

  const handleMouseUp = () => {
    if (dragStartIdx !== null && dragCurrentIdx !== null) {
      commitSelection(dragStartIdx, dragCurrentIdx);
    }
    setDragStartIdx(null);
    setDragCurrentIdx(null);
  };

  const hoveredBucket: VolumeBucketDto | null =
    hoveredIdx !== null && hoveredIdx >= 0 && hoveredIdx < buckets.length ? buckets[hoveredIdx] : null;

  const tickIndices = useMemo(() => {
    if (buckets.length <= 1) return buckets.length === 1 ? [0] : [];
    const tickCount = Math.min(6, buckets.length);
    const indices = new Set<number>();
    for (let i = 0; i < tickCount; i++) {
      indices.add(Math.round((i * (buckets.length - 1)) / (tickCount - 1)));
    }
    return Array.from(indices).sort((a, b) => a - b);
  }, [buckets.length]);

  return (
    <div className="border-b border-slate-800/80 bg-slate-950/90 px-4 py-2 select-none">
      {/* Header row */}
      <div className="flex flex-wrap items-center justify-between gap-2 text-xs">
        <div className="flex items-center gap-2 text-slate-300">
          <button
            type="button"
            onClick={() => setCollapsed((c) => !c)}
            className="flex items-center gap-1.5 hover:text-white cursor-pointer font-medium"
            title={collapsed ? 'Expand volume histogram' : 'Collapse volume histogram'}
          >
            <BarChart3 className="w-3.5 h-3.5 text-emerald-400" />
            <span>Event Volume</span>
            {collapsed ? <ChevronDown className="w-3.5 h-3.5 text-slate-500" /> : <ChevronUp className="w-3.5 h-3.5 text-slate-500" />}
          </button>
          {data && (
            <span className="text-[11px] text-slate-500 font-mono">
              ({formatBucketSize(bucketSeconds)} buckets · drag bars to zoom)
            </span>
          )}
          {isZoomed && (
            <button
              type="button"
              onClick={onResetZoom}
              className="inline-flex items-center gap-1 px-2 py-0.5 rounded bg-emerald-500/15 border border-emerald-500/30 text-emerald-300 hover:bg-emerald-500/25 text-[11px] cursor-pointer"
            >
              <RotateCcw className="w-3 h-3" />
              Reset zoom
            </button>
          )}
        </div>

        {/* Legend */}
        <div className="flex items-center gap-3 text-[11px] text-slate-400">
          {(['Critical', 'Error', 'Warning', 'Information', 'Debug'] as const).map((lvl) => (
            <span key={lvl} className="inline-flex items-center gap-1">
              <span
                className="w-2 h-2 rounded-xs"
                style={{ backgroundColor: LEVEL_COLORS[lvl] }}
              />
              <span className="hidden sm:inline">{lvl === 'Information' ? 'Info' : lvl}</span>
            </span>
          ))}
        </div>
      </div>

      {/* Chart body */}
      {!collapsed && (
        <div
          className="relative mt-2"
          onMouseLeave={() => {
            setHoveredIdx(null);
            if (dragStartIdx !== null) {
              setDragStartIdx(null);
              setDragCurrentIdx(null);
            }
          }}
          onMouseUp={handleMouseUp}
        >
          {isLoading && buckets.length === 0 ? (
            <div className="h-24 flex items-center justify-center text-xs text-slate-500">
              Loading volume histogram...
            </div>
          ) : buckets.length === 0 ? (
            <div className="h-20 flex items-center justify-center text-xs text-slate-500">
              No events in this time range.
            </div>
          ) : (
            <>
              {/* Hover tooltip */}
              {hoveredBucket && (
                <div className="pointer-events-none absolute -top-1 right-0 z-20 rounded-md border border-slate-700 bg-slate-900/95 px-2.5 py-1.5 text-[11px] shadow-lg backdrop-blur">
                  <div className="font-mono text-slate-300">
                    {formatTooltipRange(hoveredBucket.timestamp, bucketSeconds)}
                  </div>
                  <div className="mt-1 flex flex-wrap items-center gap-2 font-mono">
                    <span className="font-semibold text-slate-100">
                      {hoveredBucket.total.toLocaleString()} total
                    </span>
                    {Object.entries(hoveredBucket.counts)
                      .filter(([, count]) => count > 0)
                      .map(([key, count]) => (
                        <span key={key} className="inline-flex items-center gap-1 text-slate-300">
                          <span
                            className="w-1.5 h-1.5 rounded-full"
                            style={{ backgroundColor: LEVEL_COLORS[key] ?? '#38bdf8' }}
                          />
                          {key}: {count.toLocaleString()}
                        </span>
                      ))}
                  </div>
                </div>
              )}

              {/* Stacked Bars */}
              <div className="h-24 flex items-end gap-[2px] border-b border-slate-800/80 pt-3 pb-0.5 cursor-crosshair">
                {buckets.map((bucket, idx) => {
                  const isSelected =
                    selectionRange !== null && idx >= selectionRange.min && idx <= selectionRange.max;
                  const isHovered = hoveredIdx === idx;
                  const totalPct = bucket.total > 0 ? Math.max(4, (bucket.total / maxTotal) * 100) : 0;

                  return (
                    <div
                      key={bucket.timestamp}
                      onMouseDown={(e) => {
                        e.preventDefault();
                        setDragStartIdx(idx);
                        setDragCurrentIdx(idx);
                      }}
                      onMouseEnter={() => {
                        setHoveredIdx(idx);
                        if (dragStartIdx !== null) {
                          setDragCurrentIdx(idx);
                        }
                      }}
                      className={`relative flex-1 h-full flex flex-col justify-end rounded-t-xs transition-colors ${
                        isSelected
                          ? 'bg-emerald-500/20'
                          : isHovered
                            ? 'bg-slate-800/60'
                            : 'bg-transparent'
                      }`}
                    >
                      {bucket.total > 0 && (
                        <div
                          className="w-full flex flex-col-reverse overflow-hidden rounded-t-[2px]"
                          style={{ height: `${totalPct}%` }}
                        >
                          {LEVEL_ORDER.map((lvl) => {
                            const count = bucket.counts[lvl] ?? 0;
                            if (count <= 0) return null;
                            const segPct = (count / bucket.total) * 100;
                            return (
                              <div
                                key={lvl}
                                style={{
                                  height: `${segPct}%`,
                                  minHeight: '2px',
                                  backgroundColor: LEVEL_COLORS[lvl],
                                }}
                                className={isHovered ? 'opacity-100' : 'opacity-85'}
                              />
                            );
                          })}
                          {/* Fallback for any custom/unknown level keys */}
                          {Object.entries(bucket.counts)
                            .filter(([k, c]) => c > 0 && !LEVEL_COLORS[k])
                            .map(([k, c]) => (
                              <div
                                key={k}
                                style={{
                                  height: `${(c / bucket.total) * 100}%`,
                                  minHeight: '2px',
                                  backgroundColor: '#34d399',
                                }}
                              />
                            ))}
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>

              {/* Time Axis Labels */}
              <div className="mt-1 flex items-center justify-between text-[10px] font-mono text-slate-500">
                {tickIndices.map((idx) => (
                  <span key={buckets[idx].timestamp}>
                    {formatAxisLabel(buckets[idx].timestamp, bucketSeconds)}
                  </span>
                ))}
              </div>
            </>
          )}
        </div>
      )}
    </div>
  );
}
