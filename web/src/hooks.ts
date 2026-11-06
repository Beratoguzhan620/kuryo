import { useCallback, useEffect, useRef, useState } from "react";

export type Loadable<T> =
  | { state: "loading"; data?: T }
  | { state: "ready"; data: T }
  | { state: "failed"; error: string; data?: T };

/** Loads data for a page, reloads when the dependencies change, and ignores answers that arrive too late. */
export function useLoad<T>(load: () => Promise<T>, deps: unknown[]): [Loadable<T>, () => void, (data: T) => void] {
  const [value, setValue] = useState<Loadable<T>>({ state: "loading" });
  const [attempt, setAttempt] = useState(0);
  const latest = useRef(0);

  useEffect(() => {
    const id = ++latest.current;
    setValue((previous) => ({ state: "loading", data: previous.data }));
    load().then(
      (data) => { if (id === latest.current) setValue({ state: "ready", data }); },
      (error: Error) => { if (id === latest.current) setValue((previous) => ({ state: "failed", error: error.message, data: previous.data })); },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, attempt]);

  const reload = useCallback(() => setAttempt((n) => n + 1), []);
  const set = useCallback((data: T) => setValue({ state: "ready", data }), []);
  return [value, reload, set];
}

/** Waits until typing pauses before passing the value on. */
export function useDebounced<T>(value: T, ms = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return debounced;
}

/** The current time, ticking every second: for countdowns. */
export function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}

/** Whether the browser thinks it has a connection, kept up to date. */
export function useOnline(): boolean {
  const [online, setOnline] = useState(() => navigator.onLine);
  useEffect(() => {
    const up = () => setOnline(true);
    const down = () => setOnline(false);
    window.addEventListener("online", up);
    window.addEventListener("offline", down);
    return () => {
      window.removeEventListener("online", up);
      window.removeEventListener("offline", down);
    };
  }, []);
  return online;
}
