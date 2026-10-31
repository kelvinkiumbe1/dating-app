import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  api,
  clearSession,
  hasStoredSession,
  onSessionExpired,
  refreshSession,
  type MyProfile,
  type RegisterInput,
} from './api';

interface SessionValue {
  me: MyProfile | null;
  loading: boolean;
  login(email: string, password: string): Promise<void>;
  register(input: RegisterInput): Promise<void>;
  logout(): Promise<void>;
  reload(): Promise<void>;
  setMe(me: MyProfile | null): void;
}

const SessionContext = createContext<SessionValue | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<MyProfile | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(async () => setMe(await api.me()), []);

  useEffect(() => {
    onSessionExpired(() => {
      clearSession();
      setMe(null);
    });

    // Resume a stored session on page load.
    (async () => {
      try {
        if (hasStoredSession() && (await refreshSession())) await reload();
      } catch {
        setMe(null);
      } finally {
        setLoading(false);
      }
    })();
  }, [reload]);

  // Signing out in one tab signs out the others.
  useEffect(() => {
    const onStorage = (e: StorageEvent) => {
      if (e.key === 'spark.refreshToken' && e.newValue === null) setMe(null);
    };
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, []);

  const value = useMemo<SessionValue>(
    () => ({
      me,
      loading,
      setMe,
      reload,
      async login(email, password) {
        await api.login(email, password);
        await reload();
      },
      async register(input) {
        await api.register(input);
        await reload();
      },
      async logout() {
        await api.logout();
        setMe(null);
      },
    }),
    [me, loading, reload],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession() {
  const value = useContext(SessionContext);
  if (!value) throw new Error('useSession outside SessionProvider');
  return value;
}

/** For components that only render when signed in. */
export function useMe() {
  const { me } = useSession();
  if (!me) throw new Error('useMe while signed out');
  return me;
}
