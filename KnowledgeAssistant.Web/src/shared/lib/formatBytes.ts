const UNITS = ['B', 'KB', 'MB', 'GB'] as const;
const STEP = 1024;

/**
 * Formats a byte count for display.
 *
 * Binary units, matching the server: its 20 MB limit is `20 * 1024 * 1024`, so
 * formatting in decimal megabytes here would show 21.0 MB for a file the server
 * calls exactly 20 MB and rejects.
 */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) {
    return '—';
  }

  let value = bytes;
  let unitIndex = 0;

  while (value >= STEP && unitIndex < UNITS.length - 1) {
    value /= STEP;
    unitIndex += 1;
  }

  // Whole bytes never need a decimal; anything scaled reads better with one.
  const formatted = unitIndex === 0 ? String(Math.round(value)) : value.toFixed(1);

  return `${formatted} ${UNITS[unitIndex] ?? 'B'}`;
}
