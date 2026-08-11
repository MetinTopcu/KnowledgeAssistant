import { useCallback, useEffect, useRef, useState } from 'react';

/** How long the confirmation stays up before the control returns to rest. */
const CONFIRMATION_MS = 1_200;

interface ClipboardState {
  readonly copied: boolean;
  readonly copy: (text: string) => Promise<void>;
}

/**
 * Copies text and reports a short confirmation.
 *
 * `navigator.clipboard` rejects rather than throwing synchronously — a denied
 * permission or an insecure origin both land there — so a failure leaves the
 * control at rest instead of claiming a copy that did not happen.
 */
export function useCopyToClipboard(): ClipboardState {
  const [copied, setCopied] = useState(false);
  const timerRef = useRef<number | undefined>(undefined);

  useEffect(
    () => () => {
      if (timerRef.current !== undefined) {
        window.clearTimeout(timerRef.current);
      }
    },
    [],
  );

  const copy = useCallback(async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);

      if (timerRef.current !== undefined) {
        window.clearTimeout(timerRef.current);
      }

      timerRef.current = window.setTimeout(() => {
        setCopied(false);
      }, CONFIRMATION_MS);
    } catch {
      setCopied(false);
    }
  }, []);

  return { copied, copy };
}
