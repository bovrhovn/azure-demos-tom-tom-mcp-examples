FROM node:22-bookworm-slim AS client
ARG NPM_REGISTRY=https://registry.npmjs.org/
WORKDIR /client
COPY src/TTSLN/TT.Web.LocationIntelligence/package*.json ./
RUN npm ci --registry=$NPM_REGISTRY --no-audit --no-fund
COPY src/TTSLN/TT.Web.LocationIntelligence/Client ./Client
COPY src/TTSLN/TT.Web.LocationIntelligence/vite.config.js ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG NUGET_SOURCE=https://api.nuget.org/v3/index.json
WORKDIR /source
COPY src/TTSLN/TT.Web.LocationIntelligence/TT.Web.LocationIntelligence.csproj ./
RUN dotnet restore --source $NUGET_SOURCE
COPY src/TTSLN/TT.Web.LocationIntelligence/ ./
COPY --from=client /client/wwwroot/dist ./wwwroot/dist
RUN dotnet publish -c Release -o /app --no-restore -p:SkipClientBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ReverseProxy__TrustAllProxies=true
EXPOSE 8080
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "TT.Web.LocationIntelligence.dll"]

FROM runtime AS development
USER root
RUN apt-get update && apt-get install -y --no-install-recommends gnupg \
    && curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft.gpg \
    && echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/repos/azure-cli/ noble main" > /etc/apt/sources.list.d/azure-cli.list \
    && apt-get update && apt-get install -y --no-install-recommends azure-cli \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_ENVIRONMENT=Development
USER app

FROM runtime AS production
