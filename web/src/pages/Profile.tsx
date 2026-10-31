import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react';
import { api, type ProfileUpdate } from '../api';
import { ErrorBanner, errorMessage, GENDERS, Modal, PhotoStack, Chips } from '../components';
import { useMe, useSession } from '../session';
import { InterestInput, SeekingInput, useLocationPicker } from './Auth';

const MAX_PHOTOS = 6;

export function Profile() {
  const me = useMe();
  const { logout } = useSession();
  const [preview, setPreview] = useState(false);

  return (
    <div className="page profile">
      <div className="row between">
        <h1 className="page-title">Profile</h1>
        <button className="ghost small" onClick={() => setPreview(true)}>
          Preview
        </button>
      </div>
      <Photos />
      <AboutMe />
      <Preferences />
      <Security />
      <Blocked />
      <section className="card-section">
        <button className="ghost block" onClick={logout}>
          Sign out
        </button>
      </section>
      <DeleteAccount />

      {preview && (
        <Modal onClose={() => setPreview(false)} title="How others see you">
          <div className="profile-sheet">
            <PhotoStack name={me.displayName} urls={me.photoUrls} />
            <h2>
              {me.displayName}, {me.age}
            </h2>
            <p className="muted">{GENDERS[me.gender] ?? me.gender}</p>
            {me.bio && <p>{me.bio}</p>}
            <Chips items={me.interests} />
          </div>
        </Modal>
      )}
    </div>
  );
}

function Photos() {
  const me = useMe();
  const { reload } = useSession();
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const upload = async (e: ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []).slice(0, MAX_PHOTOS - me.photoUrls.length);
    e.target.value = '';
    if (files.length === 0) return;
    setBusy(true);
    setError(null);
    try {
      for (const file of files) {
        if (file.size > 5 * 1024 * 1024) throw new Error(`${file.name} is larger than 5MB.`);
        await api.uploadPhoto(file);
      }
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      await reload();
      setBusy(false);
    }
  };

  const remove = async (url: string) => {
    if (!confirm('Delete this photo?')) return;
    try {
      await api.deletePhoto(url);
      await reload();
    } catch (err) {
      setError(errorMessage(err));
    }
  };

  return (
    <section className="card-section">
      <h3 className="section-title">Photos</h3>
      <div className="photo-grid">
        {me.photoUrls.map((url, i) => (
          <div key={url} className="photo-slot">
            <img src={url} alt={`Photo ${i + 1}`} />
            {i === 0 && <span className="primary-tag">Main</span>}
            <button className="remove" aria-label="Delete photo" onClick={() => remove(url)}>
              ×
            </button>
          </div>
        ))}
        {me.photoUrls.length < MAX_PHOTOS && (
          <button className="photo-slot add" onClick={() => input.current?.click()} disabled={busy}>
            {busy ? '…' : '+'}
          </button>
        )}
      </div>
      <input ref={input} type="file" accept="image/jpeg,image/png,image/webp" multiple hidden onChange={upload} />
      <p className="muted small">
        JPEG, PNG or WebP up to 5MB. Location and camera details are removed from photos when you upload them.
      </p>
      <ErrorBanner error={error} />
    </section>
  );
}

function useSaver() {
  const { setMe } = useSession();
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const save = async (update: ProfileUpdate) => {
    setSaving(true);
    setSaved(false);
    setError(null);
    try {
      setMe(await api.updateMe(update));
      setSaved(true);
      setTimeout(() => setSaved(false), 2000);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setSaving(false);
    }
  };

  const button = (
    <button className="primary" disabled={saving}>
      {saving ? 'Saving…' : saved ? 'Saved ✓' : 'Save'}
    </button>
  );
  return { save, button, error };
}

function AboutMe() {
  const me = useMe();
  const [displayName, setDisplayName] = useState(me.displayName);
  const [bio, setBio] = useState(me.bio);
  const [interests, setInterests] = useState(me.interests);
  const { save, button, error } = useSaver();

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save({ displayName, bio, interests: interests.join(',') });
  };

  return (
    <form className="card-section" onSubmit={submit}>
      <h3 className="section-title">About me</h3>
      <label>
        Name
        <input required maxLength={40} value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
      </label>
      <label>
        Bio
        <textarea maxLength={500} value={bio} onChange={(e) => setBio(e.target.value)} />
      </label>
      <div className="field">
        <span>Interests</span>
        <InterestInput value={interests} onChange={setInterests} />
      </div>
      <p className="muted small">
        {me.age} · {GENDERS[me.gender] ?? me.gender} · {me.email}
      </p>
      <ErrorBanner error={error} />
      <div className="row end">{button}</div>
    </form>
  );
}

function Preferences() {
  const me = useMe();
  const [seeking, setSeeking] = useState(me.seekingGenders);
  const [minAge, setMinAge] = useState(me.minAge);
  const [maxAge, setMaxAge] = useState(me.maxAge);
  const [distance, setDistance] = useState(me.maxDistanceKm);
  const [visible, setVisible] = useState(me.isDiscoverable);
  const { coords, picker } = useLocationPicker([me.latitude, me.longitude]);
  const { save, button, error } = useSaver();

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save({
      seekingGenders: seeking.join(','),
      minAge,
      maxAge: Math.max(minAge, maxAge),
      maxDistanceKm: distance,
      isDiscoverable: visible,
      latitude: coords?.[0],
      longitude: coords?.[1],
    });
  };

  return (
    <form className="card-section" onSubmit={submit}>
      <h3 className="section-title">Discovery</h3>
      <div className="field">
        <span>Show me</span>
        <SeekingInput value={seeking} onChange={setSeeking} />
      </div>
      <div className="field">
        <span>
          Age range: {minAge} – {maxAge}
        </span>
        <div className="row">
          <input type="range" min={18} max={99} value={minAge} onChange={(e) => setMinAge(Math.min(+e.target.value, maxAge))} aria-label="Minimum age" />
          <input type="range" min={18} max={99} value={maxAge} onChange={(e) => setMaxAge(Math.max(+e.target.value, minAge))} aria-label="Maximum age" />
        </div>
      </div>
      <label>
        Maximum distance: {distance} km
        <input type="range" min={1} max={300} value={Math.min(distance, 300)} onChange={(e) => setDistance(+e.target.value)} />
      </label>
      <div className="field">
        <span>Location</span>
        {picker}
      </div>
      <label className="check">
        <input type="checkbox" checked={visible} onChange={(e) => setVisible(e.target.checked)} />
        Show me in discovery
      </label>
      {!visible && <p className="muted small">You're hidden. Your existing matches can still message you.</p>}
      <ErrorBanner error={error} />
      <div className="row end">{button}</div>
    </form>
  );
}

function Security() {
  const { setMe } = useSession();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const change = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      await api.changePassword(current, next);
      setCurrent('');
      setNext('');
      setMessage('Password changed. Other devices have been signed out.');
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  const everywhere = async () => {
    if (!confirm('Sign out on every device, including this one?')) return;
    await api.logoutEverywhere().catch(() => {});
    setMe(null);
  };

  return (
    <form className="card-section" onSubmit={change}>
      <h3 className="section-title">Security</h3>
      <label>
        Current password
        <input type="password" autoComplete="current-password" required value={current} onChange={(e) => setCurrent(e.target.value)} />
      </label>
      <label>
        New password
        <input type="password" autoComplete="new-password" required minLength={8} value={next} onChange={(e) => setNext(e.target.value)} />
      </label>
      <ErrorBanner error={error} />
      {message && <p className="success">{message}</p>}
      <div className="row between">
        <button type="button" className="ghost small" onClick={everywhere}>
          Sign out everywhere
        </button>
        <button className="primary" disabled={busy}>
          Change password
        </button>
      </div>
    </form>
  );
}

function Blocked() {
  const [blocked, setBlocked] = useState<{ id: string; displayName: string }[]>([]);
  useEffect(() => {
    api.blocks().then(setBlocked).catch(() => {});
  }, []);

  if (blocked.length === 0) return null;

  const unblock = async (id: string) => {
    await api.unblock(id).catch(() => {});
    setBlocked((b) => b.filter((u) => u.id !== id));
  };

  return (
    <section className="card-section">
      <h3 className="section-title">Blocked</h3>
      <ul className="plain-list">
        {blocked.map((u) => (
          <li key={u.id} className="row between">
            {u.displayName}
            <button className="ghost small" onClick={() => unblock(u.id)}>
              Unblock
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}

function DeleteAccount() {
  const { setMe } = useSession();
  const [open, setOpen] = useState(false);
  const [confirmText, setConfirmText] = useState('');
  const [error, setError] = useState<string | null>(null);

  const destroy = async () => {
    try {
      await api.deleteMe();
      setMe(null);
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <section className="card-section">
      <button className="danger-text block" onClick={() => setOpen(true)}>
        Delete account
      </button>
      {open && (
        <Modal title="Delete your account?" onClose={() => setOpen(false)}>
          <p>
            This permanently deletes your profile, photos, matches and messages. It can't be undone.
          </p>
          <label>
            Type DELETE to confirm
            <input value={confirmText} onChange={(e) => setConfirmText(e.target.value)} />
          </label>
          <ErrorBanner error={error} />
          <div className="row end">
            <button className="ghost" onClick={() => setOpen(false)}>
              Cancel
            </button>
            <button className="danger" disabled={confirmText !== 'DELETE'} onClick={destroy}>
              Delete forever
            </button>
          </div>
        </Modal>
      )}
    </section>
  );
}
