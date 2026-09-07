# Contexte Docker = racine du dépôt (voir docker-compose.yml service parrainage-backend).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Messaging.Contracts/Messaging.Contracts.csproj Messaging.Contracts/
COPY Kyntus.Identity.Jwt/Kyntus.Identity.Jwt.csproj Kyntus.Identity.Jwt/
COPY Kyntus.Iam/Kyntus.Iam.csproj Kyntus.Iam/
COPY Kyntus.Caching/Kyntus.Caching.csproj Kyntus.Caching/
COPY parrainage_service_backend/Parrainage.Domain/Parrainage.Domain.csproj parrainage_service_backend/Parrainage.Domain/
COPY parrainage_service_backend/Parrainage.Application/Parrainage.Application.csproj parrainage_service_backend/Parrainage.Application/
COPY parrainage_service_backend/Parrainage.Infrastructure/Parrainage.Infrastructure.csproj parrainage_service_backend/Parrainage.Infrastructure/
COPY parrainage_service_backend/Parrainage.API/Parrainage.API.csproj parrainage_service_backend/Parrainage.API/
RUN dotnet restore parrainage_service_backend/Parrainage.API/Parrainage.API.csproj

COPY Messaging.Contracts/ Messaging.Contracts/
COPY Kyntus.Identity.Jwt/ Kyntus.Identity.Jwt/
COPY Kyntus.Iam/ Kyntus.Iam/
COPY Kyntus.Caching/ Kyntus.Caching/
COPY parrainage_service_backend/ parrainage_service_backend/
RUN dotnet publish parrainage_service_backend/Parrainage.API/Parrainage.API.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Parrainage.API.dll"]
