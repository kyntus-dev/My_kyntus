# Contexte Docker = racine du dépôt (voir docker-compose.yml service conge-backend).
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Messaging.Contracts/Messaging.Contracts.csproj Messaging.Contracts/
COPY Kyntus.Identity.Jwt/Kyntus.Identity.Jwt.csproj Kyntus.Identity.Jwt/
COPY Kyntus.Iam/Kyntus.Iam.csproj Kyntus.Iam/
COPY Kyntus.Caching/Kyntus.Caching.csproj Kyntus.Caching/
COPY FormationService/Conge.API/Conge.API.csproj FormationService/Conge.API/
COPY FormationService/Conge.Application/Conge.Application.csproj FormationService/Conge.Application/
COPY FormationService/Conge.Domain/Conge.Domain.csproj FormationService/Conge.Domain/
COPY FormationService/Conge.Infrastructure/Conge.Infrastructure.csproj FormationService/Conge.Infrastructure/

RUN dotnet restore "FormationService/Conge.API/Conge.API.csproj"

COPY Messaging.Contracts/ Messaging.Contracts/
COPY Kyntus.Identity.Jwt/ Kyntus.Identity.Jwt/
COPY Kyntus.Iam/ Kyntus.Iam/
COPY Kyntus.Caching/ Kyntus.Caching/
COPY FormationService/ FormationService/

WORKDIR "/src/FormationService/Conge.API"
RUN dotnet build "Conge.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Conge.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Conge.API.dll"]
