// An office, a merchant, two couriers on phones and a customer, in separate browsers, against the whole Compose stack.
// The nearer courier passes, the next one takes the order, the customer follows it live, and a second order that
// nobody answers is assigned by hand.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=1000 docker compose up --build -d
//        cd tests/e2e && npm install && npx playwright install chromium && node kuryo.mjs
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = new URL("./output/", import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const run = Date.now().toString(36);
const password = "e2e-test-password";
const owner = { name: "Deniz Yılmaz", email: `deniz.${run}@example.com` };
const cashier = { name: "Selin Kasa", email: `selin.${run}@example.com` };
const ali = { name: "Ali Kaya", email: `ali.${run}@example.com`, at: { latitude: 41.2895, longitude: 36.3310 } };
const veli = { name: "Veli Demir", email: `veli.${run}@example.com`, at: { latitude: 41.2990, longitude: 36.3250 } };

const browser = await chromium.launch();
const errors = [];
const pages = {};
let failures = 0;

async function step(name, action) {
  try {
    await action();
    console.log(`ok   ${name}`);
  } catch (error) {
    failures++;
    console.log(`FAIL ${name}\n     ${String(error.message ?? error).split("\n").slice(0, 3).join("\n     ")}`);
    for (const [label, page] of Object.entries(pages)) await page.screenshot({ path: `${OUT}${label}-${failures}.png` }).catch(() => {});
  }
}

async function open(label, viewport, options = {}) {
  const context = await browser.newContext({ viewport, locale: "en-GB", timezoneId: "Europe/Istanbul", ...options });
  const page = await context.newPage();
  page.on("pageerror", (error) => errors.push(`${label}: ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error" && !/Failed to load resource|tile\.openstreetmap|WebSocket/.test(message.text())) errors.push(`${label}: ${message.text()}`);
  });
  pages[label] = page;
  return page;
}

const phone = (who) => open(who.name.split(" ")[0].toLowerCase(), { width: 390, height: 844 },
  { isMobile: true, hasTouch: true, geolocation: who.at, permissions: ["geolocation"] });

async function api(page, method, path, body) {
  return page.evaluate(async ({ method, path, body }) => {
    const session = JSON.parse(localStorage.getItem("kuryo.session"));
    const response = await fetch(path, { method, headers: { "Content-Type": "application/json", Authorization: `Bearer ${session.accessToken}` }, body: body ? JSON.stringify(body) : undefined });
    if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
    return response.status === 204 ? null : response.json();
  }, { method, path, body });
}

async function register(who) {
  const response = await fetch(`${BASE}/api/auth/register`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ displayName: who.name, email: who.email, password }) });
  if (!response.ok) throw new Error(`register ${who.email}: ${response.status}`);
}

async function signIn(page, who, next) {
  await page.goto(`${BASE}/signin?next=${encodeURIComponent(next)}`);
  await page.getByLabel("Email").fill(who.email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
}

/** Clicks the map at an offset from its centre, in pixels. */
async function clickMap(page, dx, dy, scope = page) {
  const box = await scope.locator(".leaflet-container").first().boundingBox();
  await page.mouse.click(box.x + box.width / 2 + dx, box.y + box.height / 2 + dy);
}

const desk = await open("desk", { width: 1440, height: 900 });
let company = "";
let apiBase = "";
let orderId = "";
let tracking = "";
let code = "";

await step("couriers and the merchant's cashier have accounts", async () => {
  for (const who of [cashier, ali, veli]) await register(who);
});

await step("an owner signs up and creates a courier company", async () => {
  await desk.goto(`${BASE}/register`);
  await desk.getByLabel("Your name").fill(owner.name);
  await desk.getByLabel("Email").fill(owner.email);
  await desk.getByLabel("Password").fill(password);
  await desk.getByRole("button", { name: "Create account" }).click();
  await desk.getByRole("heading", { name: "Your companies" }).waitFor();
  await desk.getByLabel("Name").fill(`Hızlı Kurye ${run}`);
  await desk.getByRole("button", { name: "Create company" }).click();
  await desk.getByRole("heading", { name: "Zones" }).waitFor();
  company = new URL(desk.url()).pathname.split("/")[2];
  apiBase = `/api/companies/${company}`;
});

await step("the owner draws a delivery zone on the map", async () => {
  await desk.getByRole("button", { name: "Draw a zone" }).click();
  for (const [dx, dy] of [[-220, -170], [220, -170], [220, 170], [-220, 170]]) await clickMap(desk, dx, dy);
  await desk.getByLabel("Name").fill("Merkez");
  await desk.getByLabel("Delivery fee (₺)").fill("45");
  await desk.getByRole("button", { name: "Save zone" }).click();
  await desk.getByRole("button", { name: "Change Merkez" }).waitFor();
});

await step("adds a merchant with its pickup point", async () => {
  await desk.getByRole("link", { name: "Merchants" }).click();
  await desk.getByRole("button", { name: "Add merchant" }).click();
  const dialog = desk.getByRole("dialog");
  await dialog.getByLabel("Name").fill("Lezzet Pide");
  await dialog.getByLabel("Phone").fill("0362 431 00 00");
  await dialog.getByLabel("Address").fill("İstiklal Cd. 10, İlkadım");
  await dialog.getByLabel("Usual preparation (minutes)").fill("10");
  await clickMap(desk, 0, 0, dialog);
  await dialog.getByRole("button", { name: "Save" }).click();
  await desk.getByRole("cell", { name: /Lezzet Pide/ }).waitFor();
});

await step("puts two couriers and the cashier on the team", async () => {
  await desk.getByRole("link", { name: "Team" }).click();
  await desk.getByRole("button", { name: "Add member" }).click();
  const dialog = desk.getByRole("dialog");
  await dialog.getByLabel("Email").fill(ali.email);
  await dialog.getByRole("radio", { name: /Courier/ }).check();
  await dialog.getByRole("button", { name: "Add", exact: true }).click();
  await desk.getByText(ali.email).waitFor();
  const merchants = await api(desk, "GET", `${apiBase}/merchants`);
  await api(desk, "POST", `${apiBase}/members`, { email: veli.email, role: "Courier" });
  await api(desk, "POST", `${apiBase}/members`, { email: cashier.email, role: "Merchant", merchantId: merchants[0].id });
  await desk.getByLabel("Seconds to answer").fill("20");
  await desk.getByRole("button", { name: "Save settings" }).click();
  await desk.getByText("Saved. New offers follow these settings.").waitFor();
});

const aliPhone = await phone(ali);
const veliPhone = await phone(veli);

await step("both couriers go online and share where they are", async () => {
  for (const [page, who] of [[aliPhone, ali], [veliPhone, veli]]) {
    await signIn(page, who, "/courier");
    await page.getByRole("button", { name: "Go online" }).click();
    await page.getByText("sharing location").waitFor({ timeout: 20_000 });
    await page.getByText("No offer right now").waitFor();
  }
});

await step("the dispatcher sees both couriers on the live map", async () => {
  await desk.getByRole("link", { name: "Live" }).click();
  await desk.locator(".live-badge.live").waitFor({ timeout: 15_000 });
  await desk.locator(".courier-list").getByText("Ali Kaya").waitFor();
  await desk.locator(".courier-list").getByText("Free").first().waitFor();
});

const till = await open("till", { width: 1280, height: 860 });

await step("the cashier sends an order, picking the door on the map", async () => {
  await signIn(till, cashier, `/c/${company}/orders/new`);
  await till.getByRole("heading", { name: "New order" }).waitFor();
  await till.getByLabel("Customer").fill("Ayşe Kaya");
  await till.getByLabel("Phone").fill("0532 000 00 00");
  await till.getByLabel("Address").fill("Atatürk Blv. 50, Atakum");
  await till.getByLabel("For the courier").fill("Kat 3, zil 7");
  await till.getByLabel("Items").fill("2 kıymalı pide");
  await till.locator(".leaflet-container").waitFor();
  await till.waitForTimeout(500);
  await clickMap(till, -90, -60);
  await till.getByText("Fee").waitFor();
  await till.getByRole("button", { name: "Send order" }).click();
  await till.getByText("Delivery code").waitFor();
  orderId = new URL(till.url()).pathname.split("/").pop();
  const order = await api(till, "GET", `${apiBase}/orders/${orderId}`);
  tracking = order.trackingToken;
  code = order.deliveryCode;
});

const customer = await open("customer", { width: 390, height: 844 }, { isMobile: true, hasTouch: true });

await step("the customer opens the tracking link without signing in", async () => {
  await customer.goto(`${BASE}/t/${tracking}`);
  await customer.getByText("Give this code to the courier at the door").waitFor();
  await customer.locator(".code-big").getByText(code).waitFor();
  const text = await customer.locator(".track-card").innerText();
  if (text.includes("Ayşe") || text.includes("0532") || text.includes("Atatürk")) throw new Error("the tracking page shows the customer's details");
});

await step("the nearest courier gets the offer first, live, and passes", async () => {
  await aliPhone.getByText("New offer").waitFor({ timeout: 15_000 });
  await aliPhone.getByRole("timer").waitFor();
  if (await veliPhone.getByText("New offer").isVisible()) throw new Error("both couriers were offered the order");
  await aliPhone.getByRole("button", { name: "Pass" }).click();
  await aliPhone.getByText("No offer right now").waitFor();
});

await step("the next courier gets it and takes it", async () => {
  await veliPhone.getByText("New offer").waitFor({ timeout: 15_000 });
  await veliPhone.getByRole("button", { name: "Take it" }).click();
  await veliPhone.getByText("Collect from").waitFor();
  await veliPhone.getByText("Lezzet Pide").first().waitFor();
});

await step("the office and the customer see who is coming", async () => {
  await desk.locator(".lane-block").filter({ hasText: "On the way" }).getByText("Veli Demir").waitFor({ timeout: 10_000 });
  await customer.getByRole("heading", { name: /Arriving around/ }).waitFor({ timeout: 10_000 });
  await customer.getByText("Veli", { exact: true }).waitFor();
});

await step("the courier rides, collects, and a wrong code is refused", async () => {
  await veliPhone.context().setGeolocation({ latitude: 41.2870, longitude: 36.3300 });
  await veliPhone.waitForTimeout(6000);
  await veliPhone.getByRole("button", { name: "I have the order" }).click();
  await veliPhone.getByText("Deliver to").waitFor();
  await veliPhone.getByText("Ayşe Kaya").waitFor();
  await customer.locator(".track-steps li.now").getByText("Out for delivery").waitFor({ timeout: 10_000 });
  const wrong = code === "1111" ? "2222" : "1111";
  await veliPhone.getByLabel("Delivery code").fill(wrong);
  await veliPhone.getByRole("button", { name: "Delivered" }).click();
  await veliPhone.getByText("That is not the code.").waitFor();
});

await step("the right code closes the delivery everywhere", async () => {
  await veliPhone.getByLabel("Delivery code").fill(code);
  await veliPhone.getByRole("button", { name: "Delivered" }).click();
  await veliPhone.getByRole("status").getByText("delivered", { exact: false }).waitFor();
  await customer.getByRole("heading", { name: "Delivered" }).waitFor({ timeout: 10_000 });
  await till.goto(`${BASE}/c/${company}/orders/${orderId}`);
  await till.getByText("Delivered", { exact: true }).first().waitFor();
  await till.getByText("Wrong code").waitFor();
  const trail = await api(desk, "GET", `${apiBase}/couriers/${(await api(veliPhone, "GET", `${apiBase}/couriers/me`)).userId}/trail?orderId=${orderId}`);
  if (trail.length < 1) throw new Error("no trail was kept for the delivery");
});

await step("an order nobody answers lands with the dispatcher, who assigns it by hand", async () => {
  await api(desk, "PUT", apiBase, { name: `Hızlı Kurye ${run}`, offerSeconds: 10, searchRadiusKm: 6, maxOffers: 1 });
  await veliPhone.getByRole("button", { name: "Go offline" }).click();
  await veliPhone.getByText("You are offline").waitFor();
  const merchants = await api(desk, "GET", `${apiBase}/merchants`);
  const second = await api(till, "POST", `${apiBase}/orders`, {
    merchantId: merchants[0].id, customerName: "Mehmet Öz", customerPhone: "0533 000 00 00", dropoffAddress: "Cumhuriyet Cd. 5",
    latitude: 41.2905, longitude: 36.3260, dropoffNotes: "", items: "1 lahmacun",
  });
  await aliPhone.getByText("New offer").waitFor({ timeout: 15_000 });
  await desk.getByRole("alert").getByText("found no courier").waitFor({ timeout: 30_000 });
  await aliPhone.getByText("No offer right now").waitFor();
  const lane = desk.locator(".lane-block").filter({ hasText: "Needs a courier" });
  await lane.getByText(second.code).waitFor();
  await lane.getByRole("button", { name: "Assign" }).click();
  await desk.getByRole("dialog").getByRole("button", { name: "Assign to Ali Kaya" }).click();
  await lane.getByText(second.code).waitFor({ state: "detached" });
  await aliPhone.getByText("Collect from").waitFor({ timeout: 10_000 });
});

await step("the order list and the history tell the story", async () => {
  await desk.goto(`${BASE}/c/${company}/orders/${orderId}`);
  for (const text of ["Offer declined", "Assigned", "Picked up", "Wrong code"]) await desk.locator(".history").getByText(text, { exact: true }).first().waitFor();
  await desk.goto(`${BASE}/c/${company}/orders`);
  await desk.getByRole("tab", { name: "All" }).click();
  await desk.getByRole("cell", { name: /Mehmet Öz/ }).waitFor();
});

await step("outsiders see nothing, and a made-up tracking link shows that it has ended", async () => {
  const response = await fetch(`${BASE}${apiBase}/orders`);
  if (response.status !== 401) throw new Error(`expected 401, got ${response.status}`);
  await customer.goto(`${BASE}/t/not-a-real-token-123`);
  await customer.getByText("This tracking link has ended").waitFor();
});

if (errors.length > 0) {
  failures++;
  console.log(`FAIL the browsers reported errors\n     ${[...new Set(errors)].join("\n     ")}`);
}

await browser.close();
console.log(failures === 0 ? "\nAll steps passed." : `\n${failures} step(s) failed.`);
process.exit(failures === 0 ? 0 : 1);
