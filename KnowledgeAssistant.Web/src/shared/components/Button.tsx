import { Slot } from '@radix-ui/react-slot';
import { cva, type VariantProps } from 'class-variance-authority';
import { type ButtonHTMLAttributes, type ReactNode } from 'react';

import { cn } from '@/shared/lib/cn';

/**
 * Button variants per docs/DESIGN.md §8.3.
 *
 * No variant carries a shadow and none exceeds a 4px radius. Separation comes
 * from a 1px border, which is why `default` reads as a control rather than as a
 * tinted rectangle.
 */
const buttonVariants = cva(
  cn(
    'inline-flex items-center justify-center gap-2 rounded-sm border font-medium whitespace-nowrap',
    'transition-colors duration-instant ease-standard',
    'disabled:pointer-events-none disabled:opacity-50',
  ),
  {
    variants: {
      variant: {
        primary:
          'border-transparent bg-accent-emphasis text-fg-on-emphasis hover:brightness-110 active:brightness-95',
        default:
          'border-border-default bg-canvas-base text-fg-default hover:bg-canvas-hover active:bg-canvas-inset',
        subtle:
          'border-transparent bg-transparent text-fg-muted hover:bg-canvas-hover hover:text-fg-default',
        danger:
          'border-transparent bg-danger-emphasis text-fg-on-emphasis hover:brightness-110 active:brightness-95',
      },
      size: {
        sm: 'h-control-sm px-2 text-ui',
        md: 'h-control-md px-3 text-ui',
        lg: 'h-control-lg px-4 text-body',
        icon: 'size-control-sm p-0',
      },
    },
    defaultVariants: {
      variant: 'default',
      size: 'md',
    },
  },
);

export interface ButtonProps
  extends ButtonHTMLAttributes<HTMLButtonElement>, VariantProps<typeof buttonVariants> {
  readonly children?: ReactNode;
  /**
   * Renders the child element with the button's styling instead of emitting a
   * `<button>`. Used for router links, which must stay anchors so that middle
   * click, Ctrl+click, and "copy link address" keep working.
   */
  readonly asChild?: boolean;
}

export function Button({
  className,
  variant,
  size,
  asChild = false,
  type = 'button',
  ...props
}: ButtonProps) {
  if (asChild) {
    return <Slot className={cn(buttonVariants({ variant, size }), className)} {...props} />;
  }

  return (
    <button type={type} className={cn(buttonVariants({ variant, size }), className)} {...props} />
  );
}
