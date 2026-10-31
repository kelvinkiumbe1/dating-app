import { useEffect, useState } from 'react';
import { api, type Profile } from '../api';
import { Chips, Empty, ErrorBanner, errorMessage, GENDERS, Modal, PhotoStack, Spinner } from '../components';
import { useRealtimeEvent } from '../realtime';
import { useMe } from '../session';
import { MatchModal, type MatchCelebration } from './Discover';

export function Likes() {
  const me = useMe();
  const [likes, setLikes] = useState<Profile[] | null>(null);
  const [open, setOpen] = useState<Profile | null>(null);
  const [match, setMatch] = useState<MatchCelebration | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = () =>
    api
      .likes()
      .then(setLikes)
      .catch((e) => {
        setError(errorMessage(e));
        setLikes([]);
      });

  useEffect(() => {
    load();
  }, []);

  // Someone matching with us elsewhere can remove them from this list.
  useRealtimeEvent('match', () => load());

  const answer = async (person: Profile, like: boolean) => {
    setOpen(null);
    setLikes((l) => l?.filter((p) => p.id !== person.id) ?? []);
    try {
      const result = await api.swipe(person.id, like);
      if (result.isMatch && result.matchId)
        setMatch({ matchId: result.matchId, name: person.displayName, photo: person.photoUrls[0] });
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  if (likes === null) return <Spinner />;

  return (
    <div className="page">
      <h1 className="page-title">Likes you</h1>
      <ErrorBanner error={error} />
      {likes.length === 0 ? (
        <Empty icon="💌" title="No new likes yet">
          When someone likes you, they'll show up here and you can like them back for an instant match.
        </Empty>
      ) : (
        <>
          <p className="muted">These people already liked you. Like them back to match instantly.</p>
          <div className="grid">
            {likes.map((p) => (
              <button key={p.id} className="tile" onClick={() => setOpen(p)}>
                <PhotoStack name={p.displayName} urls={p.photoUrls.slice(0, 1)} />
                <span className="tile-label">
                  {p.displayName}, {p.age}
                </span>
              </button>
            ))}
          </div>
        </>
      )}

      {open && (
        <Modal onClose={() => setOpen(null)}>
          <div className="profile-sheet">
            <PhotoStack name={open.displayName} urls={open.photoUrls} />
            <h2>
              {open.displayName}, {open.age}
            </h2>
            <p className="muted">{GENDERS[open.gender] ?? open.gender}</p>
            {open.bio && <p>{open.bio}</p>}
            <Chips items={open.interests} highlight={me.interests} />
            <div className="actions">
              <button className="round nope" aria-label="Pass" onClick={() => answer(open, false)}>
                ✕
              </button>
              <button className="round like" aria-label="Like back" onClick={() => answer(open, true)}>
                ♥
              </button>
            </div>
          </div>
        </Modal>
      )}
      {match && <MatchModal match={match} onClose={() => setMatch(null)} />}
    </div>
  );
}
