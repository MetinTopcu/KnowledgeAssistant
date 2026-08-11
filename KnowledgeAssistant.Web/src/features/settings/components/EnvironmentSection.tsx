import { SettingsSectionPanel } from '@/features/settings/components/SettingsSectionPanel';
import { describeTimeout, resolveApiBaseUrl } from '@/features/settings/model/environmentSettings';
import { Alert } from '@/shared/components/Alert';
import { CopyButton } from '@/shared/components/CopyButton';
import { DefinitionList, DefinitionRow } from '@/shared/components/DefinitionList';
import { env } from '@/shared/config/env';

interface ValueProps {
  readonly value: string;
  /** What the copy action puts on the clipboard, when it differs from the display. */
  readonly copyValue?: string;
  readonly copyLabel: string;
}

/** A configured value in mono, with the copy action §6.8 asks for on every row. */
function Value({ value, copyValue, copyLabel }: ValueProps) {
  return (
    <div className="flex min-w-0 items-center gap-1">
      <span className="min-w-0 truncate font-mono text-mono-body text-fg-default" title={value}>
        {value}
      </span>
      <CopyButton value={copyValue ?? value} label={copyLabel} />
    </div>
  );
}

/**
 * Environment.
 *
 * Read-only, and read-only in the strong sense: these values were fixed when
 * the bundle was built, and nothing on this page could change them if it tried.
 * docs/DESIGN.md §6.8 also asks for deployment names — those live in the
 * server's configuration, no endpoint publishes them, and this page will not
 * invent them.
 */
export function EnvironmentSection() {
  const apiBaseUrl = resolveApiBaseUrl(env.apiBaseUrl, window.location.origin);

  return (
    <SettingsSectionPanel slug="environment">
      <DefinitionList label="Configuration this client was built with">
        <DefinitionRow
          term="API base URL"
          note={
            apiBaseUrl.isSameOrigin ? (
              <>
                Relative, so the API is served from this page&apos;s own origin — requests resolve
                to <span className="font-mono">{apiBaseUrl.effective}</span>. That is what both the
                dev proxy and a co-hosted deployment provide.
              </>
            ) : (
              <>
                Absolute, so this client is hosted separately from the API and every request is
                cross-origin. Copy gives you{' '}
                <span className="font-mono">{apiBaseUrl.effective}</span>.
              </>
            )
          }
        >
          <Value
            value={apiBaseUrl.configured}
            copyValue={apiBaseUrl.effective}
            copyLabel="Copy URL"
          />
        </DefinitionRow>

        <DefinitionRow
          term="Request timeout"
          note={`${describeTimeout(env.apiTimeoutMs)}, applied to every call. Agent runs are the slow path — the agent may search several times before it answers — so this is set for the worst case rather than the usual one.`}
        >
          <Value value={`${env.apiTimeoutMs.toLocaleString()} ms`} copyLabel="Copy timeout" />
        </DefinitionRow>

        <DefinitionRow
          term="Environment"
          note="Also shown at the left of the status bar, so which stack you are on is answerable without opening this page."
        >
          <Value value={env.environment} copyLabel="Copy environment" />
        </DefinitionRow>

        <DefinitionRow
          term="Azure region"
          note="Where this deployment runs. It is the first thing the status bar drops on a narrow window; it is always here."
        >
          <Value value={env.azureRegion} copyLabel="Copy region" />
        </DefinitionRow>

        <DefinitionRow
          term="Application version"
          note="The build identifier of this client. Quote it in a bug report alongside the correlation id of the run that failed — together they identify which code produced which request."
        >
          <Value value={env.appVersion} copyLabel="Copy version" />
        </DefinitionRow>
      </DefinitionList>

      <Alert variant="info" title="Nothing here is a secret, because none exists">
        Every value on this page is compiled into the JavaScript this browser downloaded, so it was
        never private and could never hold a credential. The API authenticates to Azure with a
        managed identity of its own; this client authenticates to the API, never to Azure. Server
        configuration — deployment names, model names, index names — is not published by any
        endpoint, so it is absent here rather than guessed at.
      </Alert>
    </SettingsSectionPanel>
  );
}
