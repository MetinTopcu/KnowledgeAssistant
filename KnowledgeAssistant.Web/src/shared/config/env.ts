import { z } from 'zod';

/**
 * The environment this client was built for.
 *
 * Validated at module load rather than at first use, mirroring the server's
 * decision to validate every configuration section at startup so that a
 * misconfigured deploy fails visibly instead of on first request.
 */
const environmentSchema = z.object({
  /**
   * Origin the API is served from. `/` means "same origin as this app", which
   * is what both the dev proxy and a co-hosted production deploy provide.
   */
  VITE_API_BASE_URL: z.string().min(1),

  /** Shown in the status bar so a user always knows which stack they are on. */
  VITE_ENVIRONMENT: z.enum(['development', 'staging', 'production']),

  /** Azure region, shown beside the environment. */
  VITE_AZURE_REGION: z.string().min(1),

  /** Build identifier, quoted in bug reports alongside the correlation id. */
  VITE_APP_VERSION: z.string().min(1),

  /** Milliseconds before an in-flight request is abandoned. */
  VITE_API_TIMEOUT_MS: z.coerce.number().int().positive(),
});

const parsed = environmentSchema.safeParse(import.meta.env);

if (!parsed.success) {
  const issues = parsed.error.issues
    .map((issue) => `  ${issue.path.join('.')}: ${issue.message}`)
    .join('\n');

  throw new Error(
    `Invalid environment configuration. Check your .env file.\n${issues}\n\n` +
      'See .env.example for every variable this client reads.',
  );
}

export const env = {
  apiBaseUrl: parsed.data.VITE_API_BASE_URL,
  environment: parsed.data.VITE_ENVIRONMENT,
  azureRegion: parsed.data.VITE_AZURE_REGION,
  appVersion: parsed.data.VITE_APP_VERSION,
  apiTimeoutMs: parsed.data.VITE_API_TIMEOUT_MS,
} as const;
