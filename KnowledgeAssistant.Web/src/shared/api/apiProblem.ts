/**
 * The API's failure contract, as it is actually sent on the wire.
 *
 * Every failure is an RFC 9457 problem document. Two shapes exist, and they
 * carry the machine-readable error code in different places — a distinction
 * worth encoding once here rather than rediscovering at each call site:
 *
 *   validation (400, 413)  ValidationProblemDetails, where `errors` is keyed by
 *                          error code, not by property name. The server groups
 *                          them that way deliberately: the code is the stable
 *                          contract, property names are an implementation
 *                          detail of the command.
 *
 *   everything else        ProblemDetails, where `title` *is* the error code
 *                          (ResultExtensions maps Error.Code onto Title) and
 *                          `detail` carries the human description.
 *
 * Both are stamped with a `traceId` extension, and the correlation id arrives
 * as a response header rather than in the body.
 */

/** Response headers the API sets on every response, success or failure. */
export const CORRELATION_ID_HEADER = 'x-correlation-id';
export const TRACE_ID_HEADER = 'x-trace-id';

/** A single machine-readable failure, paired with the server's description. */
export interface ApiErrorDetail {
  /** Stable error code, e.g. `Question.TooLong` or `Search.IndexUnavailable`. */
  readonly code: string;
  /** The server's human-readable description of this specific failure. */
  readonly description: string;
}

/**
 * A normalised failure. Every transport, validation, and infrastructure error
 * reaching the UI is one of these, so no component branches on Axios internals.
 */
export interface ApiProblem {
  /** HTTP status, or 0 when the request never reached the server. */
  readonly status: number;
  /** Problem title. For non-validation failures this is also the error code. */
  readonly title: string;
  /** The server's description, when it sent one. */
  readonly detail: string | undefined;
  /**
   * Every error code this failure carries, in the order the server listed them.
   * Empty only when the response was not a problem document at all.
   */
  readonly errors: readonly ApiErrorDetail[];
  /** Correlation id from the response header, for support tickets and logs. */
  readonly correlationId: string | undefined;
  /** Distributed trace id, for Azure Monitor. */
  readonly traceId: string | undefined;
  /** Seconds to wait before retrying, parsed from `Retry-After` when present. */
  readonly retryAfterSeconds: number | undefined;
  /** True when the request failed before a response was received. */
  readonly isNetworkError: boolean;
}

/** The wire shape of a non-validation problem document. */
interface ProblemDetailsBody {
  readonly title?: string;
  readonly detail?: string;
  readonly status?: number;
  readonly traceId?: string;
  readonly errors?: Record<string, readonly string[]>;
}

function isProblemDetailsBody(value: unknown): value is ProblemDetailsBody {
  return typeof value === 'object' && value !== null;
}

/**
 * Reads the error codes out of whichever problem shape was returned.
 *
 * A validation document's `errors` keys are the codes; a plain document's
 * `title` is the code. Callers branch on `code`, never on the message — the
 * messages are prose and will change.
 */
export function extractProblemErrors(body: unknown): readonly ApiErrorDetail[] {
  if (!isProblemDetailsBody(body)) {
    return [];
  }

  if (body.errors) {
    return Object.entries(body.errors).flatMap(([code, descriptions]) =>
      descriptions.map((description) => ({ code, description })),
    );
  }

  if (body.title) {
    return [{ code: body.title, description: body.detail ?? body.title }];
  }

  return [];
}
