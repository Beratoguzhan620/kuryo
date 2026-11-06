export type Role = "Courier" | "Merchant" | "Dispatcher" | "Owner";
export type Vehicle = "Bicycle" | "Scooter" | "Car";
export type CourierStatus = "Offline" | "Available" | "Busy";
export type OrderStatus = "Searching" | "Unassigned" | "Assigned" | "PickedUp" | "Delivered" | "Cancelled";
export type EventKind =
  | "Created" | "Offered" | "OfferDeclined" | "OfferExpired" | "Assigned" | "Unassigned" | "Released"
  | "PickedUp" | "Delivered" | "Cancelled" | "WrongCode" | "SearchAgain";

export interface User { id: string; email: string; displayName: string }
export interface Session { accessToken: string; expiresAt: string; user: User }

export interface Company {
  id: string; name: string; role: Role; merchantId: string | null; timeZone: string;
  offerSeconds: number; searchRadiusKm: number; maxOffers: number;
  members: number; merchants: number; couriersOnline: number; openOrders: number; createdAt: string;
}
export interface Me { user: User; companies: Company[] }

export interface Member { userId: string; displayName: string; email: string; role: Role; merchantId: string | null; merchantName: string | null; joinedAt: string }

export interface Merchant { id: string; name: string; phone: string; address: string; latitude: number; longitude: number; prepMinutes: number; active: boolean; openOrders: number; createdAt: string }
export interface MerchantInput { name: string; phone: string; address: string; latitude: number; longitude: number; prepMinutes: number; active?: boolean }

/** Corners are [latitude, longitude]. */
export interface Zone { id: string; name: string; corners: [number, number][]; deliveryFee: number; active: boolean; areaKm2: number }
export interface ZoneInput { name: string; corners: [number, number][]; deliveryFee: number; active?: boolean }
export interface ZoneCheck { delivers: boolean; zoneId: string | null; zoneName: string | null; deliveryFee: number | null }

export interface Courier {
  userId: string; displayName: string; vehicle: Vehicle; plate: string; status: CourierStatus; statusSince: string;
  latitude: number | null; longitude: number | null; locationAt: string | null; orderId: string | null; orderCode: string | null;
}

export interface Page<T> { items: T[]; total: number; pageNumber: number; pageSize: number }

export interface OrderSummary {
  id: string; code: string; status: OrderStatus; merchantId: string; merchantName: string; customerName: string; dropoffAddress: string;
  latitude: number; longitude: number; courierId: string | null; courierName: string | null; offersMade: number;
  readyAt: string; estimatedDeliveryAt: string | null; createdAt: string;
}

export interface Place { address: string; latitude: number; longitude: number }
export interface HistoryEntry { id: number; kind: EventKind; actorName: string | null; at: string; detail: string }

export interface Order {
  id: string; number: number; code: string; status: OrderStatus;
  merchant: { id: string; name: string; phone: string }; pickup: Place;
  customerName: string; customerPhone: string; dropoff: Place; dropoffNotes: string; items: string;
  zoneName: string; deliveryFee: number; distanceKm: number; readyAt: string;
  courier: { id: string; name: string; vehicle: Vehicle; plate: string; latitude: number | null; longitude: number | null; locationAt: string | null } | null;
  offersMade: number; pendingOffer: { courierId: string; courierName: string; toPickupKm: number; expiresAt: string } | null;
  assignedAt: string | null; pickedUpAt: string | null; deliveredAt: string | null; estimatedDeliveryAt: string | null;
  cancelReason: string; trackingToken: string | null; deliveryCode: string | null; codeAttempts: number;
  history: HistoryEntry[]; createdAt: string; updatedAt: string;
}

export interface OrderInput {
  merchantId: string; customerName: string; customerPhone: string; dropoffAddress: string; latitude: number; longitude: number; dropoffNotes: string; items: string;
}

export interface Offer {
  id: string; orderId: string; orderCode: string; merchantName: string; pickup: Place; dropoff: Place;
  toPickupKm: number; tripKm: number; deliveryFee: number; readyAt: string; offeredAt: string; expiresAt: string;
}

export interface TrailPoint { latitude: number; longitude: number; at: string }

export interface TrackView {
  code: string; status: OrderStatus; merchantName: string; merchantPhone: string;
  pickup: { latitude: number; longitude: number }; dropoff: { latitude: number; longitude: number };
  createdAt: string; readyAt: string; assignedAt: string | null; pickedUpAt: string | null; deliveredAt: string | null; estimatedDeliveryAt: string | null;
  courier: { firstName: string; vehicle: Vehicle; latitude: number | null; longitude: number | null; locationAt: string | null } | null;
  deliveryCode: string | null;
}
