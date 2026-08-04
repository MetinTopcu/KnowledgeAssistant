import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/**
 * Joins class names and resolves Tailwind conflicts so the last value wins.
 *
 * Without the merge step, a caller passing `p-4` to a component that already
 * applies `p-2` gets whichever CSS rule the build happens to order last — a
 * bug that appears only after an unrelated change reorders the stylesheet.
 */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
