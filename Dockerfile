FROM node:24-alpine AS client-assets
WORKDIR /src/src/GitCandy/ClientApp

COPY src/GitCandy/ClientApp/package.json src/GitCandy/ClientApp/package-lock.json ./
RUN npm ci --ignore-scripts
COPY src/GitCandy/ClientApp/app.css src/GitCandy/ClientApp/app.js ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY . .
COPY --from=client-assets /src/src/GitCandy/wwwroot/assets src/GitCandy/wwwroot/assets
RUN dotnet restore GitCandy.slnx
RUN dotnet publish src/GitCandy/GitCandy.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -p:SkipClientAssetsBuild=true \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl git \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p /var/lib/gitcandy/repositories /var/lib/gitcandy/cache /var/lib/gitcandy/logs \
    && mkdir -p /var/lib/gitcandy/data-protection-keys \
    && chown -R "$APP_UID:$APP_UID" /var/lib/gitcandy

VOLUME ["/var/lib/gitcandy"]
EXPOSE 8080 2222
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "GitCandy.dll"]
