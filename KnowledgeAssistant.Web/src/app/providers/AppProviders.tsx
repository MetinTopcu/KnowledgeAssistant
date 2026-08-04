import { type ReactNode } from 'react';

import { QueryProvider } from '@/app/providers/QueryProvider';
import { ThemeProvider } from '@/app/providers/ThemeProvider';

interface AppProvidersProps {
  readonly children: ReactNode;
}

/**
 * The application's cross-cutting context, composed in one place so that the
 * order is reviewable rather than scattered across the entry point.
 */
export function AppProviders({ children }: AppProvidersProps) {
  return (
    <ThemeProvider>
      <QueryProvider>{children}</QueryProvider>
    </ThemeProvider>
  );
}
