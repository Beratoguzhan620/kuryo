import { createContext, useContext } from "react";
import type { Company, Me, Session } from "./types";

export interface Account {
  session: Session;
  me: Me;
  /** Reads the companies again, after one was created or the team changed. */
  refresh: () => Promise<Me>;
  signOut: () => void;
}

export const AccountCtx = createContext<{ account: Account | null; signedIn: (session: Session) => void } | null>(null);

export function useApp() {
  const value = useContext(AccountCtx);
  if (!value) throw new Error("useApp outside of the app");
  return value;
}

export function useAccount(): Account {
  const { account } = useApp();
  if (!account) throw new Error("useAccount before signing in");
  return account;
}

export const CompanyCtx = createContext<Company | null>(null);

export function useCompany(): Company {
  const value = useContext(CompanyCtx);
  if (!value) throw new Error("useCompany outside of a company page");
  return value;
}

export const isDispatcher = (company: Company) => company.role === "Dispatcher" || company.role === "Owner";
export const canSendOrders = (company: Company) => isDispatcher(company) || company.role === "Merchant";
