# Contexte Docker = racine du dépôt (voir docker-compose.yml service planning-backend).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Messaging.Contracts/Messaging.Contracts.csproj Messaging.Contracts/
COPY Kyntus.Iam/Kyntus.Iam.csproj Kyntus.Iam/
COPY Kyntus.Identity.Jwt/Kyntus.Identity.Jwt.csproj Kyntus.Identity.Jwt/
COPY Kyntus.Messaging.Outbox/Kyntus.Messaging.Outbox.csproj Kyntus.Messaging.Outbox/
COPY Kyntus.Caching/Kyntus.Caching.csproj Kyntus.Caching/
COPY Shared.Kernel/Shared.Kernel.csproj Shared.Kernel/
COPY PlanningService/Planning.Domain/Planning.Domain.csproj PlanningService/Planning.Domain/
COPY PlanningService/Planning.Application/Planning.Application.csproj PlanningService/Planning.Application/
COPY PlanningService/Planning.Infrastructure/Planning.Infrastructure.csproj PlanningService/Planning.Infrastructure/
COPY PlanningService/Planning.API/Planning.API.csproj PlanningService/Planning.API/
RUN dotnet restore PlanningService/Planning.API/Planning.API.csproj

COPY Messaging.Contracts/ Messaging.Contracts/
COPY Kyntus.Iam/ Kyntus.Iam/
COPY Kyntus.Identity.Jwt/ Kyntus.Identity.Jwt/
COPY Kyntus.Messaging.Outbox/ Kyntus.Messaging.Outbox/
COPY Kyntus.Caching/ Kyntus.Caching/
COPY Shared.Kernel/ Shared.Kernel/
COPY PlanningService/ PlanningService/
RUN dotnet publish PlanningService/Planning.API/Planning.API.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Planning.API.dll"]
