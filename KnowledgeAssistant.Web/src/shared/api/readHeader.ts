import { type AxiosResponse } from 'axios';

/**
 * Reads a response header, treating absent and empty as the same thing.
 *
 * Axios types headers loosely enough that every call site would otherwise
 * repeat the same narrowing, and the correlation id is read on both the success
 * and the failure path.
 */
export function readHeader(response: AxiosResponse | undefined, name: string): string | undefined {
  const value = response?.headers[name] as unknown;
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}
