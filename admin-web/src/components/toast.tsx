"use client";

import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from "react";
import { CheckIcon, CloseIcon } from "./icons";

interface Toast {
  id: number;
  text: string;
}

const ToastContext = createContext<(text: string) => void>(() => {});

/** Toasts confirm with the past tense of the button ("Business suspended"). */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => setToasts((ts) => ts.filter((t) => t.id !== id)), []);
  const push = useCallback(
    (text: string) => {
      const id = nextId.current++;
      setToasts((ts) => [...ts.slice(-2), { id, text }]);
      setTimeout(() => dismiss(id), 5000);
    },
    [dismiss],
  );
  const value = useMemo(() => push, [push]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div aria-live="polite" role="status" className="pointer-events-none fixed inset-x-0 bottom-20 z-50 flex flex-col items-center gap-2 px-4 md:bottom-6">
        {toasts.map((t) => (
          <div key={t.id} className="pointer-events-auto flex max-w-md items-center gap-2 rounded-panel bg-graphite py-2 pr-1 pl-3 text-sm text-white shadow-dialog animate-dialog-in">
            <CheckIcon className="h-4 w-4 text-caution" />
            <span className="min-w-0 flex-1">{t.text}</span>
            <button type="button" onClick={() => dismiss(t.id)} className="inline-flex h-8 w-8 items-center justify-center rounded-control text-on-graphite hover:text-white" aria-label="Dismiss">
              <CloseIcon className="h-4 w-4" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  return useContext(ToastContext);
}
