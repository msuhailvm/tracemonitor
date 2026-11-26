export interface LoginResponse {
  token: string;
  expiresAt: string;
  username: string;
  role: string;
}

export interface ApplicationDto {
  id: string;
  name: string;
  slug: string;
  environment: string;
  apiKeyPrefix: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateApplicationResponse {
  application: ApplicationDto;
  apiKey: string;
}

export interface ApplicationHealthDto {
  id: string;
  name: string;
  slug: string;
  environment: string;
  apiKeyPrefix: string;
  isActive: boolean;
  lastHeartbeatAt: string | null;
  cpuPercent: number | null;
  workingSetMb: number | null;
  gcHeapMb: number | null;
  threadCount: number | null;
  machineName: string | null;
  processId: number | null;
  status: 'Online' | 'Stale' | 'Unknown';
}

export interface LogSummaryDto {
  id: number;
  applicationId: string;
  applicationName: string;
  applicationSlug: string;
  timestamp: string;
  eventType: 'Request' | 'OutgoingHttp' | 'Log' | 'Exception' | 'Lifecycle' | string;
  level: 'Debug' | 'Information' | 'Warning' | 'Error' | 'Critical' | string;
  traceId: string | null;
  environment: string | null;
  httpMethod: string | null;
  requestPath: string | null;
  queryString: string | null;
  targetHost: string | null;
  statusCode: number | null;
  elapsedMs: number | null;
  category: string | null;
  message: string | null;
  exceptionType: string | null;
}

export interface LogDetailDto extends LogSummaryDto {
  spanId: string | null;
  queryString: string | null;
  clientIp: string | null;
  userAgent: string | null;
  exceptionMessage: string | null;
  stackTrace: string | null;
  requestHeadersJson: string | null;
  requestBody: string | null;
  responseBody: string | null;
  propertiesJson: string | null;
}

export interface LogSearchResponse {
  items: LogSummaryDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface TraceLogsResponse {
  traceId: string;
  items: LogSummaryDto[];
}

export interface LogSearchParams {
  q?: string;
  applicationIds?: string[];
  levels?: string[];
  eventTypes?: string[];
  statusRange?: string;
  from?: string;
  to?: string;
  traceId?: string;
  page?: number;
  pageSize?: number;
}

// ─── Phase 2 Analytics Types ──────────────────────────────────

export interface AnalyticsFilterParams {
  applicationIds?: string[];
  from?: string;
  to?: string;
  interval?: string;
}

export interface VolumeAnalyticsParams extends Omit<LogSearchParams, 'page' | 'pageSize'> {
  groupBy?: 'level' | 'app';
  interval?: string;
}

export interface VolumeBucketDto {
  timestamp: string;
  total: number;
  counts: Record<string, number>;
}

export interface VolumeAnalyticsResponse {
  bucketSeconds: number;
  from: string;
  to: string;
  groupBy: 'level' | 'app' | string;
  totalEvents: number;
  buckets: VolumeBucketDto[];
}

export interface ErrorRateSummaryDto {
  totalRequests: number;
  totalEvents: number;
  totalErrors: number;
  httpErrorCount: number;
  errorRatePercent: number;
}

export interface ErrorRateBucketDto {
  timestamp: string;
  requests: number;
  errors: number;
  errorRatePercent: number;
}

export interface AppErrorBreakdownDto {
  applicationId: string;
  applicationName: string;
  applicationSlug: string;
  totalRequests: number;
  totalErrors: number;
  errorRatePercent: number;
}

export interface ErrorRateAnalyticsResponse {
  bucketSeconds: number;
  from: string;
  to: string;
  summary: ErrorRateSummaryDto;
  timeline: ErrorRateBucketDto[];
  byApplication: AppErrorBreakdownDto[];
}

export interface LatencySummaryDto {
  sampleCount: number;
  avgMs: number;
  p50Ms: number;
  p95Ms: number;
  p99Ms: number;
}

export interface LatencyBucketDto {
  timestamp: string;
  count: number;
  avgMs: number;
  p50Ms: number;
  p95Ms: number;
  p99Ms: number;
}

export interface SlowEndpointDto {
  applicationId: string;
  applicationName: string;
  applicationSlug: string;
  httpMethod: string | null;
  targetHost: string | null;
  requestPath: string;
  count: number;
  errorCount: number;
  avgMs: number;
  p95Ms: number;
  maxMs: number;
}

export interface LatencyAnalyticsResponse {
  bucketSeconds: number;
  from: string;
  to: string;
  summary: LatencySummaryDto;
  timeline: LatencyBucketDto[];
  slowestEndpoints: SlowEndpointDto[];
  slowestDependencies: SlowEndpointDto[];
}

export interface ResourcePointDto {
  timestamp: string;
  cpuPercent: number;
  workingSetMb: number;
  gcHeapMb: number;
  threadCount: number;
  pendingWorkItems: number;
  gen2Collections: number;
}

export interface AppResourceSeriesDto {
  applicationId: string;
  applicationName: string;
  applicationSlug: string;
  points: ResourcePointDto[];
}

export interface ResourceAnalyticsResponse {
  from: string;
  to: string;
  series: AppResourceSeriesDto[];
}

export interface TopExceptionDto {
  applicationId: string;
  applicationName: string;
  applicationSlug: string;
  exceptionType: string;
  requestPath: string | null;
  sampleMessage: string | null;
  count: number;
  firstSeen: string;
  lastSeen: string;
}

export interface TopExceptionsResponse {
  from: string;
  to: string;
  items: TopExceptionDto[];
}
