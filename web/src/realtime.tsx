import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { accessToken, type Message } from './api';

export interface RealtimeEvents {
  message: { matchId: string; message: Message };
  read: { matchId: string; readerId: string; readAt: string };
  match: { matchId: string; userId: string; displayName: string; photoUrls: string[] };
  unmatched: { matchId: string };
  typing: { matchId: string; userId: string };
}

type Handler<K extends keyof RealtimeEvents> = (event: RealtimeEvents[K]) => void;

interface RealtimeValue {
  connected: boolean;
  subscribe<K extends keyof RealtimeEvents>(event: K, handler: Handler<K>): () => void;
  typing(matchId: string): void;
}

const RealtimeContext = createContext<RealtimeValue | null>(null);

const EVENTS: (keyof RealtimeEvents)[] = ['message', 'read', 'match', 'unmatched', 'typing'];

/** One hub connection per signed-in tab, shared by every screen. */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const [connected, setConnected] = useState(false);
  const connectionRef = useRef<HubConnection | null>(null);
  const handlers = useRef(new Map<string, Set<(e: unknown) => void>>());

  useEffect(() => {
    let stopped = false;
    let retry: ReturnType<typeof setTimeout> | undefined;

    const connection = new HubConnectionBuilder()
      // Called on every (re)connect, so a reconnect always presents a fresh token.
      .withUrl('/hubs/chat', { accessTokenFactory: () => accessToken() })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connectionRef.current = connection;

    for (const name of EVENTS) {
      connection.on(name, (payload: unknown) => handlers.current.get(name)?.forEach((h) => h(payload)));
    }

    const start = async () => {
      if (stopped) return;
      try {
        await connection.start();
        setConnected(true);
      } catch {
        retry = setTimeout(start, 5000);
      }
    };

    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(() => setConnected(true));
    // The server closes the socket when the access token expires; reconnect with a new one.
    connection.onclose(() => {
      setConnected(false);
      if (!stopped) retry = setTimeout(start, 1000);
    });

    start();
    return () => {
      stopped = true;
      clearTimeout(retry);
      connection.stop();
    };
  }, []);

  // Stable identities, so subscribers don't resubscribe on every render.
  const subscribe = useCallback<RealtimeValue['subscribe']>((event, handler) => {
    const set = handlers.current.get(event) ?? new Set();
    set.add(handler as (e: unknown) => void);
    handlers.current.set(event, set);
    return () => {
      set.delete(handler as (e: unknown) => void);
    };
  }, []);

  const typing = useCallback((matchId: string) => {
    const c = connectionRef.current;
    if (c?.state === HubConnectionState.Connected) c.invoke('Typing', matchId).catch(() => {});
  }, []);

  const value = useMemo(() => ({ connected, subscribe, typing }), [connected, subscribe, typing]);

  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

export function useRealtime() {
  const value = useContext(RealtimeContext);
  if (!value) throw new Error('useRealtime outside RealtimeProvider');
  return value;
}

/** Subscribes for the component's lifetime; the latest handler is always the one called. */
export function useRealtimeEvent<K extends keyof RealtimeEvents>(event: K, handler: Handler<K>) {
  const { subscribe } = useRealtime();
  const latest = useRef(handler);
  latest.current = handler;
  useEffect(() => subscribe(event, (e) => latest.current(e)), [event, subscribe]);
}
