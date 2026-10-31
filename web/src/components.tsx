import { useEffect, useState, type ReactNode } from 'react';
import { ApiError, api, REPORT_REASONS } from './api';

export function errorMessage(e: unknown) {
  return e instanceof ApiError || e instanceof Error ? e.message : 'Something went wrong.';
}

/** A stable warm gradient per name, for people without photos. */
function gradientFor(seed: string) {
  let hash = 0;
  for (const ch of seed) hash = (hash * 31 + ch.charCodeAt(0)) | 0;
  const hue = Math.abs(hash) % 360;
  return `linear-gradient(135deg, hsl(${hue} 75% 62%), hsl(${(hue + 40) % 360} 80% 52%))`;
}

export function Avatar({ name, url, size = 48 }: { name: string; url?: string; size?: number }) {
  const style = { width: size, height: size, fontSize: size * 0.42 };
  return url ? (
    <img className="avatar" src={url} alt="" style={style} />
  ) : (
    <div className="avatar avatar-fallback" style={{ ...style, background: gradientFor(name) }} aria-hidden>
      {name.trim().charAt(0).toUpperCase() || '?'}
    </div>
  );
}

/** Full-bleed photo with tap zones to step through several. */
export function PhotoStack({ name, urls }: { name: string; urls: string[] }) {
  const [index, setIndex] = useState(0);
  useEffect(() => setIndex(0), [urls]);

  if (urls.length === 0) {
    return (
      <div className="photo photo-fallback" style={{ background: gradientFor(name) }}>
        <span>{name.charAt(0).toUpperCase()}</span>
      </div>
    );
  }

  return (
    <div className="photo">
      <img src={urls[index]} alt={`${name}, photo ${index + 1} of ${urls.length}`} draggable={false} />
      {urls.length > 1 && (
        <>
          <div className="photo-dots">
            {urls.map((u, i) => (
              <span key={u} className={i === index ? 'on' : ''} />
            ))}
          </div>
          <button className="photo-zone left" aria-label="Previous photo" onClick={() => setIndex((i) => Math.max(0, i - 1))} />
          <button
            className="photo-zone right"
            aria-label="Next photo"
            onClick={() => setIndex((i) => Math.min(urls.length - 1, i + 1))}
          />
        </>
      )}
    </div>
  );
}

export function Chips({ items, highlight = [] }: { items: string[]; highlight?: string[] }) {
  if (items.length === 0) return null;
  return (
    <div className="chips">
      {items.map((i) => (
        <span key={i} className={highlight.includes(i) ? 'chip shared' : 'chip'}>
          {i}
        </span>
      ))}
    </div>
  );
}

export function Modal({ title, onClose, children }: { title?: string; onClose: () => void; children: ReactNode }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" role="dialog" aria-modal aria-label={title} onClick={(e) => e.stopPropagation()}>
        {title && <h2>{title}</h2>}
        {children}
      </div>
    </div>
  );
}

export function Spinner() {
  return <div className="spinner" role="status" aria-label="Loading" />;
}

export function Empty({ icon, title, children }: { icon: string; title: string; children?: ReactNode }) {
  return (
    <div className="empty">
      <div className="empty-icon">{icon}</div>
      <h2>{title}</h2>
      {children && <p>{children}</p>}
    </div>
  );
}

export function ErrorBanner({ error }: { error: string | null }) {
  return error ? (
    <div className="error" role="alert">
      {error}
    </div>
  ) : null;
}

/** Report, optionally also blocking. Calls onDone(blocked) when finished. */
export function ReportDialog({
  userId,
  name,
  onClose,
  onDone,
}: {
  userId: string;
  name: string;
  onClose: () => void;
  onDone: (blocked: boolean) => void;
}) {
  const [reason, setReason] = useState('');
  const [details, setDetails] = useState('');
  const [alsoBlock, setAlsoBlock] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.report(userId, reason, details.trim() || undefined);
      if (alsoBlock) await api.block(userId);
      onDone(alsoBlock);
    } catch (e) {
      setError(errorMessage(e));
      setBusy(false);
    }
  };

  return (
    <Modal title={`Report ${name}`} onClose={onClose}>
      <p className="muted">Reports are confidential. A moderator reviews every one.</p>
      <div className="reason-list">
        {Object.entries(REPORT_REASONS).map(([key, label]) => (
          <label key={key} className={reason === key ? 'reason on' : 'reason'}>
            <input type="radio" name="reason" value={key} checked={reason === key} onChange={() => setReason(key)} />
            {label}
          </label>
        ))}
      </div>
      <textarea
        placeholder="Anything else the moderator should know? (optional)"
        value={details}
        maxLength={1000}
        onChange={(e) => setDetails(e.target.value)}
      />
      <label className="check">
        <input type="checkbox" checked={alsoBlock} onChange={(e) => setAlsoBlock(e.target.checked)} />
        Also block {name}
      </label>
      <ErrorBanner error={error} />
      <div className="row end">
        <button className="ghost" onClick={onClose}>
          Cancel
        </button>
        <button className="danger" disabled={!reason || busy} onClick={submit}>
          {busy ? 'Sending…' : 'Report'}
        </button>
      </div>
    </Modal>
  );
}

export function timeAgo(iso: string) {
  const seconds = Math.max(0, (Date.now() - Date.parse(iso)) / 1000);
  if (seconds < 60) return 'now';
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h`;
  if (seconds < 604800) return `${Math.floor(seconds / 86400)}d`;
  return new Date(iso).toLocaleDateString();
}

export const GENDERS: Record<string, string> = { f: 'Woman', m: 'Man', nb: 'Non-binary' };
