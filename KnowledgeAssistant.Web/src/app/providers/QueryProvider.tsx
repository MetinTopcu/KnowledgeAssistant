import { QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode } from 'react';

import { queryClient } from '@/shared/api/queryClient';

interface QueryProviderProps {
  readonly children: ReactNode;
}

export function QueryProvider({ children }: QueryProviderProps) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
