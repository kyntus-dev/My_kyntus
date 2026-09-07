# Contexte Docker = racine du dépôt (Messaging.Contracts hors dossier FormationService).
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY Messaging.Contracts/Messaging.Contracts.csproj Messaging.Contracts/
COPY Shared.Kernel/Shared.Kernel.csproj Shared.Kernel/
COPY Kyntus.Identity.Jwt/Kyntus.Identity.Jwt.csproj Kyntus.Identity.Jwt/
COPY Kyntus.Caching/Kyntus.Caching.csproj Kyntus.Caching/
COPY FormationService/projetformationconges/Formation.API.csproj FormationService/projetformationconges/
COPY FormationService/src/Services/Formation/Formation.Domain/Formation.Domain.csproj FormationService/src/Services/Formation/Formation.Domain/
COPY FormationService/src/Services/Formation/Formation.Application/Formation.Application.csproj FormationService/src/Services/Formation/Formation.Application/
COPY FormationService/src/Services/Formation/Formation.Infrastructure/Formation.Infrastructure.csproj FormationService/src/Services/Formation/Formation.Infrastructure/

RUN dotnet restore FormationService/projetformationconges/Formation.API.csproj

COPY Messaging.Contracts/ Messaging.Contracts/
COPY Shared.Kernel/ Shared.Kernel/
COPY Kyntus.Identity.Jwt/ Kyntus.Identity.Jwt/
COPY Kyntus.Caching/ Kyntus.Caching/
COPY FormationService/ FormationService/

WORKDIR /src/FormationService/projetformationconges
RUN dotnet build Formation.API.csproj -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish Formation.API.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Formation.API.dll"]
