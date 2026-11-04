# syntax=docker/dockerfile:1
# ExpenseHub report worker. Build context: repository root.
#   docker build -f sources/ExpenseHub.Worker/Dockerfile .

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.402
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.13

FROM ${SDK_IMAGE} AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY sources/ExpenseHub.Core/ExpenseHub.Core.csproj sources/ExpenseHub.Core/
COPY sources/ExpenseHub.Worker/ExpenseHub.Worker.csproj sources/ExpenseHub.Worker/
RUN dotnet restore sources/ExpenseHub.Worker/ExpenseHub.Worker.csproj
COPY sources/ExpenseHub.Core/ sources/ExpenseHub.Core/
COPY sources/ExpenseHub.Worker/ sources/ExpenseHub.Worker/
RUN dotnet publish sources/ExpenseHub.Worker/ExpenseHub.Worker.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
# /data is the SQLite volume shared with the web host (the web host owns migrations).
RUN mkdir -p /data && chown "$APP_UID" /data
COPY --from=build /app .
USER $APP_UID
ENV DOTNET_ENVIRONMENT=Production \
    ConnectionStrings__ExpenseHub="Data Source=/data/expensehub.db"
ENTRYPOINT ["dotnet", "ExpenseHub.Worker.dll"]
