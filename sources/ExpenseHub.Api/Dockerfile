# syntax=docker/dockerfile:1
# ExpenseHub web host. Build context: repository root.
#   docker build -f sources/ExpenseHub.Api/Dockerfile .

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.402
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.13

FROM ${SDK_IMAGE} AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY sources/ExpenseHub.Core/ExpenseHub.Core.csproj sources/ExpenseHub.Core/
COPY sources/ExpenseHub.Api/ExpenseHub.Api.csproj sources/ExpenseHub.Api/
RUN dotnet restore sources/ExpenseHub.Api/ExpenseHub.Api.csproj
COPY sources/ExpenseHub.Core/ sources/ExpenseHub.Core/
COPY sources/ExpenseHub.Api/ sources/ExpenseHub.Api/
RUN dotnet publish sources/ExpenseHub.Api/ExpenseHub.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM ${RUNTIME_IMAGE} AS runtime
# The commit identifies the delivered version: CI passes the full SHA, local builds pass "local".
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="expensehub-api" \
      org.opencontainers.image.source="https://github.com/gh-johnny/fiap-expensehub" \
      org.opencontainers.image.revision="$GIT_SHA"
WORKDIR /app
# /data holds the shared SQLite database and the Data Protection keys (mounted as a volume).
RUN mkdir -p /data && chown "$APP_UID" /data
COPY --from=build /app .
USER $APP_UID
ENV APP_VERSION=$GIT_SHA \
    ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__ExpenseHub="Data Source=/data/expensehub.db" \
    DataProtection__KeysPath=/data/keys
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=5 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\n\\r\\n' >&3 && head -1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "ExpenseHub.Api.dll"]
