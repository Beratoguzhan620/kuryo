import type { CourierStatus, EventKind, OrderStatus, Role, Vehicle } from "./types";

/** The company works in Istanbul time; screens show its wall clock wherever the browser is. */
export const ZONE = "Europe/Istanbul";

const money = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY", minimumFractionDigits: 2 });
const time = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit", timeZone: ZONE });
const dayTime = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", timeZone: ZONE });

export const formatMoney = (value: number) => money.format(value);
export const formatTime = (iso: string) => time.format(new Date(iso));
export const formatDayTime = (iso: string) => dayTime.format(new Date(iso));
export const km = (value: number) => `${value.toFixed(1)} km`;

export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  return ((words[0]?.[0] ?? "") + (words.length > 1 ? words[words.length - 1]![0] : "")).toLocaleUpperCase("tr-TR") || "?";
}

/** "in 12 min", "3 min ago": how far a moment is from now, roughly. */
export function relative(iso: string, now = Date.now()): string {
  const diff = new Date(iso).getTime() - now;
  const abs = Math.abs(diff);
  if (abs < 45_000) return diff >= 0 ? "now" : "just now";
  const unit = abs < 3_600_000 ? [Math.round(abs / 60_000), "min"] as const : abs < 172_800_000 ? [Math.round(abs / 3_600_000), "h"] as const : [Math.round(abs / 86_400_000), "days"] as const;
  const text = `${Math.max(1, unit[0])} ${unit[1]}`;
  return diff >= 0 ? `in ${text}` : `${text} ago`;
}

/** Whole seconds left until a moment, never below zero. */
export const secondsLeft = (iso: string, now = Date.now()) => Math.max(0, Math.ceil((new Date(iso).getTime() - now) / 1000));

export const statusLabel: Record<OrderStatus, string> = {
  Searching: "Finding a courier", Unassigned: "Needs a courier", Assigned: "Courier on the way", PickedUp: "Out for delivery", Delivered: "Delivered", Cancelled: "Cancelled",
};

export const statusTone: Record<OrderStatus, "plain" | "info" | "ok" | "danger" | "accent"> = {
  Searching: "info", Unassigned: "danger", Assigned: "accent", PickedUp: "accent", Delivered: "ok", Cancelled: "plain",
};

export const courierLabel: Record<CourierStatus, string> = { Offline: "Offline", Available: "Free", Busy: "On a delivery" };
export const courierTone: Record<CourierStatus, "plain" | "ok" | "accent"> = { Offline: "plain", Available: "ok", Busy: "accent" };

export const vehicleLabel: Record<Vehicle, string> = { Bicycle: "Bicycle", Scooter: "Scooter", Car: "Car" };

export const eventLabel: Record<EventKind, string> = {
  Created: "Order placed", Offered: "Offered", OfferDeclined: "Offer declined", OfferExpired: "Offer ran out", Assigned: "Assigned",
  Unassigned: "Waiting for a dispatcher", Released: "Handed back", PickedUp: "Picked up", Delivered: "Delivered", Cancelled: "Cancelled",
  WrongCode: "Wrong code", SearchAgain: "Searching again",
};

export const isOpen = (status: OrderStatus) => status !== "Delivered" && status !== "Cancelled";

export const plural = (n: number, word: string, many = `${word}s`) => `${n} ${n === 1 ? word : many}`;

export const roleHint: Record<Role, string> = {
  Courier: "Takes offers and delivers with the courier app",
  Merchant: "Sends and follows the orders of one merchant",
  Dispatcher: "Runs the live map, assigns by hand, keeps zones and merchants",
  Owner: "Also manages the team and how offers work",
};

/** A location is worth trusting for a couple of minutes, as the server does when making offers. */
export const isFresh = (iso: string | null, now = Date.now()) => !!iso && now - new Date(iso).getTime() <= 120_000;

/** Straight-line distance in kilometres, as the server measures it. */
export function distanceKm(a: { latitude: number; longitude: number }, b: { latitude: number; longitude: number }): number {
  const rad = Math.PI / 180;
  const dLat = (b.latitude - a.latitude) * rad;
  const dLng = (b.longitude - a.longitude) * rad;
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(a.latitude * rad) * Math.cos(b.latitude * rad) * Math.sin(dLng / 2) ** 2;
  return 2 * 6371.0088 * Math.asin(Math.min(1, Math.sqrt(h)));
}
