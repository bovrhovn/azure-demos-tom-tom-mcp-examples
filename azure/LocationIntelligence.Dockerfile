FROM node:22-bookworm-slim AS client
WORKDIR /src
COPY package*.json ./
ARG NPM_CONFIG_REGISTRY
RUN npm ci --no-audit --no-fund
COPY Client ./Client
COPY vite.config.js ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release --no-restore -p:SkipClientBuild=true -o /publish
COPY --from=client /src/wwwroot/dist /publish/wwwroot/dist

FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl jq \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /publish .
COPY verify-location-intelligence.sh /app/verify-location-intelligence.sh
RUN sed -i 's/\r$//' /app/verify-location-intelligence.sh
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "TT.Web.LocationIntelligence.dll"]
