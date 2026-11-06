import { api } from "../api";

export interface Point { latitude: number; longitude: number; at: string }

/** Keeps a point at most this often, so a phone reporting every second does not flood the server. */
export const MIN_GAP_MS = 3000;
/** Points are sent in batches this often; a failed batch stays queued for the next one. */
export const FLUSH_MS = 5000;
/** A courier standing still gets no new positions from the phone; the last one is sent again this often to stay fresh. */
export const HEARTBEAT_MS = 30_000;
/** The server takes this many points a request, and only the newest matter when catching up. */
export const MAX_QUEUE = 50;

/** The queue of positions waiting to be sent. Pure, so it can be tested without a phone. */
export class PointQueue {
  private points: Point[] = [];
  private lastKept = 0;
  /** The newest point skipped for being too soon; it is kept at the next batch unless a newer one was kept meanwhile. */
  private skipped: Point | null = null;

  add(point: Point): boolean {
    const at = new Date(point.at).getTime();
    if (at - this.lastKept < MIN_GAP_MS) {
      this.skipped = point;
      return false;
    }
    this.lastKept = at;
    this.skipped = null;
    this.points.push(point);
    if (this.points.length > MAX_QUEUE) this.points.splice(0, this.points.length - MAX_QUEUE);
    return true;
  }

  /** Before a batch goes out: a courier who moved and then stopped still reports where they stopped. */
  settle(): Point | null {
    const point = this.skipped;
    if (!point) return null;
    this.skipped = null;
    this.lastKept = new Date(point.at).getTime();
    this.points.push(point);
    if (this.points.length > MAX_QUEUE) this.points.splice(0, this.points.length - MAX_QUEUE);
    return point;
  }

  take(): Point[] {
    const batch = this.points;
    this.points = [];
    return batch;
  }

  /** Puts a batch that could not be sent back in front of what came since. */
  restore(batch: Point[]) {
    this.points = [...batch, ...this.points].slice(-MAX_QUEUE);
  }

  get size() {
    return this.points.length;
  }
}

export type TrackerState = "idle" | "waiting" | "sharing" | "denied" | "unavailable";

/**
 * Follows the phone's position while the courier is online and sends it in batches. Returns a function that stops
 * watching and sends what is left.
 */
export function startTracker(companyId: string, onState: (state: TrackerState, last?: Point) => void): () => void {
  if (!("geolocation" in navigator)) {
    onState("unavailable");
    return () => {};
  }

  const queue = new PointQueue();
  let last: Point | undefined;
  let sending = false;
  onState("waiting");

  const flush = async () => {
    if (sending || queue.size === 0) return;
    sending = true;
    const batch = queue.take();
    try {
      await api.ping(companyId, batch);
    } catch {
      queue.restore(batch);
    } finally {
      sending = false;
    }
  };

  const watch = navigator.geolocation.watchPosition(
    (position) => {
      const point = {
        latitude: Math.round(position.coords.latitude * 1e6) / 1e6,
        longitude: Math.round(position.coords.longitude * 1e6) / 1e6,
        at: new Date(position.timestamp || Date.now()).toISOString(),
      };
      const first = !last;
      queue.add(point);
      last = point;
      onState("sharing", last);
      if (first) void flush();
    },
    (error) => onState(error.code === error.PERMISSION_DENIED ? "denied" : "unavailable", last),
    { enableHighAccuracy: true, maximumAge: 5000, timeout: 20000 },
  );
  const timer = setInterval(() => {
    queue.settle();
    if (last && queue.size === 0 && Date.now() - new Date(last.at).getTime() >= HEARTBEAT_MS) {
      const again = { ...last, at: new Date().toISOString() };
      if (queue.add(again)) last = again;
    }
    void flush();
  }, FLUSH_MS);

  return () => {
    navigator.geolocation.clearWatch(watch);
    clearInterval(timer);
    void flush();
  };
}
