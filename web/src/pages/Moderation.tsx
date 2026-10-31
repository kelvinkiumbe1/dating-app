import { useCallback, useEffect, useState } from 'react';
import { api, REPORT_REASONS, type AdminReport } from '../api';
import { Avatar, Empty, ErrorBanner, errorMessage, Spinner, timeAgo } from '../components';

export function Moderation() {
  const [showAll, setShowAll] = useState(false);
  const [reports, setReports] = useState<AdminReport[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setReports(await api.admin.reports(showAll));
    } catch (e) {
      setError(errorMessage(e));
      setReports([]);
    }
  }, [showAll]);

  useEffect(() => {
    load();
  }, [load]);

  return (
    <div className="page">
      <div className="row between">
        <h1 className="page-title">Moderation</h1>
        <label className="check small">
          <input type="checkbox" checked={showAll} onChange={(e) => setShowAll(e.target.checked)} />
          Include resolved
        </label>
      </div>
      <ErrorBanner error={error} />
      {reports === null ? (
        <Spinner />
      ) : reports.length === 0 ? (
        <Empty icon="🛡️" title="Queue is clear">
          No open reports.
        </Empty>
      ) : (
        <ul className="report-list">
          {reports.map((r) => (
            <ReportCard key={r.id} report={r} onChanged={load} />
          ))}
        </ul>
      )}
    </div>
  );
}

function ReportCard({ report, onChanged }: { report: AdminReport; onChanged: () => void }) {
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const user = report.reported;

  const act = async (fn: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await fn();
      onChanged();
    } catch (e) {
      setError(errorMessage(e));
      setBusy(false);
    }
  };

  return (
    <li className={report.reviewed ? 'report resolved' : 'report'}>
      <div className="report-head">
        <span className="reason-tag">{REPORT_REASONS[report.reason] ?? report.reason}</span>
        <span className="muted small">
          {timeAgo(report.createdAt)} · by {report.reporterName}
        </span>
      </div>
      {report.details && <blockquote>{report.details}</blockquote>}

      {user ? (
        <div className="reported">
          <Avatar name={user.displayName} url={user.photoUrls[0]} size={56} />
          <div>
            <strong>
              {user.displayName}, {user.age}
            </strong>
            {user.isBanned && <span className="banned-tag">Banned</span>}
            <div className="muted small">{user.email}</div>
            <div className="muted small">
              {user.openReportCount} open · {user.totalReportCount} total reports
            </div>
            {user.bio && <p className="small">{user.bio}</p>}
          </div>
        </div>
      ) : (
        <p className="muted">The reported account has been deleted.</p>
      )}

      {user && user.photoUrls.length > 1 && (
        <div className="report-photos">
          {user.photoUrls.map((u) => (
            <a key={u} href={u} target="_blank" rel="noreferrer">
              <img src={u} alt="" />
            </a>
          ))}
        </div>
      )}

      {report.reviewed ? (
        <p className="muted small">
          Resolved: <strong>{report.resolution}</strong>
          {report.moderatorNote && ` — ${report.moderatorNote}`}
          {user?.isBanned && (
            <>
              {' · '}
              <button className="link" disabled={busy} onClick={() => act(() => api.admin.unban(user.id))}>
                Lift ban
              </button>
            </>
          )}
        </p>
      ) : (
        <>
          <input placeholder="Note for the record (optional)" value={note} onChange={(e) => setNote(e.target.value)} />
          <ErrorBanner error={error} />
          <div className="row end">
            <button className="ghost" disabled={busy} onClick={() => act(() => api.admin.resolve(report.id, 'dismiss', note))}>
              Dismiss
            </button>
            <button
              className="danger"
              disabled={busy || !user}
              onClick={() =>
                confirm(`Ban ${user?.displayName}? They are signed out everywhere and all their matches end.`) &&
                act(() => api.admin.resolve(report.id, 'ban', note))
              }
            >
              Ban user
            </button>
          </div>
        </>
      )}
    </li>
  );
}
