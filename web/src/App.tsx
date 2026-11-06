import { Bike } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, setToken } from "./api";
import { Shell } from "./components/Shell";
import { Empty, Failed, Loading } from "./components/States";
import { AccountCtx, CompanyCtx, isDispatcher, type Account } from "./context";
import { CourierApp } from "./courier/CourierApp";
import { Register, SignIn } from "./pages/Auth";
import { Home } from "./pages/Home";
import { Live } from "./pages/Live";
import { Merchants } from "./pages/Merchants";
import { OrderForm } from "./pages/OrderForm";
import { OrderPage } from "./pages/OrderPage";
import { Orders } from "./pages/Orders";
import { Team } from "./pages/Team";
import { TrackPage } from "./pages/TrackPage";
import { Zones } from "./pages/Zones";
import { companyOf, linkProps, navigate, signInHere, useRoute, type Route } from "./router";
import { loadSession, saveSession } from "./session";
import type { Company, Me, Session } from "./types";

function NotFound() {
  return (
    <Empty title="This page does not exist" icon={<Bike size={26} />}>
      <span className="muted">Or it belongs to a company you are not part of.</span>
      <a className="button" {...linkProps("/")}>Home</a>
    </Empty>
  );
}

/** Pages a merchant may open; the rest are for the office. */
const merchantPages = new Set(["orders", "order", "order-new"]);

function CompanyPage({ route, company }: { route: Route; company: Company }) {
  if (company.role === "Courier") {
    return <Empty title="This is for the office" icon={<Bike size={26} />}><a className="button primary" {...linkProps("/courier")}>Open the courier app</a></Empty>;
  }
  if (!isDispatcher(company) && !merchantPages.has(route.page)) {
    if (route.page === "live") { navigate(`/c/${company.id}/orders`, true); return null; }
    return <NotFound />;
  }
  switch (route.page) {
    case "live": return <Live />;
    case "orders": return <Orders />;
    case "order-new": return <OrderForm />;
    case "order": return <OrderPage key={route.orderId} orderId={route.orderId} />;
    case "zones": return <Zones />;
    case "merchants": return <Merchants />;
    case "team": return <Team />;
    default: return <NotFound />;
  }
}

export function App() {
  const route = useRoute();
  const [session, setSession] = useState<Session | null>(() => loadSession());
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);

  const signOut = useCallback(() => {
    saveSession(null);
    setToken(null, () => {});
    setSession(null);
    setMe(null);
    navigate("/signin", true);
  }, []);

  if (session) setToken(session.accessToken, signOut);

  const refresh = useCallback(async () => {
    const value = await api.me();
    setMe(value);
    return value;
  }, []);

  useEffect(() => {
    if (!session) return;
    setError(null);
    refresh().catch((e: Error) => setError(e.message));
  }, [session, refresh]);

  // A company in the address that is not among ours yet may be one we were just added to.
  const companyId = companyOf(route);
  useEffect(() => {
    if (me && companyId && !me.companies.some((c) => c.id === companyId)) void refresh();
  }, [companyId]); // eslint-disable-line react-hooks/exhaustive-deps

  const open = route.page === "track";
  useEffect(() => {
    if (open) return;
    const auth = route.page === "signin" || route.page === "register";
    if (!session && !auth) navigate(signInHere(), true);
    if (session && auth) navigate(route.next ?? "/", true);
  }, [session, route, open]);

  const signedIn = useCallback((value: Session) => {
    saveSession(value);
    setToken(value.accessToken, signOut);
    setSession(value);
  }, [signOut]);

  const account = useMemo<Account | null>(() => (session && me ? { session, me, refresh, signOut } : null), [session, me, refresh, signOut]);
  const context = useMemo(() => ({ account, signedIn }), [account, signedIn]);

  // The customer's tracking page needs no account at all.
  if (route.page === "track") return <TrackPage token={route.token} />;

  if (!session) {
    return <AccountCtx.Provider value={context}>{route.page === "register" ? <Register next={route.next} /> : <SignIn next={route.page === "signin" ? route.next : undefined} />}</AccountCtx.Provider>;
  }

  if (!me) return <div className="center">{error ? <Failed message={error} onRetry={() => refresh().catch((e: Error) => setError(e.message))} /> : <Loading />}</div>;

  if (route.page === "courier") return <AccountCtx.Provider value={context}><CourierApp /></AccountCtx.Provider>;

  const company = companyId ? me.companies.find((c) => c.id === companyId) ?? null : null;
  let content;
  if (route.page === "home") content = <Home />;
  else if (!company) content = <NotFound />;
  else content = <CompanyCtx.Provider value={company}><CompanyPage route={route} company={company} /></CompanyCtx.Provider>;

  return (
    <AccountCtx.Provider value={context}>
      <Shell route={route} company={company} wide={route.page === "live" || route.page === "zones"}>{content}</Shell>
    </AccountCtx.Provider>
  );
}
