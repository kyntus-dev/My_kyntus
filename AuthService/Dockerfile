# Contexte Docker = racine du dépôt (voir docker-compose.yml service auth-backend).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Kyntus.Caching/Kyntus.Caching.csproj Kyntus.Caching/
COPY AuthService/Auth.Domain/Auth.Domain.csproj AuthService/Auth.Domain/
COPY AuthService/Auth.Application/Auth.Application.csproj AuthService/Auth.Application/
COPY AuthService/Auth.Infrastructure/Auth.Infrastructure.csproj AuthService/Auth.Infrastructure/
COPY AuthService/Auth.API/Auth.API.csproj AuthService/Auth.API/
RUN dotnet restore AuthService/Auth.API/Auth.API.csproj

COPY Kyntus.Caching/ Kyntus.Caching/
COPY AuthService/ AuthService/
RUN dotnet publish AuthService/Auth.API/Auth.API.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Auth.API.dll"]
