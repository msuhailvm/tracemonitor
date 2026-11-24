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
