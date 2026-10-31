// Typed client for DatingApp.Api. Access tokens live only in memory; the refresh token is
// kept in localStorage so a reload doesn't sign you out.

export interface AuthResponse {
  token: string;
  userId: string;
  displayName: string;
  refreshToken: string;
  expiresAt: string;
}

export interface MyProfile {
  id: string;
  email: string;
  displayName: string;
  age: number;
  dateOfBirth: string;
  gender: string;
  bio: string;
  interests: string[];
  latitude: number;
  longitude: number;
  seekingGenders: string[];
  minAge: number;
  maxAge: number;
  maxDistanceKm: number;
  isDiscoverable: boolean;
  photoUrls: string[];
  isAdmin: boolean;
}

export interface Profile {
  id: string;
  displayName: string;
  age: number;
  gender: string;
  bio: string;
  interests: string[];
  photoUrls: string[];
}

export interface Candidate extends Profile {
  compatibilityPercent: number;
  distanceKm: number;
}

export interface SwipeResult {
  isMatch: boolean;
  matchId: string | null;
  message: string;
}

export interface MatchSummary {
  matchId: string;
  userId: string;
  displayName: string;
  age: number;
  compatibilityPercent: number;
  matchedAt: string;
  lastMessage: string | null;
  lastMessageAt: string | null;
  unreadCount: number;
  photoUrls: string[];
}

export interface Message {
  id: string;
  senderId: string;
  body: string;
  sentAt: string;
  readAt: string | null;
}

export interface RegisterInput {
  email: string;
  password: string;
  displayName: string;
  dateOfBirth: string;
  gender: string;
  bio: string;
  interests: string;
  latitude: number;
  longitude: number;
  seekingGenders?: string;
}

export interface ProfileUpdate {
  displayName?: string;
  bio?: string;
  interests?: string;
  latitude?: number;
  longitude?: number;
  seekingGenders?: string;
  minAge?: number;
  maxAge?: number;
  maxDistanceKm?: number;
  isDiscoverable?: boolean;
}

export interface AdminUser {
  id: string;
  displayName: string;
  email: string;
  age: number;
  bio: string;
  photoUrls: string[];
  isBanned: boolean;
  banReason: string | null;
  openReportCount: number;
  totalReportCount: number;
}

export interface AdminReport {
  id: string;
  reason: string;
  details: string | null;
  createdAt: string;
  reviewed: boolean;
  reviewedAt: string | null;
  resolution: string | null;
  moderatorNote: string | null;
  reporterId: string;
  reporterName: string;
  reported: AdminUser | null;
}

export const REPORT_REASONS: Record<string, string> = {
  harassment: 'Harassment',
  spam: 'Spam',
  fake_profile: 'Fake profile',
  inappropriate_photos: 'Inappropriate photos',
  underage: 'Appears under 18',
  scam: 'Scam or money request',
  offline_behavior: 'Behaviour offline',
  other: 'Something else',
};

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

// ── Session ─────────────────────────────────────────────────────────────────

const REFRESH_KEY = 'spark.refreshToken';

let access: { token: string; expiresAt: number } | null = null;
let refreshing: Promise<boolean> | null = null;
let onExpired: () => void = () => {};

export function onSessionExpired(handler: () => void) {
  onExpired = handler;
}

export function hasStoredSession() {
  return localStorage.getItem(REFRESH_KEY) !== null;
}

function storeSession(auth: AuthResponse) {
  access = { token: auth.token, expiresAt: Date.parse(auth.expiresAt) };
  localStorage.setItem(REFRESH_KEY, auth.refreshToken);
}

export function clearSession() {
  access = null;
  localStorage.removeItem(REFRESH_KEY);
}

async function exchange(refreshToken: string) {
  return fetch('/auth/refresh', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  });
}

async function doRefresh(): Promise<boolean> {
  const stored = localStorage.getItem(REFRESH_KEY);
  if (!stored) return false;

  let res = await exchange(stored);

  // Another tab may have rotated the token a moment ago. The server answers our stale copy
  // with a 401 (without killing the session) and the winner has written the new one here.
  if (res.status === 401) {
    const latest = localStorage.getItem(REFRESH_KEY);
    if (latest && latest !== stored) res = await exchange(latest);
  }

  if (!res.ok) {
    clearSession();
    return false;
  }
  storeSession(await res.json());
  return true;
}

/** One refresh at a time per tab; concurrent callers share the result. */
export function refreshSession(): Promise<boolean> {
  refreshing ??= doRefresh().finally(() => (refreshing = null));
  return refreshing;
}

/** A usable access token, refreshing first if it's about to expire. */
export async function accessToken(): Promise<string> {
  if (!access || access.expiresAt - Date.now() < 30_000) {
    if (!(await refreshSession())) throw new ApiError(401, 'Session expired.');
  }
  return access!.token;
}

// ── Requests ────────────────────────────────────────────────────────────────

async function request<T>(method: string, path: string, body?: unknown, auth = true): Promise<T> {
  const send = async () => {
    const headers: Record<string, string> = {};
    if (auth) headers.Authorization = `Bearer ${await accessToken()}`;
    let payload: BodyInit | undefined;
    if (body instanceof FormData) payload = body;
    else if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }
    return fetch(path, { method, headers, body: payload });
  };

  let res: Response;
  try {
    res = await send();
    // The token may have been revoked server-side (password change on another device);
    // one refresh tells us whether the session itself is still alive.
    if (res.status === 401 && auth && (await refreshSession())) res = await send();
  } catch (e) {
    if (e instanceof ApiError) {
      if (e.status === 401) onExpired();
      throw e;
    }
    throw new ApiError(0, 'Network error. Check your connection.');
  }

  if (res.status === 401 && auth) {
    clearSession();
    onExpired();
  }

  if (!res.ok) {
    let message = res.statusText || 'Something went wrong.';
    try {
      const data = await res.json();
      if (data?.error) message = data.error;
      else if (data?.title) message = data.title;
    } catch {
      /* body was not JSON */
    }
    if (res.status === 429) message = 'Too many requests. Wait a minute and try again.';
    throw new ApiError(res.status, message);
  }

  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

const get = <T>(path: string) => request<T>('GET', path);
const post = <T>(path: string, body?: unknown) => request<T>('POST', path, body);
const patch = <T>(path: string, body: unknown) => request<T>('PATCH', path, body);
const del = <T>(path: string) => request<T>('DELETE', path);

export const api = {
  async register(input: RegisterInput) {
    const auth = await request<AuthResponse>('POST', '/auth/register', input, false);
    storeSession(auth);
    return auth;
  },
  async login(email: string, password: string) {
    const auth = await request<AuthResponse>('POST', '/auth/login', { email, password }, false);
    storeSession(auth);
    return auth;
  },
  async logout() {
    const refreshToken = localStorage.getItem(REFRESH_KEY);
    clearSession();
    if (refreshToken) await request('POST', '/auth/logout', { refreshToken }, false).catch(() => {});
  },
  async logoutEverywhere() {
    await post('/auth/logout-all');
    clearSession();
  },
  async changePassword(currentPassword: string, newPassword: string) {
    storeSession(await post<AuthResponse>('/auth/change-password', { currentPassword, newPassword }));
  },

  me: () => get<MyProfile>('/me'),
  updateMe: (update: ProfileUpdate) => patch<MyProfile>('/me', update),
  async deleteMe() {
    await del('/me');
    clearSession();
  },
  user: (id: string) => get<Profile>(`/users/${id}`),

  uploadPhoto(file: File) {
    const form = new FormData();
    form.append('file', file);
    return post<{ id: string; url: string; isPrimary: boolean }>('/me/photos', form);
  },
  deletePhoto: (url: string) => del(`/me${url}`),

  discover: () => get<Candidate[]>('/discover?limit=30'),
  swipe: (targetId: string, isLike: boolean) => post<SwipeResult>('/swipes', { targetId, isLike }),
  likes: () => get<Profile[]>('/likes'),

  matches: () => get<MatchSummary[]>('/matches'),
  messages: (matchId: string) => get<Message[]>(`/matches/${matchId}/messages`),
  send: (matchId: string, body: string) => post<Message>(`/matches/${matchId}/messages`, { body }),
  unmatch: (matchId: string) => del(`/matches/${matchId}`),

  block: (userId: string) => post(`/safety/block/${userId}`),
  unblock: (userId: string) => del(`/safety/block/${userId}`),
  blocks: () => get<{ id: string; displayName: string }[]>('/safety/blocks'),
  report: (userId: string, reason: string, details?: string) =>
    post('/safety/report', { userId, reason, details }),

  admin: {
    reports: (all: boolean) => get<AdminReport[]>(`/admin/reports${all ? '?status=all' : ''}`),
    resolve: (id: string, action: 'dismiss' | 'ban', note?: string) =>
      post(`/admin/reports/${id}/resolve`, { action, note }),
    ban: (userId: string, reason?: string) => post(`/admin/users/${userId}/ban`, { reason }),
    unban: (userId: string) => post(`/admin/users/${userId}/unban`),
  },
};
