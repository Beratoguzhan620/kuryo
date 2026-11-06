import { AlertTriangle, Inbox, Loader2 } from "lucide-react";
import type { ReactNode } from "react";

export function Loading({ label = "Loading" }: { label?: string }) {
  return (
    <div className="state" role="status">
      <Loader2 size={22} className="spin" />
      <span>{label}…</span>
    </div>
  );
}

export function SkeletonRows({ rows = 5, columns = 5 }: { rows?: number; columns?: number }) {
  return (
    <tbody aria-hidden="true">
      {Array.from({ length: rows }, (_, r) => (
        <tr key={r}>
          {Array.from({ length: columns }, (_, c) => (
            <td key={c}><div className="skeleton" style={{ width: `${40 + ((r * 7 + c * 13) % 50)}%` }} /></td>
          ))}
        </tr>
      ))}
    </tbody>
  );
}

export function Empty({ title, children, icon }: { title: string; children?: ReactNode; icon?: ReactNode }) {
  return (
    <div className="state">
      {icon ?? <Inbox size={24} />}
      <strong>{title}</strong>
      {children}
    </div>
  );
}

export function Failed({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="state error" role="alert">
      <AlertTriangle size={22} />
      <strong>Could not load this</strong>
      <span>{message}</span>
      {onRetry && <button className="button small" onClick={onRetry}>Try again</button>}
    </div>
  );
}
