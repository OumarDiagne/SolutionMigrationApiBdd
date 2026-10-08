# syntax=docker/dockerfile:1

# ---------- Étape 1 : compilation et publication (SDK, lourd, jeté ensuite) ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# On copie d'abord uniquement le .csproj : tant qu'il ne change pas, Docker réutilise
# le cache de la restauration des paquets (builds suivants beaucoup plus rapides).
COPY MigrationApiBdd/MigrationApiBdd.csproj MigrationApiBdd/
RUN dotnet restore MigrationApiBdd/MigrationApiBdd.csproj

COPY MigrationApiBdd/ MigrationApiBdd/
RUN dotnet publish MigrationApiBdd/MigrationApiBdd.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Étape 2 : image finale (runtime seul, légère) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# L'API écoute en HTTP sur 8080 dans le conteneur ; le HTTPS est à la charge
# d'un reverse proxy (nginx, Azure...) placé devant, pas du conteneur.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Ne pas tourner en root dans le conteneur (utilisateur « app » fourni par l'image).
USER $APP_UID

ENTRYPOINT ["dotnet", "MigrationApiBdd.dll"]
