import { FileQuestion } from 'lucide-react';
import { Link, useLocation } from 'react-router';

import { routePaths } from '@/app/router/routePaths';
import { Button } from '@/shared/components/Button';
import { ErrorPage } from '@/shared/components/ErrorPage';

export function NotFoundRoute() {
  const { pathname } = useLocation();

  return (
    <ErrorPage
      icon={FileQuestion}
      tone="neutral"
      title="This page does not exist"
      description={`Nothing is routed at ${pathname}. It may have been renamed, or the link that brought you here may be out of date.`}
      actions={
        <Button variant="primary" asChild>
          <Link to={routePaths.ask}>Go to Ask</Link>
        </Button>
      }
    />
  );
}
