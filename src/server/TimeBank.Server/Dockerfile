FROM node:20-alpine AS web
ARG NPM_REGISTRY=https://registry.npmjs.org
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm config set registry "$NPM_REGISTRY" && npm ci --no-fund --no-audit
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/server/TimeBank.Server/TimeBank.Server.csproj ./
RUN dotnet restore
COPY src/server/TimeBank.Server/ ./
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=web /web/dist/ ./wwwroot/
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "TimeBank.Server.dll"]
