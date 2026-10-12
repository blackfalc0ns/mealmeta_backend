FROM mcr.microsoft.com/dotnet/sdk:9.0-noble

ENV DEBIAN_FRONTEND=noninteractive
WORKDIR /runner

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl git jq tar gzip lftp docker.io libicu74 libssl3 unzip sudo \
    && rm -rf /var/lib/apt/lists/*

RUN RUNNER_VERSION="$(curl -fsSL https://api.github.com/repos/actions/runner/releases/latest | jq -r .tag_name | tr -d v)" \
    && test -n "$RUNNER_VERSION" \
    && curl -fsSL "https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz" -o /tmp/runner.tar.gz \
    && tar -xzf /tmp/runner.tar.gz -C /runner \
    && rm /tmp/runner.tar.gz \
    && ./bin/installdependencies.sh

COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]
