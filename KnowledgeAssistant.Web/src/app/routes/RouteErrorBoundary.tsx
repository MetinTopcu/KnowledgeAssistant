import { AlertOctagon } from 'lucide-react';
import { isRouteErrorResponse, useRouteError } from 'react-router';

import { isApiRequestError } from '@/shared/api/ApiRequestError';
import { Button } from '@/shared/components/Button';
import { ErrorPage } from '@/shared/components/ErrorPage';

interface PresentedError {
  readonly title: string;
  readonly description: string;
  readonly code: string | undefined;
  readonly correlationId: string | undefined;
  readonly traceId: string | undefined;
}

/**
 * Turns whatever was thrown into something a user can act on.
 *
 * An {@link ApiRequestError} already carries the server's own code and
 * identifiers, so it is reported verbatim rather than flattened into a generic
 * apology — the code is the thing worth putting in a ticket.
 */
function present(error: unknown): PresentedError {
  if (isApiRequestError(error)) {
    const { problem } = error;

    if (problem.isNetworkError) {
      return {
        title: 'The service could not be reached',
        description:
          'The request did not complete. Check your connection, then try again — nothing was submitted.',
        code: undefined,
        correlationId: problem.correlationId,
        traceId: problem.traceId,
      };
    }

    return {
      title: 'This screen could not be loaded',
      description:
        problem.detail ?? 'The server rejected the request but sent no further description.',
      code: problem.errors[0]?.code,
      correlationId: problem.correlationId,
      traceId: problem.traceId,
    };
  }

  if (isRouteErrorResponse(error)) {
    return {
      title: 'This screen could not be loaded',
      description: error.statusText || 'The route failed to resolve.',
      code: String(error.status),
      correlationId: undefined,
      traceId: undefined,
    };
  }

  return {
    title: 'Something failed in the interface',
    description:
      error instanceof Error
        ? error.message
        : 'An unexpected error occurred while rendering this screen.',
    code: undefined,
    correlationId: undefined,
    traceId: undefined,
  };
}

export function RouteErrorBoundary() {
  const error = useRouteError();
  const presented = present(error);

  return (
    <ErrorPage
      icon={AlertOctagon}
      title={presented.title}
      description={presented.description}
      code={presented.code}
      correlationId={presented.correlationId}
      traceId={presented.traceId}
      actions={
        <Button
          variant="primary"
          onClick={() => {
            window.location.reload();
          }}
        >
          Reload
        </Button>
      }
    />
  );
}
