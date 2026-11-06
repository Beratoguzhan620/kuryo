import { X } from "lucide-react";
import { useEffect, useRef, type ReactNode } from "react";

/** A native modal dialog: focus is trapped, Escape closes it. */
export function Dialog({ title, onClose, children, wide = false }: { title: string; onClose: () => void; children: ReactNode; wide?: boolean }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    if (dialog && !dialog.open) dialog.showModal();
    return () => dialog?.close();
  }, []);

  return (
    <dialog ref={ref} className={wide ? "wide" : undefined} onCancel={(e) => { e.preventDefault(); onClose(); }} aria-label={title}>
      <div className="dialog-body">
        <div className="dialog-head">
          <h2>{title}</h2>
          <button type="button" className="icon-button" onClick={onClose} aria-label="Close"><X size={18} /></button>
        </div>
        {children}
      </div>
    </dialog>
  );
}
