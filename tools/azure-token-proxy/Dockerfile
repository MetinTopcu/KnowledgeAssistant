# Development-only token endpoint for docker-compose. NOT part of any deployed
# system: it serves the developer's own Azure CLI login to the API container
# through the managed identity protocol. See token_proxy.py and CONFIGURATION.md.
#
# The Azure CLI lives here, in a separate dev-only image, precisely so that it
# never enters the API's runtime image.

# Pinned for the same reason the API's base images are: a CLI upgrade should be
# a reviewed change, not a side effect of a rebuild.
FROM mcr.microsoft.com/azure-cli:2.90.0

COPY token_proxy.py /opt/token-proxy/token_proxy.py

ENV TOKEN_PROXY_PORT=8079
EXPOSE 8079

CMD ["python3", "/opt/token-proxy/token_proxy.py"]
