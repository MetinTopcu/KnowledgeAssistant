import axios, { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';

import { ApiRequestError } from '@/shared/api/ApiRequestError';
import {
  CORRELATION_ID_HEADER,
  TRACE_ID_HEADER,
  extractProblemErrors,
  type ApiProblem,
} from '@/shared/api/apiProblem';
import { env } from '@/shared/config/env';

/**
 * The HTTP client every feature uses.
 *
 * Two responsibilities, both of which exist so that no feature has to remember
 * them: stamp an outgoing correlation id, and normalise every failure into an
 * {@link ApiRequestError}.
 */
export const httpClient = axios.create({
  baseURL: env.apiBaseUrl,
  timeout: env.apiTimeoutMs,
  headers: { Accept: 'application/json' },
});

/**
 * Generates the id that ties this request to the server's logs and traces.
 *
 * The server mints one when the client does not send it, so this is not
 * required — but an id chosen here is known before the response arrives, which
 * means a request that times out or never connects can still be reported.
 */
function newCorrelationId(): string {
  return crypto.randomUUID();
}

httpClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  config.headers.set(CORRELATION_ID_HEADER, newCorrelationId());
  return config;
});

/**
 * Parses `Retry-After`, which is either a delay in seconds or an HTTP date.
 *
 * Returns undefined rather than a guess when the header is absent or
 * unparseable: a countdown shown against an invented deadline is worse than no
 * countdown at all.
 */
function parseRetryAfter(value: string | undefined): number | undefined {
  if (!value) {
    return undefined;
  }

  const seconds = Number(value);
  if (Number.isFinite(seconds) && seconds >= 0) {
    return Math.ceil(seconds);
  }

  const deadline = Date.parse(value);
  if (Number.isNaN(deadline)) {
    return undefined;
  }

  return Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
}

function headerValue(response: AxiosResponse | undefined, name: string): string | undefined {
  const value = response?.headers[name] as unknown;
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function requestCorrelationId(error: AxiosError): string | undefined {
  const value = error.config?.headers.get(CORRELATION_ID_HEADER);
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function bodyString(body: unknown, key: string): string | undefined {
  if (typeof body !== 'object' || body === null || !(key in body)) {
    return undefined;
  }

  const value = (body as Record<string, unknown>)[key];
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function toApiProblem(error: AxiosError): ApiProblem {
  const { response } = error;
  const body: unknown = response?.data;
  const errors = extractProblemErrors(body);

  return {
    status: response?.status ?? 0,
    // The server's own title and detail, verbatim. Neither is replaced by the
    // first error code: on a validation document the title is "One or more
    // validation errors occurred." and the codes live in `errors`, so
    // substituting one for the other would report the document as saying
    // something it does not say.
    title: bodyString(body, 'title') ?? error.message,
    detail: bodyString(body, 'detail'),
    errors,
    // Falls back to the id this client sent, so a request that never reached
    // the server is still reportable against something the server can search.
    correlationId: headerValue(response, CORRELATION_ID_HEADER) ?? requestCorrelationId(error),
    traceId: headerValue(response, TRACE_ID_HEADER) ?? bodyString(body, 'traceId'),
    retryAfterSeconds: parseRetryAfter(headerValue(response, 'retry-after')),
    isNetworkError: response === undefined,
  };
}

httpClient.interceptors.response.use(
  (response) => response,
  (error: unknown) => {
    if (error instanceof AxiosError) {
      return Promise.reject(new ApiRequestError(toApiProblem(error)));
    }

    return Promise.reject(error instanceof Error ? error : new Error(String(error)));
  },
);
