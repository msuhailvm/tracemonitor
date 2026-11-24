import type {
  ApplicationDto,
  ApplicationHealthDto,
  CreateApplicationResponse,
  LogDetailDto,
  LogSearchParams,
  LogSearchResponse,
  LoginResponse,
  TraceLogsResponse,
} from '../types/telemetry';

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5080').replace(/\/$/, '');
const TOKEN_KEY = 'tm_jwt_token';
const USER_KEY = 'tm_user';

export function getStoredToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function getStoredUser(): { username: string; role: string } | null {
  const raw = localStorage.getItem(USER_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

export function saveAuth(res: LoginResponse): void {
  localStorage.setItem(TOKEN_KEY, res.token);
  localStorage.setItem(USER_KEY, JSON.stringify({ username: res.username, role: res.role }));
}

export function clearAuth(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(USER_KEY);
}

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const token = getStoredToken();
  const headers = new Headers(init?.headers);
  if (!headers.has('Content-Type') && init?.body) {
    headers.set('Content-Type', 'application/json');
  }
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const res = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers,
  });

  if (res.status === 401) {
    clearAuth();
    if (!window.location.pathname.startsWith('/login')) {
      window.location.href = '/login';
    }
    throw new Error('Unauthorized');
  }

  if (!res.ok) {
    let message = `Request failed (${res.status})`;
    try {
      const errBody = await res.json();
      if (errBody?.error) message = errBody.error;
    } catch {
      // ignore
    }
    throw new Error(message);
  }

  return res.json() as Promise<T>;
}

export const apiClient = {
  baseUrl: API_BASE_URL,

  login: (username: string, password: string) =>
    apiFetch<LoginResponse>('/api/v1/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  getApplications: () => apiFetch<ApplicationDto[]>('/api/v1/applications'),

  getApplicationsHealth: () => apiFetch<ApplicationHealthDto[]>('/api/v1/applications/health'),

  createApplication: (name: string, slug: string, environment: string) =>
    apiFetch<CreateApplicationResponse>('/api/v1/applications', {
      method: 'POST',
      body: JSON.stringify({ name, slug, environment }),
    }),

  rotateApiKey: (id: string) =>
    apiFetch<CreateApplicationResponse>(`/api/v1/applications/${id}/rotate-key`, {
      method: 'POST',
    }),

  searchLogs: (params: LogSearchParams) => {
    const qs = new URLSearchParams();
    if (params.q) qs.set('q', params.q);
    if (params.applicationIds?.length) qs.set('applicationIds', params.applicationIds.join(','));
    if (params.levels?.length) qs.set('levels', params.levels.join(','));
    if (params.eventTypes?.length) qs.set('eventTypes', params.eventTypes.join(','));
    if (params.statusRange) qs.set('statusRange', params.statusRange);
    if (params.from) qs.set('from', params.from);
    if (params.to) qs.set('to', params.to);
    if (params.traceId) qs.set('traceId', params.traceId);
    if (params.page) qs.set('page', String(params.page));
    if (params.pageSize) qs.set('pageSize', String(params.pageSize));
    return apiFetch<LogSearchResponse>(`/api/v1/logs?${qs.toString()}`);
  },

  getLogDetail: (id: number) => apiFetch<LogDetailDto>(`/api/v1/logs/${id}`),

  getTraceLogs: (traceId: string) =>
    apiFetch<TraceLogsResponse>(`/api/v1/logs/trace/${encodeURIComponent(traceId)}`),
};
