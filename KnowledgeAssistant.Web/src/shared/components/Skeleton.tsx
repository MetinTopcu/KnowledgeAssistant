import { cn } from '@/shared/lib/cn';

interface SkeletonProps {
  /** Sizing utilities. Give each row a slightly different width. */
  readonly className?: string;
}

/**
 * A placeholder block for content whose layout is already known (§12.2).
 *
 * A slow opacity pulse, never a diagonal shimmer sweep — the sweep is the
 * single most recognisable tell of a generated interface, and it draws the eye
 * to the absence of content rather than away from it.
 */
export function Skeleton({ className }: SkeletonProps) {
  return (
    <div className={cn('animate-skeleton rounded-xs bg-canvas-inset', className)} aria-hidden />
  );
}
