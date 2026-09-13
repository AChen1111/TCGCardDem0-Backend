import { useEffect } from "react";

export function usePoll(enabled: boolean, intervalMs: number, tick: () => void) {
  useEffect(() => {
    if (!enabled) {
      return;
    }

    const id = window.setInterval(tick, intervalMs);
    return () => window.clearInterval(id);
  }, [enabled, intervalMs, tick]);
}
