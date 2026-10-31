import { useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { ErrorBanner, errorMessage, GENDERS } from '../components';
import { useSession } from '../session';

export function Welcome() {
  return (
    <div className="welcome">
      <div className="welcome-hero">
        <div className="logo">spark</div>
        <h1>Meet people who actually like you back.</h1>
        <p>You only see people whose preferences include you too, so every like has a real chance.</p>
      </div>
      <div className="welcome-actions">
        <Link className="button primary block" to="/signup">
          Create account
        </Link>
        <Link className="button ghost block" to="/login">
          I already have an account
        </Link>
      </div>
    </div>
  );
}

export function Login() {
  const { login } = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(email, password);
    } catch (err) {
      setError(errorMessage(err));
      setBusy(false);
    }
  };

  return (
    <form className="auth-form" onSubmit={submit}>
      <Link to="/" className="back">
        ←
      </Link>
      <h1>Welcome back</h1>
      <label>
        Email
        <input type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      <label>
        Password
        <input
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
      </label>
      <ErrorBanner error={error} />
      <button className="primary block" disabled={busy}>
        {busy ? 'Signing in…' : 'Sign in'}
      </button>
      <p className="muted center">
        New here? <Link to="/signup">Create an account</Link>
      </p>
    </form>
  );
}

const CITIES: Record<string, [number, number]> = {
  Nairobi: [-1.2864, 36.8172],
  Mombasa: [-4.0435, 39.6682],
  Kisumu: [-0.0917, 34.768],
  Nakuru: [-0.3031, 36.08],
  Eldoret: [0.5143, 35.2698],
  Kampala: [0.3476, 32.5825],
  'Dar es Salaam': [-6.7924, 39.2083],
  Lagos: [6.5244, 3.3792],
  London: [51.5074, -0.1278],
  'New York': [40.7128, -74.006],
};

export function useLocationPicker(initial?: [number, number]) {
  const [coords, setCoords] = useState<[number, number] | null>(initial ?? null);
  const [label, setLabel] = useState(initial ? 'Saved location' : '');
  const [locating, setLocating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const locate = () => {
    if (!navigator.geolocation) {
      setError('Your browser cannot share location. Pick a city instead.');
      return;
    }
    setLocating(true);
    setError(null);
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        // Rounded to ~1km: distance matching doesn't need more, and less precision is safer.
        setCoords([+pos.coords.latitude.toFixed(2), +pos.coords.longitude.toFixed(2)]);
        setLabel('Current location (approximate)');
        setLocating(false);
      },
      () => {
        setError('Location permission was denied. Pick a city instead.');
        setLocating(false);
      },
      { enableHighAccuracy: false, timeout: 10000 },
    );
  };

  const pickCity = (city: string) => {
    if (CITIES[city]) {
      setCoords(CITIES[city]);
      setLabel(city);
    }
  };

  const picker = (
    <div className="location">
      <div className="row">
        <button type="button" className="ghost" onClick={locate} disabled={locating}>
          📍 {locating ? 'Locating…' : 'Use my location'}
        </button>
        <select value="" onChange={(e) => pickCity(e.target.value)} aria-label="Pick a city">
          <option value="">or pick a city…</option>
          {Object.keys(CITIES).map((c) => (
            <option key={c}>{c}</option>
          ))}
        </select>
      </div>
      {label && <p className="muted small">✓ {label}</p>}
      <ErrorBanner error={error} />
    </div>
  );

  return { coords, picker };
}

export function InterestInput({ value, onChange }: { value: string[]; onChange: (v: string[]) => void }) {
  const [draft, setDraft] = useState('');
  const add = (raw: string) => {
    const item = raw.trim().toLowerCase();
    if (item && !value.includes(item) && value.length < 12) onChange([...value, item]);
    setDraft('');
  };
  return (
    <div className="interest-input">
      <div className="chips">
        {value.map((i) => (
          <button type="button" key={i} className="chip removable" onClick={() => onChange(value.filter((v) => v !== i))}>
            {i} ×
          </button>
        ))}
      </div>
      <input
        placeholder={value.length ? 'Add another…' : 'e.g. hiking, coffee, music'}
        value={draft}
        onChange={(e) => (e.target.value.endsWith(',') ? add(e.target.value.slice(0, -1)) : setDraft(e.target.value))}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault();
            add(draft);
          }
        }}
        onBlur={() => draft && add(draft)}
      />
    </div>
  );
}

export function SeekingInput({ value, onChange }: { value: string[]; onChange: (v: string[]) => void }) {
  return (
    <div className="row wrap">
      {Object.entries(GENDERS).map(([key, label]) => (
        <label key={key} className={value.includes(key) ? 'toggle on' : 'toggle'}>
          <input
            type="checkbox"
            checked={value.includes(key)}
            onChange={(e) => onChange(e.target.checked ? [...value, key] : value.filter((v) => v !== key))}
          />
          {label}
        </label>
      ))}
    </div>
  );
}

export function Signup() {
  const { register } = useSession();
  const [step, setStep] = useState(0);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [dateOfBirth, setDateOfBirth] = useState('');
  const [gender, setGender] = useState('');
  const [seeking, setSeeking] = useState<string[]>([]);
  const [interests, setInterests] = useState<string[]>([]);
  const [bio, setBio] = useState('');
  const { coords, picker } = useLocationPicker();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const maxDob = new Date();
  maxDob.setFullYear(maxDob.getFullYear() - 18);

  const next = (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (step === 0 && password.length < 8) return setError('Password must be at least 8 characters.');
    if (step === 1 && !gender) return setError('Choose how you identify.');
    setStep(step + 1);
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!coords) return setError('Share your location or pick a city so we can find people near you.');
    setBusy(true);
    setError(null);
    try {
      await register({
        email,
        password,
        displayName,
        dateOfBirth,
        gender,
        bio,
        interests: interests.join(','),
        latitude: coords[0],
        longitude: coords[1],
        seekingGenders: seeking.join(','),
      });
    } catch (err) {
      setError(errorMessage(err));
      setBusy(false);
      // Email problems are on the first step; send the user back there.
      if (/email|password/i.test(errorMessage(err))) setStep(0);
    }
  };

  return (
    <form className="auth-form" onSubmit={step < 2 ? next : submit}>
      {step === 0 ? (
        <Link to="/" className="back">
          ←
        </Link>
      ) : (
        <button type="button" className="back" onClick={() => setStep(step - 1)}>
          ←
        </button>
      )}
      <div className="steps">
        {[0, 1, 2].map((i) => (
          <span key={i} className={i <= step ? 'on' : ''} />
        ))}
      </div>

      {step === 0 && (
        <>
          <h1>Create your account</h1>
          <label>
            Email
            <input type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          </label>
          <label>
            Password
            <input
              type="password"
              autoComplete="new-password"
              required
              minLength={8}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            <span className="hint">At least 8 characters</span>
          </label>
        </>
      )}

      {step === 1 && (
        <>
          <h1>About you</h1>
          <label>
            First name
            <input required maxLength={40} value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
          </label>
          <label>
            Date of birth
            <input
              type="date"
              required
              max={maxDob.toISOString().slice(0, 10)}
              value={dateOfBirth}
              onChange={(e) => setDateOfBirth(e.target.value)}
            />
            <span className="hint">You must be 18 or older. Only your age is shown.</span>
          </label>
          <div className="field">
            <span>I am a</span>
            <div className="row wrap">
              {Object.entries(GENDERS).map(([key, label]) => (
                <label key={key} className={gender === key ? 'toggle on' : 'toggle'}>
                  <input type="radio" name="gender" checked={gender === key} onChange={() => setGender(key)} />
                  {label}
                </label>
              ))}
            </div>
          </div>
          <div className="field">
            <span>Show me</span>
            <SeekingInput value={seeking} onChange={setSeeking} />
            <span className="hint">Leave all unselected to see everyone.</span>
          </div>
        </>
      )}

      {step === 2 && (
        <>
          <h1>Almost there</h1>
          <div className="field">
            <span>Interests</span>
            <InterestInput value={interests} onChange={setInterests} />
            <span className="hint">Shared interests decide who you see first.</span>
          </div>
          <label>
            Bio
            <textarea maxLength={500} value={bio} onChange={(e) => setBio(e.target.value)} placeholder="A line or two about you" />
          </label>
          <div className="field">
            <span>Location</span>
            {picker}
          </div>
        </>
      )}

      <ErrorBanner error={error} />
      <button className="primary block" disabled={busy}>
        {step < 2 ? 'Continue' : busy ? 'Creating account…' : 'Create account'}
      </button>
      {step === 0 && (
        <p className="muted center">
          Have an account? <Link to="/login">Sign in</Link>
        </p>
      )}
    </form>
  );
}
