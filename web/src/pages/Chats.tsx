import { useCallback, useEffect, useLayoutEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { api, type MatchSummary, type Message, type Profile } from '../api';
import {
  Avatar,
  Chips,
  Empty,
  ErrorBanner,
  errorMessage,
  GENDERS,
  Modal,
  PhotoStack,
  ReportDialog,
  Spinner,
  timeAgo,
} from '../components';
import { useRealtime, useRealtimeEvent } from '../realtime';
import { useMe } from '../session';

export function Chats() {
  const [matches, setMatches] = useState<MatchSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(
    () =>
      api
        .matches()
        .then(setMatches)
        .catch((e) => setError(errorMessage(e))),
    [],
  );

  useEffect(() => {
    load();
  }, [load]);

  useRealtimeEvent('message', load);
  useRealtimeEvent('match', load);
  useRealtimeEvent('unmatched', load);

  if (matches === null) return error ? <ErrorBanner error={error} /> : <Spinner />;

  const fresh = matches.filter((m) => !m.lastMessage);
  const conversations = matches.filter((m) => m.lastMessage);

  return (
    <div className="page">
      <h1 className="page-title">Matches</h1>
      <ErrorBanner error={error} />
      {matches.length === 0 ? (
        <Empty icon="💬" title="No matches yet">
          Keep swiping. When someone likes you back, you can chat here.
        </Empty>
      ) : (
        <>
          {fresh.length > 0 && (
            <section>
              <h3 className="section-title">New matches</h3>
              <div className="fresh-matches">
                {fresh.map((m) => (
                  <Link key={m.matchId} to={`/chats/${m.matchId}`} className="fresh-match">
                    <Avatar name={m.displayName} url={m.photoUrls[0]} size={68} />
                    <span>{m.displayName}</span>
                  </Link>
                ))}
              </div>
            </section>
          )}
          {conversations.length > 0 && (
            <section>
              <h3 className="section-title">Messages</h3>
              <ul className="conversation-list">
                {conversations.map((m) => (
                  <li key={m.matchId}>
                    <Link to={`/chats/${m.matchId}`} className={m.unreadCount ? 'conversation unread' : 'conversation'}>
                      <Avatar name={m.displayName} url={m.photoUrls[0]} size={56} />
                      <div className="conversation-text">
                        <div className="row between">
                          <strong>{m.displayName}</strong>
                          <span className="muted small">{m.lastMessageAt && timeAgo(m.lastMessageAt)}</span>
                        </div>
                        <div className="row between">
                          <span className="preview">{m.lastMessage}</span>
                          {m.unreadCount > 0 && <span className="badge">{m.unreadCount}</span>}
                        </div>
                      </div>
                    </Link>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </>
      )}
    </div>
  );
}

export function Chat() {
  const { matchId = '' } = useParams();
  const me = useMe();
  const navigate = useNavigate();
  const [summary, setSummary] = useState<MatchSummary | null>(null);
  const [profile, setProfile] = useState<Profile | null>(null);
  const [messages, setMessages] = useState<Message[] | null>(null);
  const [draft, setDraft] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ended, setEnded] = useState(false);
  const [theyAreTyping, setTheyAreTyping] = useState(false);
  const [menu, setMenu] = useState(false);
  const [showProfile, setShowProfile] = useState(false);
  const [reporting, setReporting] = useState(false);
  const bottom = useRef<HTMLDivElement>(null);
  const lastTypingSent = useRef(0);
  const typingTimer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const { typing } = useRealtime();

  const markRead = useCallback(async () => {
    try {
      setMessages(await api.messages(matchId));
      // The tab bar's unread badge just went stale.
      window.dispatchEvent(new Event('spark:unread-changed'));
    } catch (e) {
      setError(errorMessage(e));
    }
  }, [matchId]);

  useEffect(() => {
    setMessages(null);
    setEnded(false);
    markRead();
    api
      .matches()
      .then((all) => {
        const found = all.find((m) => m.matchId === matchId) ?? null;
        setSummary(found);
        // Not in the active list: unmatched or blocked. The history stays readable.
        if (!found) setEnded(true);
        else api.user(found.userId).then(setProfile).catch(() => {});
      })
      .catch(() => {});
  }, [matchId, markRead]);

  useLayoutEffect(() => {
    bottom.current?.scrollIntoView({ block: 'end' });
  }, [messages, theyAreTyping]);

  useRealtimeEvent('message', (e) => {
    if (e.matchId !== matchId) return;
    setTheyAreTyping(false);
    setMessages((list) => (list && !list.some((m) => m.id === e.message.id) ? [...list, e.message] : list));
    // Reading it while the thread is open: tell the server, which tells the sender.
    if (e.message.senderId !== me.id && document.visibilityState === 'visible') markRead();
  });

  useRealtimeEvent('read', (e) => {
    if (e.matchId !== matchId) return;
    setMessages((list) =>
      list?.map((m) => (m.senderId === me.id && !m.readAt ? { ...m, readAt: e.readAt } : m)) ?? list,
    );
  });

  useRealtimeEvent('typing', (e) => {
    if (e.matchId !== matchId) return;
    setTheyAreTyping(true);
    clearTimeout(typingTimer.current);
    typingTimer.current = setTimeout(() => setTheyAreTyping(false), 4000);
  });

  useRealtimeEvent('unmatched', (e) => {
    if (e.matchId === matchId) setEnded(true);
  });

  // Catch up on anything that arrived while the tab was in the background.
  useEffect(() => {
    const onVisible = () => document.visibilityState === 'visible' && markRead();
    document.addEventListener('visibilitychange', onVisible);
    return () => document.removeEventListener('visibilitychange', onVisible);
  }, [markRead]);

  const onDraft = (value: string) => {
    setDraft(value);
    const now = Date.now();
    if (value && now - lastTypingSent.current > 2500) {
      lastTypingSent.current = now;
      typing(matchId);
    }
  };

  const send = async (e: FormEvent) => {
    e.preventDefault();
    const body = draft.trim();
    if (!body || sending) return;
    setSending(true);
    setError(null);
    try {
      const sent = await api.send(matchId, body);
      setDraft('');
      setMessages((list) => (list && !list.some((m) => m.id === sent.id) ? [...list, sent] : list));
    } catch (err) {
      setError(errorMessage(err));
      if (/no longer active|cannot message/i.test(errorMessage(err))) setEnded(true);
    } finally {
      setSending(false);
    }
  };

  const unmatch = async () => {
    if (!confirm(`Unmatch ${summary?.displayName}? You won't be able to message each other again.`)) return;
    await api.unmatch(matchId).catch(() => {});
    navigate('/chats');
  };

  const block = async () => {
    if (!summary || !confirm(`Block ${summary.displayName}? They won't be able to see or message you.`)) return;
    await api.block(summary.userId).catch(() => {});
    navigate('/chats');
  };

  const name = summary?.displayName ?? 'Conversation';
  const lastMine = messages?.filter((m) => m.senderId === me.id).at(-1);

  return (
    <div className="chat">
      <header className="chat-header">
        <Link to="/chats" className="icon-button" aria-label="Back to matches">
          ←
        </Link>
        <button className="chat-who" onClick={() => profile && setShowProfile(true)} disabled={!profile}>
          <Avatar name={name} url={summary?.photoUrls[0]} size={40} />
          <div>
            <strong>{name}</strong>
            {summary && summary.compatibilityPercent > 0 && (
              <span className="muted small">{summary.compatibilityPercent}% in common</span>
            )}
          </div>
        </button>
        {summary && (
          <div className="menu-wrap">
            <button className="icon-button" aria-label="More" onClick={() => setMenu(!menu)}>
              ⋯
            </button>
            {menu && (
              <div className="menu" onClick={() => setMenu(false)}>
                {profile && <button onClick={() => setShowProfile(true)}>View profile</button>}
                <button onClick={unmatch}>Unmatch</button>
                <button onClick={block}>Block</button>
                <button className="danger-text" onClick={() => setReporting(true)}>
                  Report
                </button>
              </div>
            )}
          </div>
        )}
      </header>

      <div className="messages">
        {messages === null ? (
          <Spinner />
        ) : messages.length === 0 ? (
          <div className="chat-intro">
            <Avatar name={name} url={summary?.photoUrls[0]} size={88} />
            <p>You matched with {name}.</p>
            <p className="muted small">Say hi! Ask about something in their profile.</p>
          </div>
        ) : (
          messages.map((m) => (
            <div key={m.id} className={m.senderId === me.id ? 'bubble mine' : 'bubble theirs'}>
              {m.body}
              {m.id === lastMine?.id && <span className="receipt">{m.readAt ? 'Read' : 'Sent'}</span>}
            </div>
          ))
        )}
        {theyAreTyping && <div className="bubble theirs typing">typing…</div>}
        <div ref={bottom} />
      </div>

      <ErrorBanner error={error} />
      {ended ? (
        <div className="chat-ended">This conversation has ended.</div>
      ) : (
        <form className="composer" onSubmit={send}>
          <input
            placeholder="Type a message"
            value={draft}
            maxLength={2000}
            onChange={(e) => onDraft(e.target.value)}
            aria-label="Message"
            autoFocus
          />
          <button className="primary" disabled={!draft.trim() || sending} aria-label="Send">
            ➤
          </button>
        </form>
      )}

      {showProfile && profile && (
        <Modal onClose={() => setShowProfile(false)}>
          <div className="profile-sheet">
            <PhotoStack name={profile.displayName} urls={profile.photoUrls} />
            <h2>
              {profile.displayName}, {profile.age}
            </h2>
            <p className="muted">{GENDERS[profile.gender] ?? profile.gender}</p>
            {profile.bio && <p>{profile.bio}</p>}
            <Chips items={profile.interests} highlight={me.interests} />
          </div>
        </Modal>
      )}
      {reporting && summary && (
        <ReportDialog
          userId={summary.userId}
          name={summary.displayName}
          onClose={() => setReporting(false)}
          onDone={(blocked) => {
            setReporting(false);
            if (blocked) navigate('/chats');
          }}
        />
      )}
    </div>
  );
}
