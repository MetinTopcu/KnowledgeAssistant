"""Development-only stand-in for the Azure managed identity token endpoint.

The API container in docker-compose has no Azure CLI and must not get one: it
is the production image. In Development it is configured to use
ManagedIdentityCredential (Azure:Credential:DevelopmentCredential =
ManagedIdentity), and this process answers that credential with tokens for the
developer's own `az login`, using the protocol the platform identity endpoint
speaks (IDENTITY_ENDPOINT / IDENTITY_HEADER, api-version 2019-08-01).

What it deliberately does not do:
  * run anywhere but a developer machine — it hands out the developer's tokens
    to any caller on the compose network that knows IDENTITY_HEADER;
  * accept a port binding to the host — docker-compose exposes it only on the
    internal network;
  * mint tokens for arbitrary audiences — only the resources the API calls;
  * log or persist tokens — they live in memory until shortly before expiry.

Standard library only, so the Azure CLI image needs nothing installed.
"""

import hmac
import json
import os
import shutil
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

HEADER_NAME = "X-IDENTITY-HEADER"
API_VERSION = "2019-08-01"

# The audiences the API requests: Search, Foundry/OpenAI and Document
# Intelligence, Blob, and Azure Monitor export. Anything else is refused.
DEFAULT_ALLOWED = ",".join([
    "https://search.azure.com",
    "https://cognitiveservices.azure.com",
    "https://ai.azure.com",
    "https://storage.azure.com",
    "https://monitor.azure.com",
])

# A token is reused until this many seconds before it expires.
REFRESH_MARGIN_SECONDS = 300

_cache: dict[str, tuple[str, int]] = {}
_cache_lock = threading.Lock()


def _az_executable() -> str:
    found = shutil.which("az")
    if not found:
        sys.exit("token-proxy: 'az' was not found on PATH.")
    return found


def _normalise(resource: str) -> str:
    return resource.rstrip("/")


def _acquire(resource: str) -> tuple[str, int]:
    with _cache_lock:
        cached = _cache.get(resource)
        if cached and cached[1] - REFRESH_MARGIN_SECONDS > time.time():
            return cached

    completed = subprocess.run(
        [_az_executable(), "account", "get-access-token", "--resource", resource, "--output", "json"],
        capture_output=True,
        text=True,
        timeout=60,
        check=False,
    )
    if completed.returncode != 0:
        # stderr carries az's own explanation ("run az login"); it holds no token.
        raise RuntimeError(completed.stderr.strip() or "az account get-access-token failed")

    payload = json.loads(completed.stdout)
    token = payload["accessToken"]
    expires_on = int(payload["expires_on"])

    with _cache_lock:
        _cache[resource] = (token, expires_on)

    return token, expires_on


class Handler(BaseHTTPRequestHandler):
    server_version = "azure-token-proxy"

    def do_GET(self) -> None:  # noqa: N802 - http.server naming
        expected = os.environ["IDENTITY_HEADER"]
        supplied = self.headers.get(HEADER_NAME, "")
        if not hmac.compare_digest(supplied.encode(), expected.encode()):
            self._reply(401, {"error": "invalid_request", "error_description": f"{HEADER_NAME} is missing or wrong."})
            return

        query = parse_qs(urlparse(self.path).query)
        resource = _normalise((query.get("resource") or [""])[0])
        api_version = (query.get("api-version") or [""])[0]

        if api_version != API_VERSION:
            self._reply(400, {"error": "invalid_request", "error_description": f"api-version must be {API_VERSION}."})
            return

        allowed = {_normalise(r) for r in os.environ.get("ALLOWED_RESOURCES", DEFAULT_ALLOWED).split(",") if r}
        if resource not in allowed:
            self._reply(403, {"error": "invalid_resource", "error_description": f"Resource '{resource}' is not allowed."})
            return

        try:
            token, expires_on = _acquire(resource)
        except Exception as error:  # the caller needs the reason; no token is in it
            self._reply(500, {"error": "token_unavailable", "error_description": str(error)})
            return

        self._reply(200, {
            "access_token": token,
            "expires_on": str(expires_on),
            "resource": resource,
            "token_type": "Bearer",
        })

    def _reply(self, status: int, body: dict) -> None:
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        try:
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            # The caller gave up first — the API's readiness probe has a 5 s
            # budget and the first `az` call can exceed it. The token is cached,
            # so the next request is answered at once.
            pass

    def log_message(self, format: str, *args) -> None:  # noqa: A002 - http.server signature
        # Request line and status only; the query holds a resource URI, never a token.
        sys.stderr.write("token-proxy: " + (format % args) + "\n")


def main() -> None:
    if not os.environ.get("IDENTITY_HEADER"):
        sys.exit("token-proxy: IDENTITY_HEADER must be set.")

    host = os.environ.get("TOKEN_PROXY_HOST", "0.0.0.0")
    port = int(os.environ.get("TOKEN_PROXY_PORT", "8079"))
    ThreadingHTTPServer((host, port), Handler).serve_forever()


if __name__ == "__main__":
    main()
