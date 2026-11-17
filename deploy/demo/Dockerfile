# The public demo in one container: the API serves the built web app from wwwroot, so a single free web service
# (Render) runs the whole thing next to a hosted PostgreSQL with PostGIS (Neon). Compose keeps nginx in front instead.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props Kuryo.slnx ./
COPY src/ src/
RUN dotnet publish src/Kuryo.Api/Kuryo.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
COPY --from=web /web/dist ./wwwroot
USER $APP_UID
ENTRYPOINT ["dotnet", "Kuryo.Api.dll"]
