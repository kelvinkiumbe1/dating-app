import { useCallback, useEffect, useRef, useState, type PointerEvent } from 'react';
import { useNavigate } from 'react-router';
import { api, type Candidate } from '../api';
import { Avatar, Chips, Empty, ErrorBanner, errorMessage, Modal, PhotoStack, ReportDialog, Spinner } from '../components';
import { useMe } from '../session';

const SWIPE_THRESHOLD = 110;

export interface MatchCelebration {
  matchId: string;
  name: string;
  photo?: string;
}

export function MatchModal({ match, onClose }: { match: MatchCelebration; onClose: () => void }) {
  const me = useMe();
  const navigate = useNavigate();
  return (
    <Modal onClose={onClose}>
      <div className="match-modal">
        <div className="match-title">It's a match!</div>
        <p>You and {match.name} liked each other.</p>
        <div className="match-avatars">
          <Avatar name={me.displayName} url={me.photoUrls[0]} size={96} />
          <Avatar name={match.name} url={match.photo} size={96} />
        </div>
        <button className="primary block" onClick={() => navigate(`/chats/${match.matchId}`)}>
          Send a message
        </button>
        <button className="ghost block" onClick={onClose}>
          Keep swiping
        </button>
      </div>
    </Modal>
  );
}

export function Discover() {
  const me = useMe();
  const navigate = useNavigate();
  const [deck, setDeck] = useState<Candidate[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [match, setMatch] = useState<MatchCelebration | null>(null);
  const [reporting, setReporting] = useState<Candidate | null>(null);
  const [drag, setDrag] = useState({ x: 0, y: 0, active: false });
  const [leaving, setLeaving] = useState<'left' | 'right' | null>(null);
  const start = useRef({ x: 0, y: 0 });
  const busy = useRef(false);

  const load = useCallback(async () => {
    setError(null);
    try {
      setDeck(await api.discover());
    } catch (e) {
      setError(errorMessage(e));
      setDeck([]);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const top = deck?.[0];

  const decide = useCallback(
    async (like: boolean) => {
      if (!top || busy.current) return;
      busy.current = true;
      setLeaving(like ? 'right' : 'left');

      try {
        const result = await api.swipe(top.id, like);
        if (result.isMatch && result.matchId)
          setMatch({ matchId: result.matchId, name: top.displayName, photo: top.photoUrls[0] });
      } catch (e) {
        // 409 means we already swiped (another tab); dropping the card is still right.
        if (!(e instanceof Error && /already swiped/i.test(e.message))) setError(errorMessage(e));
      }

      // Let the fly-out animation play before the next card takes its place.
      setTimeout(() => {
        setDeck((d) => {
          const rest = d?.slice(1) ?? [];
          if (rest.length < 3) api.discover().then((fresh) => setDeck(fresh)).catch(() => {});
          return rest;
        });
        setLeaving(null);
        setDrag({ x: 0, y: 0, active: false });
        busy.current = false;
      }, 250);
    },
    [top],
  );

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (match || reporting || (e.target as HTMLElement).closest('input, textarea')) return;
      if (e.key === 'ArrowLeft') decide(false);
      if (e.key === 'ArrowRight') decide(true);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [decide, match, reporting]);

  const onPointerDown = (e: PointerEvent) => {
    if ((e.target as HTMLElement).closest('button')) return;
    start.current = { x: e.clientX, y: e.clientY };
    setDrag({ x: 0, y: 0, active: true });
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  };
  const onPointerMove = (e: PointerEvent) => {
    if (!drag.active) return;
    setDrag({ x: e.clientX - start.current.x, y: e.clientY - start.current.y, active: true });
  };
  const onPointerUp = () => {
    if (!drag.active) return;
    if (Math.abs(drag.x) > SWIPE_THRESHOLD) decide(drag.x > 0);
    else setDrag({ x: 0, y: 0, active: false });
  };

  if (deck === null) return <Spinner />;

  const transform = leaving
    ? `translate(${leaving === 'right' ? 150 : -150}%, ${drag.y}px) rotate(${leaving === 'right' ? 25 : -25}deg)`
    : `translate(${drag.x}px, ${drag.y}px) rotate(${drag.x / 18}deg)`;
  const intent = leaving ?? (drag.x > 40 ? 'right' : drag.x < -40 ? 'left' : null);

  return (
    <div className="discover">
      <ErrorBanner error={error} />
      {!top ? (
        <Empty icon="🌍" title="You've seen everyone nearby">
          Widen your distance or age range to see more people, or check back later.
          <br />
          <button className="ghost" style={{ marginTop: 16 }} onClick={() => navigate('/profile')}>
            Adjust preferences
          </button>
        </Empty>
      ) : (
        <>
          <div className="deck">
            {deck[1] && (
              <div className="card behind" aria-hidden>
                <PhotoStack name={deck[1].displayName} urls={deck[1].photoUrls.slice(0, 1)} />
              </div>
            )}
            <div
              key={top.id}
              className={`card ${drag.active ? 'dragging' : ''}`}
              style={{ transform }}
              onPointerDown={onPointerDown}
              onPointerMove={onPointerMove}
              onPointerUp={onPointerUp}
              onPointerCancel={onPointerUp}
            >
              <PhotoStack name={top.displayName} urls={top.photoUrls} />
              {intent === 'right' && <div className="stamp like">LIKE</div>}
              {intent === 'left' && <div className="stamp nope">NOPE</div>}
              <div className="card-info">
                <div className="card-head">
                  <h2>
                    {top.displayName} <span>{top.age}</span>
                  </h2>
                  <button className="icon-button light" aria-label="Report or block" onClick={() => setReporting(top)}>
                    ⋯
                  </button>
                </div>
                <div className="card-meta">
                  <span>📍 {top.distanceKm < 1 ? 'Less than 1 km' : `${top.distanceKm} km`} away</span>
                  {top.compatibilityPercent > 0 && <span>✨ {top.compatibilityPercent}% in common</span>}
                </div>
                {top.bio && <p className="card-bio">{top.bio}</p>}
                <Chips items={top.interests} highlight={me.interests} />
              </div>
            </div>
          </div>
          <div className="actions">
            <button className="round nope" aria-label="Pass" onClick={() => decide(false)}>
              ✕
            </button>
            <button className="round like" aria-label="Like" onClick={() => decide(true)}>
              ♥
            </button>
          </div>
          <p className="muted small center keyboard-hint">Tip: use ← and → on your keyboard</p>
        </>
      )}

      {match && <MatchModal match={match} onClose={() => setMatch(null)} />}
      {reporting && (
        <ReportDialog
          userId={reporting.id}
          name={reporting.displayName}
          onClose={() => setReporting(null)}
          onDone={() => {
            setDeck((d) => d?.filter((c) => c.id !== reporting.id) ?? []);
            setReporting(null);
          }}
        />
      )}
    </div>
  );
}
