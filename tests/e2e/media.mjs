// Screenshots and a walkthrough for the README, against the running Compose stack, with a believable evening.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=100000 docker compose up --build -d
//        cd tests/e2e && node media.mjs [output folder]
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = process.argv[2] ?? new URL("./output/media/", import.meta.url).pathname;
mkdirSync(`${OUT}frames`, { recursive: true });
const run = Date.now().toString(36);
const password = "media-password-123";

async function call(path, token, body, method = body ? "POST" : "GET") {
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const ago = (seconds) => new Date(Date.now() - seconds * 1000).toISOString();

// The company, its areas and its merchants.
const owner = await call("/api/auth/register", null, { email: `deniz.${run}@example.com`, displayName: "Deniz Yılmaz", password });
const token = owner.accessToken;
const company = await call("/api/companies", token, { name: "Hızlı Kurye Samsun" });
const c = `/api/companies/${company.id}`;
await call(c, token, { name: "Hızlı Kurye Samsun", offerSeconds: 30, searchRadiusKm: 6, maxOffers: 5 }, "PUT");
await call(`${c}/zones`, token, { name: "Atakum", corners: [[41.318, 36.215], [41.350, 36.215], [41.350, 36.300], [41.318, 36.300]], deliveryFee: 55 });
await call(`${c}/zones`, token, { name: "İlkadım", corners: [[41.268, 36.290], [41.318, 36.290], [41.318, 36.350], [41.268, 36.350]], deliveryFee: 40 });
await call(`${c}/zones`, token, { name: "Canik", corners: [[41.248, 36.340], [41.290, 36.340], [41.290, 36.400], [41.248, 36.400]], deliveryFee: 50 });

const shops = {};
for (const [key, name, address, latitude, longitude, prep] of [
  ["pide", "Lezzet Pide", "İstiklal Cd. 10, İlkadım", 41.2867, 36.3300, 15],
  ["kebap", "Kebapçı Halil", "Bahçelievler Sk. 4, İlkadım", 41.2905, 36.3205, 20],
  ["firin", "Köşe Fırın", "Atatürk Blv. 120, Atakum", 41.3290, 36.2700, 5],
  ["eczane", "Liman Eczanesi", "Gazi Cd. 33, Canik", 41.2720, 36.3550, 0],
]) {
  shops[key] = await call(`${c}/merchants`, token, { name, phone: "0362 431 00 00", address, latitude, longitude, prepMinutes: prep });
}

// The couriers, each somewhere in town.
const crew = {};
for (const [key, name, vehicle, plate, latitude, longitude, online] of [
  ["ali", "Ali Kaya", "Scooter", "55 AK 101", 41.2885, 36.3290, true],
  ["veli", "Veli Demir", "Scooter", "55 VD 202", 41.2990, 36.3150, true],
  ["zeynep", "Zeynep Arslan", "Bicycle", "", 41.3270, 36.2760, true],
  ["mert", "Mert Öztürk", "Car", "55 MO 303", 41.2750, 36.3480, true],
  ["elif", "Elif Şahin", "Scooter", "55 ES 404", 41.3100, 36.3000, true],
  ["can", "Can Yıldız", "Bicycle", "", 41.2800, 36.3350, false],
]) {
  const session = await call("/api/auth/register", null, { email: `${key}.${run}@example.com`, displayName: name, password });
  await call(`${c}/members`, token, { email: session.user.email, role: "Courier" });
  await call(`${c}/couriers/${session.user.id}`, token, { vehicle, plate }, "PUT");
  crew[key] = { id: session.user.id, token: session.accessToken, email: session.user.email, name, at: { latitude, longitude } };
  if (online) {
    await call(`${c}/couriers/me/status`, session.accessToken, { online: true });
    await call(`${c}/couriers/me/locations`, session.accessToken, { points: [{ latitude, longitude, at: ago(60) }] });
  }
}
const cashier = await call("/api/auth/register", null, { email: `selin.${run}@example.com`, displayName: "Selin Kasa", password });
await call(`${c}/members`, token, { email: cashier.user.email, role: "Merchant", merchantId: shops.pide.id });

const order = (shop, customerName, address, latitude, longitude, items, notes = "") => call(`${c}/orders`, token, {
  merchantId: shops[shop].id, customerName, customerPhone: "0532 000 00 00", dropoffAddress: address, latitude, longitude, dropoffNotes: notes, items,
});
const ping = (who, points) => call(`${c}/couriers/me/locations`, crew[who].token, { points });

/** A courier carries an order along a path: assigned by hand, a few positions on the way, picked up. */
async function carry(o, who, path, deliver) {
  await call(`${c}/orders/${o.id}/assign`, token, { courierId: crew[who].id });
  await ping(who, path.slice(0, 2).map(([latitude, longitude], i) => ({ latitude, longitude, at: ago(50 - i * 10) })));
  await call(`${c}/orders/${o.id}/pickup`, crew[who].token, {});
  await ping(who, path.slice(2).map(([latitude, longitude], i) => ({ latitude, longitude, at: ago(30 - i * 10) })));
  const [latitude, longitude] = path.at(-1);
  crew[who].at = { latitude, longitude };
  if (deliver) await call(`${c}/orders/${o.id}/deliver`, crew[who].token, { code: o.deliveryCode });
}

// Earlier this evening: two delivered, one with a wrong code first.
const early = await order("firin", "Hakan Çelik", "Mimar Sinan Mh. 12, Atakum", 41.3335, 36.2640, "Simit, poğaça");
await carry(early, "zeynep", [[41.3275, 36.2745], [41.3285, 36.2715], [41.3300, 36.2690], [41.3320, 36.2660], [41.3333, 36.2642]], true);
const second = await order("eczane", "Fatma Aydın", "Yavuz Selim Cd. 8, Canik", 41.2650, 36.3700, "Reçeteli ilaç", "Zile basmayın, arayın");
await call(`${c}/orders/${second.id}/assign`, token, { courierId: crew.mert.id });
await call(`${c}/orders/${second.id}/pickup`, crew.mert.token, {});
await fetch(`${BASE}${c}/orders/${second.id}/deliver`, { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${crew.mert.token}` }, body: JSON.stringify({ code: second.deliveryCode === "1111" ? "2222" : "1111" }) });
await call(`${c}/orders/${second.id}/deliver`, crew.mert.token, { code: second.deliveryCode });

// On the way right now.
const onWay = await order("pide", "Ayşe Kaya", "Atatürk Blv. 50, Atakum", 41.3210, 36.2950, "2 kıymalı pide, 1 ayran", "Kat 3, zil 7");
await carry(onWay, "veli", [[41.2950, 36.3200], [41.2890, 36.3280], [41.2960, 36.3190], [41.3040, 36.3110], [41.3120, 36.3040]], false);
const toPickup = await order("kebap", "Murat Koç", "Kale Mh. 3, İlkadım", 41.2960, 36.3400, "Adana dürüm");
await call(`${c}/orders/${toPickup.id}/assign`, token, { courierId: crew.elif.id });

// Nobody took this one.
await call(c, token, { name: "Hızlı Kurye Samsun", offerSeconds: 10, searchRadiusKm: 0.5, maxOffers: 1 }, "PUT");
const lonely = await order("firin", "Kemal Er", "Çatalçam Mh. 40, Atakum", 41.3450, 36.2300, "Ekmek, peynir");
await sleep(13_000);
await call(c, token, { name: "Hızlı Kurye Samsun", offerSeconds: 30, searchRadiusKm: 6, maxOffers: 5 }, "PUT");
// And this one is looking for a courier as we speak.
await order("firin", "Derya Uçar", "Kurupelit Mh. 22, Atakum", 41.3360, 36.2600, "2 açma, 1 boyoz");
// Keep everyone's position fresh for the screenshots.
const keepFresh = setInterval(() => {
  for (const who of ["ali", "veli", "zeynep", "mert", "elif"]) void ping(who, [{ ...crew[who].at, at: new Date().toISOString() }]).catch(() => {});
}, 20_000);

const browser = await chromium.launch();
async function signedIn(who, viewport, options = {}) {
  const context = await browser.newContext({ viewport, deviceScaleFactor: 2, locale: "en-GB", timezoneId: "Europe/Istanbul", ...options });
  const page = await context.newPage();
  if (who) {
    const session = await call("/api/auth/login", null, { email: who.email, password });
    await page.goto(`${BASE}/signin`);
    await page.evaluate((s) => localStorage.setItem("kuryo.session", JSON.stringify(s)), session);
  }
  return page;
}
const settle = async (page, ms = 1500) => { await page.waitForLoadState("networkidle").catch(() => {}); await page.waitForTimeout(ms); };

const desk = await signedIn({ email: owner.user.email }, { width: 1440, height: 900 });
await desk.goto(`${BASE}/c/${company.id}/live`);
await desk.locator(".live-badge.live").waitFor();
await settle(desk, 2500);
await desk.screenshot({ path: `${OUT}live.png` });
await desk.goto(`${BASE}/c/${company.id}/orders/${onWay.id}`);
await desk.getByText("Ayşe Kaya").first().waitFor();
await settle(desk);
await desk.screenshot({ path: `${OUT}order.png` });
await desk.goto(`${BASE}/c/${company.id}/zones`);
await settle(desk);
await desk.screenshot({ path: `${OUT}zones.png` });
await desk.goto(`${BASE}/c/${company.id}/orders`);
await desk.getByRole("tab", { name: "All" }).click();
await settle(desk);
await desk.screenshot({ path: `${OUT}orders.png` });
await desk.goto(`${BASE}/c/${company.id}/live`);
await desk.locator(".live-badge.live").waitFor();

const customer = await signedIn(null, { width: 390, height: 844 }, { isMobile: true, hasTouch: true });
await customer.goto(`${BASE}/t/${onWay.trackingToken}`);
await customer.getByText("Give this code to the courier at the door").waitFor();
await settle(customer);
await customer.screenshot({ path: `${OUT}tracking.png` });

// The walkthrough: an order arrives, the nearest courier passes, the next takes it; office and customer follow live.
const phoneA = await signedIn({ email: crew.ali.email }, { width: 390, height: 844 }, { isMobile: true, hasTouch: true, geolocation: crew.ali.at, permissions: ["geolocation"] });
await phoneA.goto(`${BASE}/courier`);
await phoneA.getByText("sharing location").waitFor({ timeout: 20_000 });
let frame = 0;
const shoot = async (label) => {
  const n = String(frame++).padStart(2, "0");
  await desk.screenshot({ path: `${OUT}frames/${n}-desk.png` });
  await phoneA.screenshot({ path: `${OUT}frames/${n}-phone.png` });
  await customer.screenshot({ path: `${OUT}frames/${n}-customer.png` });
  console.log(`frame ${n}: ${label}`);
};

const fresh = await order("pide", "Selin Aksoy", "Kurupelit Mh. 7, Atakum", 41.3080, 36.3060, "1 karışık pide");
await customer.goto(`${BASE}/t/${fresh.trackingToken}`);
await customer.getByText("Give this code to the courier at the door").waitFor();
await phoneA.getByText("New offer").waitFor({ timeout: 20_000 });
await settle(phoneA, 600);
await phoneA.screenshot({ path: `${OUT}courier-offer.png` });
await shoot("offer on the nearest courier's phone");
await phoneA.waitForTimeout(2500);
await shoot("the countdown runs");
await phoneA.getByRole("button", { name: "Pass" }).click();
await phoneA.getByText("No offer right now").waitFor();
await shoot("passed; the order moves on");
// Ali passed; the matcher offers it to the next nearest free courier, who takes it from the API side.
let taken = null;
for (let i = 0; i < 30 && !taken; i++) {
  for (const who of ["veli", "elif", "zeynep", "mert"]) {
    const r = await fetch(`${BASE}${c}/couriers/me/offer`, { headers: { Authorization: `Bearer ${crew[who].token}` } });
    const o = r.status === 200 ? await r.json() : null;
    if (o?.orderId === fresh.id) { await call(`${c}/offers/${o.id}/accept`, crew[who].token, {}); taken = who; break; }
  }
  if (!taken) await sleep(700);
}
await customer.getByRole("heading", { name: /Arriving around/ }).waitFor({ timeout: 15_000 });
await settle(customer, 800);
await shoot(`taken by ${taken}`);
for (const [i, point] of [[41.3000, 36.3150], [41.3030, 36.3110], [41.3055, 36.3080]].entries()) {
  await ping(taken, [{ latitude: point[0], longitude: point[1], at: new Date().toISOString() }]);
  crew[taken].at = { latitude: point[0], longitude: point[1] };
  await sleep(1200);
  await shoot(`on the way ${i + 1}`);
}
await call(`${c}/orders/${fresh.id}/pickup`, crew[taken].token, {});
await customer.locator(".track-steps li.now").getByText("Out for delivery").waitFor({ timeout: 10_000 });
await shoot("picked up");
await call(`${c}/orders/${fresh.id}/deliver`, crew[taken].token, { code: fresh.deliveryCode });
await customer.getByRole("heading", { name: "Delivered" }).waitFor({ timeout: 10_000 });
await settle(customer, 600);
await shoot("delivered");

// The courier on a delivery.
const phoneV = await signedIn({ email: crew.veli.email }, { width: 390, height: 844 }, { isMobile: true, hasTouch: true, geolocation: { latitude: 41.3120, longitude: 36.3040 }, permissions: ["geolocation"] });
await phoneV.goto(`${BASE}/courier`);
await phoneV.getByText("Deliver to").waitFor();
await settle(phoneV);
await phoneV.screenshot({ path: `${OUT}courier-delivery.png` });

clearInterval(keepFresh);
await browser.close();
console.log(`media written to ${OUT} (${frame} frames), unassigned order ${lonely.code}`);
