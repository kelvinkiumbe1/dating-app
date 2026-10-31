import { useCallback, useEffect, useState } from 'react';
import { BrowserRouter, Navigate, NavLink, Outlet, Route, Routes, useLocation, useMatch, useNavigate } from 'react-router';
import { api } from './api';
import { Avatar, Spinner } from './components';
import { Login, Signup, Welcome } from './pages/Auth';
import { Chat, Chats } from './pages/Chats';
import { Discover } from './pages/Discover';
import { Likes } from './pages/Likes';
import { Moderation } from './pages/Moderation';
import { Profile } from './pages/Profile';
import { RealtimeProvider, useRealtimeEvent, type RealtimeEvents } from './realtime';
import { SessionProvider, useSession } from './session';

export function App() {
  return (
    <BrowserRouter basename="/app">
      <SessionProvider>
        <Root />
      </SessionProvider>
    </BrowserRouter>
  );
}

function Root() {
  const { me, loading } = useSession();
  if (loading) return <Spinner />;

  if (!me) {
    return (
      <Routes>
        <Route path="/" element={<Welcome />} />
        <Route path="/login" element={<Login />} />
        <Route path="/signup" element={<Signup />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    );
  }

  return (
    <RealtimeProvider>
      <Routes>
        <Route element={<Shell />}>
          <Route path="/" element={<Discover />} />
          <Route path="/likes" element={<Likes />} />
          <Route path="/chats" element={<Chats />} />
          <Route path="/chats/:matchId" element={<Chat />} />
          <Route path="/profile" element={<Profile />} />
          {me.isAdmin && <Route path="/moderation" element={<Moderation />} />}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Route>
      </Routes>
    </RealtimeProvider>
  );
}

/** Tab bar, unread badge, and the toast for matches and messages arriving elsewhere. */
function Shell() {
  const { me } = useSession();
  const location = useLocation();
  const navigate = useNavigate();
  const inChat = useMatch('/chats/:matchId');
  const [unread, setUnread] = useState(0);
  const [toast, setToast] = useState<{ text: string; to: string; photo?: string; name: string } | null>(null);

  const refreshUnread = useCallback(
    () =>
      api
        .matches()
        .then((m) => setUnread(m.reduce((sum, x) => sum + x.unreadCount, 0)))
        .catch(() => {}),
    [],
  );

  // The badge depends on which thread is open, so recount whenever the route changes.
  useEffect(() => {
    refreshUnread();
  }, [location.pathname, refreshUnread]);

  useEffect(() => {
    window.addEventListener('spark:unread-changed', refreshUnread);
    return () => window.removeEventListener('spark:unread-changed', refreshUnread);
  }, [refreshUnread]);

  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 5000);
    return () => clearTimeout(t);
  }, [toast]);

  const onMessage = (e: RealtimeEvents['message']) => {
    if (e.message.senderId === me!.id) return;
    refreshUnread();
    // The chat list and the open thread already show it; elsewhere, a toast.
    if (inChat?.params.matchId !== e.matchId && location.pathname !== '/chats') {
      api
        .matches()
        .then((all) => {
          const m = all.find((x) => x.matchId === e.matchId);
          if (m) setToast({ name: m.displayName, photo: m.photoUrls[0], text: e.message.body, to: `/chats/${e.matchId}` });
        })
        .catch(() => {});
    }
  };
  useRealtimeEvent('message', onMessage);
  useRealtimeEvent('read', refreshUnread);
  useRealtimeEvent('match', (e) =>
    setToast({ name: e.displayName, photo: e.photoUrls[0], text: `You matched with ${e.displayName}!`, to: `/chats/${e.matchId}` }),
  );

  return (
    <div className={inChat ? 'shell in-chat' : 'shell'}>
      <main>
        <Outlet />
      </main>
      {toast && (
        <button
          className="toast"
          onClick={() => {
            navigate(toast.to);
            setToast(null);
          }}
        >
          <Avatar name={toast.name} url={toast.photo} size={40} />
          <span>
            <strong>{toast.name}</strong>
            <br />
            {toast.text.length > 80 ? `${toast.text.slice(0, 80)}…` : toast.text}
          </span>
        </button>
      )}
      {!inChat && (
        <nav className="tabbar">
          <NavLink to="/" end>
            <span className="tab-icon">🔥</span>Discover
          </NavLink>
          <NavLink to="/likes">
            <span className="tab-icon">💌</span>Likes
          </NavLink>
          <NavLink to="/chats">
            <span className="tab-icon">
              💬{unread > 0 && <span className="tab-badge">{unread > 9 ? '9+' : unread}</span>}
            </span>
            Chats
          </NavLink>
          {me!.isAdmin && (
            <NavLink to="/moderation">
              <span className="tab-icon">🛡️</span>Moderate
            </NavLink>
          )}
          <NavLink to="/profile">
            <span className="tab-icon">👤</span>Profile
          </NavLink>
        </nav>
      )}
    </div>
  );
}
