import { type ApiProblem } from '@/shared/api/apiProblem';

/**
 * The single error type every API call rejects with.
 *
 * Components and hooks never see an `AxiosError`: the response interceptor
 * converts one into this before it escapes the transport layer. That keeps the
 * choice of HTTP client out of the rest of the application, and gives error
 * handling one shape to match on instead of three.
 */
export class ApiRequestError extends Error {
  readonly problem: ApiProblem;

  constructor(problem: ApiProblem) {
    super(problem.detail ?? problem.title);
    this.name = 'ApiRequestError';
    this.problem = problem;
  }
}

/** Narrows an unknown rejection to the transport layer's error type. */
export function isApiRequestError(error: unknown): error is ApiRequestError {
  return error instanceof ApiRequestError;
}
