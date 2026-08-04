import { RouterProvider } from 'react-router';

import { AppProviders } from '@/app/providers/AppProviders';
import { appRouter } from '@/app/router/appRouter';

export function App() {
  return (
    <AppProviders>
      <RouterProvider router={appRouter} />
    </AppProviders>
  );
}
