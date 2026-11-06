import { Bike, Building2, ClipboardList, LogOut, Map, Menu, Radar, Store, Users, X } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { isDispatcher, useAccount } from "../context";
import { initials } from "../format";
import { linkProps, navigate, type Route } from "../router";
import type { Company } from "../types";

function UserMenu() {
  const account = useAccount();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);

  const go = (href: string) => (e: React.MouseEvent) => { e.preventDefault(); setOpen(false); navigate(href); };

  return (
    <div className="user-menu" ref={ref}>
      <button className="avatar-button" onClick={() => setOpen(!open)} aria-expanded={open} aria-haspopup="menu" aria-label={`Account: ${account.session.user.displayName}`}>
        <span className="avatar">{initials(account.session.user.displayName)}</span>
      </button>
      {open && (
        <div className="menu" role="menu">
          <div className="menu-head">
            <strong className="ellipsis block">{account.session.user.displayName}</strong>
            <span className="small muted ellipsis block">{account.session.user.email}</span>
          </div>
          <a role="menuitem" className="menu-item" href="/" onClick={go("/")}><Building2 size={15} />Companies</a>
          <a role="menuitem" className="menu-item" href="/courier" onClick={go("/courier")}><Bike size={15} />Courier app</a>
          <div className="menu-divider" />
          <button role="menuitem" className="menu-item" onClick={account.signOut}><LogOut size={15} />Sign out</button>
        </div>
      )}
    </div>
  );
}

const sections = [
  { key: "live", label: "Live", icon: Radar, pages: ["live"], dispatch: true },
  { key: "orders", label: "Orders", icon: ClipboardList, pages: ["orders", "order", "order-new"], dispatch: false },
  { key: "zones", label: "Zones", icon: Map, pages: ["zones"], dispatch: true },
  { key: "merchants", label: "Merchants", icon: Store, pages: ["merchants"], dispatch: true },
  { key: "team", label: "Team", icon: Users, pages: ["team"], dispatch: true },
] as const;

export function Brand({ light = false }: { light?: boolean }) {
  return <a className={`brand${light ? " light" : ""}`} {...linkProps("/")} aria-label="Kuryo, home"><span className="brand-mark"><Bike size={17} /></span>Kuryo</a>;
}

/** The office frame: the company's sections along the top, the page below. Merchants see their orders only. */
export function Shell({ route, company, children, wide = false }: { route: Route; company: Company | null; children: ReactNode; wide?: boolean }) {
  const [menu, setMenu] = useState(false);
  useEffect(() => setMenu(false), [route]);

  const visible = company ? sections.filter((s) => !s.dispatch || isDispatcher(company)) : [];
  const links = company && visible.length > 1 && (
    <>
      {visible.map((s) => {
        const active = (s.pages as readonly string[]).includes(route.page);
        const Icon = s.icon;
        return (
          <a key={s.key} {...linkProps(`/c/${company.id}/${s.key}`)} className={`top-link${active ? " active" : ""}`} aria-current={active ? "page" : undefined}>
            <Icon size={15} />{s.label}
          </a>
        );
      })}
    </>
  );

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar-inner">
          <Brand />
          {company && <span className="company-name ellipsis">{company.name}</span>}
          <nav className="top-links" aria-label="Main">{links}</nav>
          <div className="grow" />
          <UserMenu />
          {links && (
            <button className="icon-button only-sm" onClick={() => setMenu(!menu)} aria-label={menu ? "Close menu" : "Open menu"} aria-expanded={menu}>
              {menu ? <X size={20} /> : <Menu size={20} />}
            </button>
          )}
        </div>
        {menu && links && <nav className="mobile-links" aria-label="Main">{links}</nav>}
      </header>
      <main className={`page${wide ? " wide" : ""}`}>{children}</main>
    </div>
  );
}
