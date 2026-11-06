import type { Session } from "./types";

const KEY = "kuryo.session";

/** The session survives a reload in this browser only; storage can be unavailable, so every access is guarded. */
export function loadSession(): Session | null {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return null;
    const session = JSON.parse(raw) as Session;
    return new Date(session.expiresAt).getTime() > Date.now() + 60_000 ? session : null;
  } catch {
    return null;
  }
}

export function saveSession(session: Session | null) {
  try {
    if (session) localStorage.setItem(KEY, JSON.stringify(session));
    else localStorage.removeItem(KEY);
  } catch {
    // Private windows and blocked storage: the session simply lasts until the tab closes.
  }
}
