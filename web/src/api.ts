import type {
  Company, Courier, Me, Member, Merchant, MerchantInput, Offer, Order, OrderInput, OrderStatus, OrderSummary, Page, Role, Session,
  TrackView, TrailPoint, Vehicle, Zone, ZoneCheck, ZoneInput,
} from "./types";

/** An API error with the stable code from the problem response, such as "offer_gone". */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string | null,
    public readonly fields: Record<string, string[]> = {},
  ) {
    super(message);
  }

  /** The request never reached the server: the device is offline or the server is down. */
  get offline() {
    return this.status === 0;
  }
}

let token: string | null = null;
let onUnauthorized: () => void = () => {};

export function setToken(value: string | null, unauthorized: () => void) {
  token = value;
  onUnauthorized = unauthorized;
}

export const currentToken = () => token;

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (typeof init.body === "string") headers.set("Content-Type", "application/json");
  if (token && !headers.has("Authorization")) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(path, { ...init, headers });
  } catch (e) {
    if ((e as Error).name === "AbortError") throw e;
    throw new ApiError("The server cannot be reached. Check your connection.", 0, null);
  }

  if (response.status === 401 && token) onUnauthorized();
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const fields = (problem?.errors ?? {}) as Record<string, string[]>;
    const fallback = response.status === 429 ? "Too many requests. Wait a minute and try again." : "Something went wrong. Try again.";
    const detail = problem?.detail ?? Object.values(fields).flat()[0] ?? fallback;
    throw new ApiError(detail, response.status, problem?.code ?? null, fields);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

const json = (body: unknown) => JSON.stringify(body);
const post = (body: unknown = {}): RequestInit => ({ method: "POST", body: json(body) });
const put = (body: unknown): RequestInit => ({ method: "PUT", body: json(body) });
const co = (id: string) => `/api/companies/${id}`;
const ord = (c: string, id: string) => `${co(c)}/orders/${id}`;

export function query(params: Record<string, string | number | boolean | string[] | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (Array.isArray(value)) value.forEach((v) => search.append(key, v));
    else if (value !== undefined && value !== null && String(value).trim() !== "") search.set(key, String(value).trim());
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

export interface OrderFilters { status?: OrderStatus[]; merchantId?: string; courierId?: string; q?: string; page?: number; pageSize?: number }

export interface Settings { name: string; offerSeconds: number; searchRadiusKm: number; maxOffers: number }

export const api = {
  register: (body: { email: string; displayName: string; password: string }) => request<Session>("/api/auth/register", post(body)),
  login: (email: string, password: string) => request<Session>("/api/auth/login", post({ email, password })),
  me: () => request<Me>("/api/me"),

  createCompany: (name: string) => request<Company>("/api/companies", post({ name })),
  company: (c: string) => request<Company>(co(c)),
  configure: (c: string, body: Settings) => request<Company>(co(c), put(body)),
  members: (c: string) => request<Member[]>(`${co(c)}/members`),
  addMember: (c: string, email: string, role: Role, merchantId?: string | null) => request<Member>(`${co(c)}/members`, post({ email, role, merchantId: merchantId || null })),
  changeRole: (c: string, userId: string, role: Role, merchantId?: string | null) =>
    request<Member>(`${co(c)}/members/${userId}`, { method: "PATCH", body: json({ role, merchantId: merchantId || null }) }),
  removeMember: (c: string, userId: string) => request<void>(`${co(c)}/members/${userId}`, { method: "DELETE" }),

  merchants: (c: string, q?: string) => request<Merchant[]>(`${co(c)}/merchants${query({ q })}`),
  createMerchant: (c: string, body: MerchantInput) => request<Merchant>(`${co(c)}/merchants`, post(body)),
  updateMerchant: (c: string, id: string, body: MerchantInput) => request<Merchant>(`${co(c)}/merchants/${id}`, put(body)),

  zones: (c: string) => request<Zone[]>(`${co(c)}/zones`),
  createZone: (c: string, body: ZoneInput) => request<Zone>(`${co(c)}/zones`, post(body)),
  updateZone: (c: string, id: string, body: ZoneInput) => request<Zone>(`${co(c)}/zones/${id}`, put(body)),
  checkZone: (c: string, lat: number, lng: number) => request<ZoneCheck>(`${co(c)}/zones/check${query({ lat, lng })}`),

  couriers: (c: string) => request<Courier[]>(`${co(c)}/couriers`),
  saveCourier: (c: string, userId: string, vehicle: Vehicle, plate: string) => request<Courier>(`${co(c)}/couriers/${userId}`, put({ vehicle, plate })),
  trail: (c: string, userId: string, orderId?: string) => request<TrailPoint[]>(`${co(c)}/couriers/${userId}/trail${query({ orderId })}`),
  myCourier: (c: string) => request<Courier>(`${co(c)}/couriers/me`),
  setOnline: (c: string, online: boolean) => request<Courier>(`${co(c)}/couriers/me/status`, post({ online })),
  ping: (c: string, points: { latitude: number; longitude: number; at: string }[]) =>
    request<{ stored: number; locationAt: string | null }>(`${co(c)}/couriers/me/locations`, post({ points })),
  myOffer: (c: string) => request<Offer | undefined>(`${co(c)}/couriers/me/offer`),
  accept: (c: string, offerId: string) => request<Order>(`${co(c)}/offers/${offerId}/accept`, post()),
  decline: (c: string, offerId: string) => request<void>(`${co(c)}/offers/${offerId}/decline`, post()),

  orders: (c: string, f: OrderFilters) => request<Page<OrderSummary>>(`${co(c)}/orders${query({ ...f, status: f.status?.length ? f.status.join(",") : undefined })}`),
  order: (c: string, id: string) => request<Order>(ord(c, id)),
  createOrder: (c: string, body: OrderInput) => request<Order>(`${co(c)}/orders`, post(body)),
  assign: (c: string, id: string, courierId: string) => request<Order>(`${ord(c, id)}/assign`, post({ courierId })),
  release: (c: string, id: string, reason: string) => request<Order>(`${ord(c, id)}/release`, post({ reason })),
  searchAgain: (c: string, id: string) => request<Order>(`${ord(c, id)}/search-again`, post()),
  pickUp: (c: string, id: string) => request<Order>(`${ord(c, id)}/pickup`, post()),
  deliver: (c: string, id: string, code: string) => request<Order>(`${ord(c, id)}/deliver`, post({ code })),
  confirmDelivered: (c: string, id: string, reason: string) => request<Order>(`${ord(c, id)}/confirm-delivered`, post({ reason })),
  cancel: (c: string, id: string, reason: string) => request<Order>(`${ord(c, id)}/cancel`, post({ reason })),

  track: (token: string) => request<TrackView>(`/api/track/${encodeURIComponent(token)}`),
};
