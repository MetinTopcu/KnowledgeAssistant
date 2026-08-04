import { useEffect, useState } from 'react';

const TICK_MS = 200;

/**
 * Seconds elapsed since `startedAt`, ticking while it is running.
 *
 * Used where an operation has no measurable progress: an elapsed count is the
 * honest alternative to a bar that advances on a timer, because it reports
 * something true — how long this has taken — instead of implying knowledge of
 * how much is left.
 */
export function useElapsedSeconds(startedAt: number | null, running: boolean): number {
  const [elapsed, setElapsed] = useState(0);

  useEffect(() => {
    if (startedAt === null || !running) {
      return;
    }

    const update = () => {
      setElapsed((Date.now() - startedAt) / 1000);
    };

    update();
    const timer = window.setInterval(update, TICK_MS);

    return () => {
      window.clearInterval(timer);
    };
  }, [startedAt, running]);

  return elapsed;
}
