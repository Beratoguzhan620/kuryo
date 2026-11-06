import { useEffect, useState } from "react";

/** The pages of the app. Ids are in the address, so every page can be bookmarked and shared. */
export type Route =
  | { page: "signin"; next?: string }
  | { page: "register"; next?: string }
  | { page: "home" }
  | { page: "live"; companyId: string }
  | { page: "orders"; companyId: string }
  | { page: "order-new"; companyId: string }
  | { page: "order"; companyId: string; orderId: string }
  | { page: "zones"; companyId: string }
  | { page: "merchants"; companyId: string }
  | { page: "team"; companyId: string }
  | { page: "courier" }
  | { page: "track"; token: string }
  | { page: "not-found" };

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const isId = (value: string | undefined): value is string => !!value && guid.test(value);

/** Only addresses within the app are followed after signing in. */
function safeNext(value: string | null): string | undefined {
  return value && value.startsWith("/") && !value.startsWith("//") ? value : undefined;
}

export function parseRoute(pathname: string, search = ""): Route {
  const params = new URLSearchParams(search);
  const parts = pathname.split("/").filter(Boolean).map(decodeURIComponent);
  const [a, b, c, d] = parts;
  const n = parts.length;

  if (n === 0) return { page: "home" };
  if (n === 1 && a === "signin") return { page: "signin", next: safeNext(params.get("next")) };
  if (n === 1 && a === "register") return { page: "register", next: safeNext(params.get("next")) };
  if (n === 1 && a === "courier") return { page: "courier" };
  if (n === 2 && a === "t" && /^[A-Za-z0-9_-]{8,64}$/.test(b!)) return { page: "track", token: b! };
  if (a !== "c" || !isId(b)) return { page: "not-found" };
  if (n === 2 || (n === 3 && c === "live")) return { page: "live", companyId: b };
  if (n === 3 && c === "orders") return { page: "orders", companyId: b };
  if (n === 4 && c === "orders" && d === "new") return { page: "order-new", companyId: b };
  if (n === 4 && c === "orders" && isId(d)) return { page: "order", companyId: b, orderId: d };
  if (n === 3 && c === "zones") return { page: "zones", companyId: b };
  if (n === 3 && c === "merchants") return { page: "merchants", companyId: b };
  if (n === 3 && c === "team") return { page: "team", companyId: b };
  return { page: "not-found" };
}

export const companyOf = (route: Route): string | undefined => ("companyId" in route ? route.companyId : undefined);

/** Pages anyone can open, signed in or not. */
export const isPublic = (route: Route) => route.page === "track";

export function navigate(path: string, replace = false) {
  if (replace) history.replaceState(null, "", path);
  else history.pushState(null, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
  window.scrollTo({ top: 0 });
}

export function useRoute(): Route {
  const read = () => parseRoute(location.pathname, location.search);
  const [route, setRoute] = useState<Route>(read);
  useEffect(() => {
    const update = () => setRoute(read());
    window.addEventListener("popstate", update);
    return () => window.removeEventListener("popstate", update);
  }, []);
  return route;
}

/** Props for an anchor that moves within the app without reloading the page. */
export function linkProps(href: string) {
  return {
    href,
    onClick: (e: React.MouseEvent) => {
      if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
      e.preventDefault();
      navigate(href);
    },
  };
}

/** The sign-in page, coming back here afterwards. */
export const signInHere = () => `/signin?next=${encodeURIComponent(location.pathname + location.search)}`;
