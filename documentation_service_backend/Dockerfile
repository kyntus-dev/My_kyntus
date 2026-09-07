# Contexte Docker = racine du dépôt (voir docker-compose.yml service documentation-backend).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Messaging.Contracts/Messaging.Contracts.csproj Messaging.Contracts/
COPY Kyntus.Identity.Jwt/Kyntus.Identity.Jwt.csproj Kyntus.Identity.Jwt/
COPY Kyntus.Iam/Kyntus.Iam.csproj Kyntus.Iam/
COPY Kyntus.Caching/Kyntus.Caching.csproj Kyntus.Caching/
COPY documentation_service_backend/Documentation.Contracts/Documentation.Contracts.csproj documentation_service_backend/Documentation.Contracts/
COPY documentation_service_backend/Documentation.Domain/Documentation.Domain.csproj documentation_service_backend/Documentation.Domain/
COPY documentation_service_backend/Documentation.Application/Documentation.Application.csproj documentation_service_backend/Documentation.Application/
COPY documentation_service_backend/Documentation.Infrastructure/Documentation.Infrastructure.csproj documentation_service_backend/Documentation.Infrastructure/
COPY documentation_service_backend/Documentation.API/Documentation.API.csproj documentation_service_backend/Documentation.API/
RUN dotnet restore documentation_service_backend/Documentation.API/Documentation.API.csproj

COPY Messaging.Contracts/ Messaging.Contracts/
COPY Kyntus.Identity.Jwt/ Kyntus.Identity.Jwt/
COPY Kyntus.Iam/ Kyntus.Iam/
COPY Kyntus.Caching/ Kyntus.Caching/
COPY documentation_service_backend/Documentation.Contracts/ documentation_service_backend/Documentation.Contracts/
COPY documentation_service_backend/ documentation_service_backend/
RUN dotnet publish documentation_service_backend/Documentation.API/Documentation.API.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Documentation.API.dll"]
