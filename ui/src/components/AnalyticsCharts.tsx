import { useEffect, useMemo, useRef, useState } from 'react';
import type { ErrorRateBucketDto } from '../types/telemetry';

export const SERIES_PALETTE = [
  '#34d399', // emerald-400
  '#38bdf8', // sky-400
  '#fbbf24', // amber-400
  '#a78bfa', // violet-400
  '#fb7185', // rose-400
  '#2dd4bf', // teal-400
  '#f97316', // orange-500
];

function formatShortTime(iso: string, spanMs: number): string {
  const d = new Date(iso);
  if (spanMs >= 86_400_000 * 3) {
    return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  }
  if (spanMs >= 86_400_000) {
    return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit' });
  }
  return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

// ─── Stacked Requests & Errors Bar Chart ─────────────────────

interface RequestsErrorsChartProps {
  data: ErrorRateBucketDto[];
  isLoading: boolean;
}

export function RequestsErrorsChart({ data, isLoading }: RequestsErrorsChartProps) {
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null);

  const maxTotal = useMemo(() => {
    let max = 0;
    for (const d of data) {
      const total = Math.max(d.requests, d.errors);
      if (total > max) max = total;
    }
    return Math.max(1, max);
  }, [data]);

  const spanMs = useMemo(() => {
    if (data.length < 2) return 3_600_000;
    return new Date(data[data.length - 1].timestamp).getTime() - new Date(data[0].timestamp).getTime();
  }, [data]);

  const tickIndices = useMemo(() => {
    if (data.length <= 1) return data.length === 1 ? [0] : [];
    const count = Math.min(6, data.length);
    const set = new Set<number>();
    for (let i = 0; i < count; i++) {
      set.add(Math.round((i * (data.length - 1)) / (count - 1)));
    }
    return Array.from(set).sort((a, b) => a - b);
  }, [data.length]);

  const hovered = hoveredIdx !== null ? data[hoveredIdx] : null;

  if (isLoading && data.length === 0) {
    return <ChartPlaceholder text="Loading request & error timeline..." />;
  }
  if (data.length === 0) {
    return <ChartPlaceholder text="No request traffic recorded in this time range." />;
  }

  return (
    <div className="relative" onMouseLeave={() => setHoveredIdx(null)}>
      {hovered && (
        <div className="pointer-events-none absolute top-0 right-0 z-10 rounded-md border border-slate-700 bg-slate-900/95 px-3 py-2 text-xs shadow-lg">
          <div className="font-mono text-slate-300">{new Date(hovered.timestamp).toLocaleString()}</div>
          <div className="mt-1 flex items-center gap-3 font-mono">
            <span className="text-emerald-400">Req: {hovered.requests.toLocaleString()}</span>
            <span className="text-rose-400">Err: {hovered.errors.toLocaleString()}</span>
            <span className="text-slate-300">Rate: {hovered.errorRatePercent.toFixed(2)}%</span>
          </div>
        </div>
      )}

      <div className="h-48 flex items-end gap-[3px] border-b border-slate-800 pt-6 pb-0.5">
        {data.map((bucket, idx) => {
          const total = Math.max(bucket.requests, bucket.errors);
          const okCount = Math.max(0, bucket.requests - bucket.errors);
          const totalPct = total > 0 ? Math.max(3, (total / maxTotal) * 100) : 0;
          const errPct = total > 0 ? (bucket.errors / total) * 100 : 0;
          const okPct = total > 0 ? (okCount / total) * 100 : 0;
          const isHovered = hoveredIdx === idx;

          return (
            <div
              key={bucket.timestamp}
              onMouseEnter={() => setHoveredIdx(idx)}
              className={`flex-1 h-full flex flex-col justify-end rounded-t-xs transition-colors ${
                isHovered ? 'bg-slate-800/70' : ''
              }`}
            >
              {total > 0 && (
                <div
                  className="w-full flex flex-col-reverse overflow-hidden rounded-t-[2px]"
                  style={{ height: `${totalPct}%` }}
                >
                  {okCount > 0 && (
                    <div
                      style={{ height: `${okPct}%`, minHeight: '2px' }}
                      className={isHovered ? 'bg-emerald-400' : 'bg-emerald-500/80'}
                    />
                  )}
                  {bucket.errors > 0 && (
                    <div
                      style={{ height: `${errPct}%`, minHeight: '2px' }}
                      className={isHovered ? 'bg-rose-400' : 'bg-rose-500/90'}
                    />
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>

      <div className="mt-1.5 flex items-center justify-between text-[11px] font-mono text-slate-500">
        {tickIndices.map((idx) => (
          <span key={data[idx].timestamp}>{formatShortTime(data[idx].timestamp, spanMs)}</span>
        ))}
      </div>
    </div>
  );
}

// ─── Multi-Series Responsive SVG Line Chart ──────────────────

export interface LineSeries {
  id: string;
  label: string;
  color: string;
  points: { timestamp: string; value: number }[];
}

interface MultiLineChartProps {
  series: LineSeries[];
  unit: string;
  isLoading: boolean;
  emptyText: string;
  minHeight?: number;
  fillHeight?: boolean;
}

export function MultiLineChart({
  series,
  unit,
  isLoading,
  emptyText,
  minHeight = 200,
  fillHeight = false,
}: MultiLineChartProps) {
  const [hoveredTime, setHoveredTime] = useState<number | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [size, setSize] = useState<{ width: number; height: number }>({
    width: 640,
    height: minHeight,
  });

  const nonEmptySeries = useMemo(() => series.filter((s) => s.points.length > 0), [series]);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    const update = () => {
      const rect = el.getBoundingClientRect();
      if (rect.width > 0 && rect.height > 0) {
        setSize((prev) =>
          Math.abs(prev.width - rect.width) > 1 || Math.abs(prev.height - rect.height) > 1
            ? { width: Math.round(rect.width), height: Math.round(rect.height) }
            : prev
        );
      }
    };

    update();
    const observer = new ResizeObserver(update);
    observer.observe(el);
    return () => observer.disconnect();
  }, [fillHeight, minHeight, nonEmptySeries.length]);

  const { minTime, maxTime, maxVal, uniqueTimestamps } = useMemo(() => {
    let minT = Infinity;
    let maxT = -Infinity;
    let maxV = 0;
    const tsSet = new Set<number>();

    for (const s of nonEmptySeries) {
      for (const pt of s.points) {
        const t = new Date(pt.timestamp).getTime();
        tsSet.add(t);
        if (t < minT) minT = t;
        if (t > maxT) maxT = t;
        if (pt.value > maxV) maxV = pt.value;
      }
    }

    if (!Number.isFinite(minT)) {
      return { minTime: 0, maxTime: 1, maxVal: 1, uniqueTimestamps: [] as number[] };
    }

    if (minT === maxT) {
      minT -= 60_000;
      maxT += 60_000;
    }

    return {
      minTime: minT,
      maxTime: maxT,
      maxVal: maxV > 0 ? maxV * 1.15 : 1,
      uniqueTimestamps: Array.from(tsSet).sort((a, b) => a - b),
    };
  }, [nonEmptySeries]);

  const hoveredValues = useMemo(() => {
    if (hoveredTime === null) return [];
    return nonEmptySeries
      .map((s) => {
        let best: { timestamp: string; value: number } | null = null;
        let bestDiff = Infinity;
        for (const pt of s.points) {
          const diff = Math.abs(new Date(pt.timestamp).getTime() - hoveredTime);
          if (diff < bestDiff) {
            bestDiff = diff;
            best = pt;
          }
        }
        return best ? { label: s.label, color: s.color, value: best.value } : null;
      })
      .filter((x): x is { label: string; color: string; value: number } => x !== null);
  }, [hoveredTime, nonEmptySeries]);

  if (isLoading && nonEmptySeries.length === 0) {
    return <ChartPlaceholder text="Loading chart..." />;
  }
  if (nonEmptySeries.length === 0) {
    return <ChartPlaceholder text={emptyText} />;
  }

  const W = Math.max(240, size.width);
  const H = Math.max(minHeight, size.height);
  const padL = 42;
  const padR = 14;
  const padT = 10;
  const padB = 22;
  const plotW = W - padL - padR;
  const plotH = H - padT - padB;

  const xOf = (t: number) => padL + ((t - minTime) / (maxTime - minTime)) * plotW;
  const yOf = (v: number) => padT + plotH - (v / maxVal) * plotH;

  const yTicks = [0, 0.25, 0.5, 0.75, 1].map((frac) => ({
    y: padT + plotH * (1 - frac),
    val: maxVal * frac,
  }));

  const spanMs = maxTime - minTime;
  const xTicks = [0, 0.25, 0.5, 0.75, 1].map((frac) => {
    const t = minTime + spanMs * frac;
    return { x: xOf(t), label: formatShortTime(new Date(t).toISOString(), spanMs) };
  });

  // Find closest timestamp to pointer
  const handleMouseMove = (e: React.MouseEvent<SVGSVGElement>) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const relX = ((e.clientX - rect.left) / rect.width) * W;
    if (relX < padL || relX > W - padR || uniqueTimestamps.length === 0) {
      setHoveredTime(null);
      return;
    }
    const targetTime = minTime + ((relX - padL) / plotW) * (maxTime - minTime);
    let closest = uniqueTimestamps[0];
    let minDiff = Math.abs(closest - targetTime);
    for (const t of uniqueTimestamps) {
      const diff = Math.abs(t - targetTime);
      if (diff < minDiff) {
        minDiff = diff;
        closest = t;
      }
    }
    setHoveredTime(closest);
  };

  return (
    <div
      className={`relative flex flex-col ${fillHeight ? 'flex-1 min-h-0' : ''}`}
      onMouseLeave={() => setHoveredTime(null)}
    >
      {/* Legend */}
      <div className="flex flex-wrap items-center gap-3 mb-2 text-[11px] text-slate-300 shrink-0">
        {nonEmptySeries.map((s) => (
          <span key={s.id} className="inline-flex items-center gap-1.5">
            <span className="w-2.5 h-0.5 rounded-full" style={{ backgroundColor: s.color }} />
            <span>{s.label}</span>
          </span>
        ))}
      </div>

      {/* Hover tooltip */}
      {hoveredTime !== null && hoveredValues.length > 0 && (
        <div className="pointer-events-none absolute top-6 right-2 z-10 rounded-md border border-slate-700 bg-slate-900/95 px-2.5 py-1.5 text-[11px] shadow-lg">
          <div className="font-mono text-slate-400 mb-1">
            {new Date(hoveredTime).toLocaleString()}
          </div>
          <div className="space-y-0.5 font-mono">
            {hoveredValues.map((item) => (
              <div key={item.label} className="flex items-center justify-between gap-3">
                <span className="inline-flex items-center gap-1.5 text-slate-300">
                  <span className="w-2 h-2 rounded-full" style={{ backgroundColor: item.color }} />
                  {item.label}
                </span>
                <span className="font-semibold text-slate-100">
                  {item.value.toFixed(item.value >= 100 ? 0 : 1)}
                  {unit}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      <div
        ref={containerRef}
        className={`w-full ${fillHeight ? 'flex-1 min-h-0' : ''}`}
        style={fillHeight ? { minHeight: `${minHeight}px` } : { height: `${minHeight}px` }}
      >
        <svg
          width={W}
          height={H}
          viewBox={`0 0 ${W} ${H}`}
          className="w-full h-full block overflow-visible"
          onMouseMove={handleMouseMove}
        >
          {/* Horizontal Gridlines & Y labels */}
          {yTicks.map((tick, i) => (
            <g key={i}>
              <line
                x1={padL}
                y1={tick.y}
                x2={W - padR}
                y2={tick.y}
                stroke="var(--chart-grid)"
                strokeWidth="1"
                strokeDasharray={i === 0 ? undefined : '3 3'}
              />
              <text
                x={padL - 6}
                y={tick.y + 3}
                textAnchor="end"
                fill="var(--chart-axis)"
                fontSize="10"
                fontFamily="ui-monospace, monospace"
              >
                {tick.val >= 1000
                  ? `${(tick.val / 1000).toFixed(1)}k`
                  : tick.val >= 10
                    ? Math.round(tick.val)
                    : tick.val.toFixed(1)}
              </text>
            </g>
          ))}

          {/* X Axis Labels */}
          {xTicks.map((tick, i) => (
            <text
              key={i}
              x={tick.x}
              y={H - 4}
              textAnchor={i === 0 ? 'start' : i === xTicks.length - 1 ? 'end' : 'middle'}
              fill="var(--chart-axis)"
              fontSize="10"
              fontFamily="ui-monospace, monospace"
            >
              {tick.label}
            </text>
          ))}

          {/* Series Paths & Points */}
          {nonEmptySeries.map((s) => {
            const coords = s.points.map((pt) => ({
              x: xOf(new Date(pt.timestamp).getTime()),
              y: yOf(pt.value),
            }));
            const pathD = coords
              .map((c, idx) => `${idx === 0 ? 'M' : 'L'} ${c.x.toFixed(1)} ${c.y.toFixed(1)}`)
              .join(' ');

            return (
              <g key={s.id}>
                <path
                  d={pathD}
                  fill="none"
                  stroke={s.color}
                  strokeWidth="2"
                  strokeLinejoin="round"
                  strokeLinecap="round"
                />
                {coords.length <= 40 &&
                  coords.map((c, idx) => (
                    <circle key={idx} cx={c.x} cy={c.y} r="2.5" fill={s.color} />
                  ))}
              </g>
            );
          })}

          {/* Hover Crosshair */}
          {hoveredTime !== null && (
            <line
              x1={xOf(hoveredTime)}
              y1={padT}
              x2={xOf(hoveredTime)}
              y2={padT + plotH}
              stroke="var(--chart-crosshair)"
              strokeWidth="1"
              strokeDasharray="3 3"
            />
          )}
        </svg>
      </div>
    </div>
  );
}

function ChartPlaceholder({ text }: { text: string }) {
  return (
    <div className="h-44 flex items-center justify-center text-xs text-slate-500 border border-dashed border-slate-800 rounded-md">
      {text}
    </div>
  );
}
