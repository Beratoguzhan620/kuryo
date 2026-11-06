import { Navigation, Radar, Timer } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { Brand } from "../components/Shell";
import { useApp } from "../context";
import { linkProps, navigate } from "../router";

function Intro() {
  return (
    <aside className="auth-intro">
      <Brand light />
      <h2>Every order to the nearest free courier, and every courier on one live map</h2>
      <ul>
        <li><Timer size={18} /><span>Orders are offered to the closest courier for a few seconds, then to the next, until someone takes them.</span></li>
        <li><Radar size={18} /><span>Dispatchers watch couriers move and step in when nobody takes an order.</span></li>
        <li><Navigation size={18} /><span>Customers follow their delivery on a link and give the courier a code at the door.</span></li>
      </ul>
    </aside>
  );
}

function useAfter(next: string | undefined) {
  const { signedIn } = useApp();
  return (session: Parameters<typeof signedIn>[0]) => {
    signedIn(session);
    navigate(next ?? "/", true);
  };
}

export function SignIn({ next }: { next?: string }) {
  const done = useAfter(next);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      done(await api.login(email, password));
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <div className="auth">
      <Intro />
      <main className="auth-main">
        <form className="auth-card card" onSubmit={submit}>
          <h1>Sign in</h1>
          <label className="field">Email<input className="input" type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus /></label>
          <label className="field">Password<input className="input" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required /></label>
          {error && <p className="form-error" role="alert">{error}</p>}
          <button className="button primary wide" disabled={busy}>{busy ? "Signing in…" : "Sign in"}</button>
          <p className="small muted center-text">New here? <a {...linkProps(`/register${next ? `?next=${encodeURIComponent(next)}` : ""}`)}>Create an account</a></p>
        </form>
      </main>
    </div>
  );
}

export function Register({ next }: { next?: string }) {
  const done = useAfter(next);
  const [form, setForm] = useState({ displayName: "", email: "", password: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [key]: e.target.value });
  const fieldError = (name: string) => error?.fields[name]?.[0];

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      done(await api.register(form));
    } catch (err) {
      setError(err as ApiError);
      setBusy(false);
    }
  }

  return (
    <div className="auth">
      <Intro />
      <main className="auth-main">
        <form className="auth-card card" onSubmit={submit} noValidate>
          <h1>Create an account</h1>
          <label className="field">Your name<input className="input" autoComplete="name" value={form.displayName} onChange={set("displayName")} autoFocus />
            <span className="hint">Shown to your team; customers see your first name only.</span>
            {fieldError("displayName") && <span className="field-error">{fieldError("displayName")}</span>}</label>
          <label className="field">Email<input className="input" type="email" autoComplete="email" value={form.email} onChange={set("email")} />
            {fieldError("email") && <span className="field-error">{fieldError("email")}</span>}</label>
          <label className="field">Password<input className="input" type="password" autoComplete="new-password" value={form.password} onChange={set("password")} />
            <span className="hint">At least 10 characters.</span>
            {fieldError("password") && <span className="field-error">{fieldError("password")}</span>}</label>
          {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}
          <button className="button primary wide" disabled={busy}>{busy ? "Creating…" : "Create account"}</button>
          <p className="small muted center-text">Have an account? <a {...linkProps(`/signin${next ? `?next=${encodeURIComponent(next)}` : ""}`)}>Sign in</a></p>
        </form>
      </main>
    </div>
  );
}
