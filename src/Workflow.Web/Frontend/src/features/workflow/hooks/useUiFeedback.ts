import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { StatusLevel, StatusState, ToastState } from "../../../shared/types/workflow";

/**
 * Что: централизованный UI feedback (status + toast).
 * Зачем: убрать инфраструктурный UI-state из orchestration-хука.
 * Как: управляет status, временным toast и вычисляет цвет статус-индикатора.
 */
export function useUiFeedback() {
  const [status, setStatus] = useState<StatusState>({ text: "Idle", level: "idle" });
  const [toast, setToast] = useState<ToastState>({ text: "", visible: false });
  const toastTimerRef = useRef<number | null>(null);

  useEffect(() => {
    return () => {
      if (toastTimerRef.current !== null) {
        window.clearTimeout(toastTimerRef.current);
      }
    };
  }, []);

  const setStatusMessage = useCallback((text: string, level: StatusLevel) => {
    setStatus({ text, level });
  }, []);

  const showToast = useCallback((message: string) => {
    setToast({ text: message, visible: true });
    if (toastTimerRef.current !== null) {
      window.clearTimeout(toastTimerRef.current);
    }

    toastTimerRef.current = window.setTimeout(() => {
      setToast((previous) => ({ ...previous, visible: false }));
    }, 1800);
  }, []);

  const statusDotColor = useMemo(() => getStatusDotColor(status.level), [status.level]);

  return {
    status,
    toast,
    statusDotColor,
    setStatusMessage,
    showToast
  };
}

function getStatusDotColor(level: StatusLevel): string {
  if (level === "active") {
    return "#f97316";
  }

  if (level === "error") {
    return "#dc2626";
  }

  return "#9ca3af";
}
