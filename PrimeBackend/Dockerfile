# Contexte Docker = racine du dépôt (voir docker-compose.yml service prime-backend).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Messaging.Contracts/Messaging.Contracts.csproj Messaging.Contracts/
COPY Kyntus.Identity.Jwt/Kyntus.Identity.Jwt.csproj Kyntus.Identity.Jwt/
COPY Kyntus.Iam/Kyntus.Iam.csproj Kyntus.Iam/
COPY Kyntus.Messaging.Outbox/Kyntus.Messaging.Outbox.csproj Kyntus.Messaging.Outbox/
COPY Kyntus.Caching/Kyntus.Caching.csproj Kyntus.Caching/
COPY Shared.Kernel/Shared.Kernel.csproj Shared.Kernel/
COPY PrimeBackend/Prime.Domain/Prime.Domain.csproj PrimeBackend/Prime.Domain/
COPY PrimeBackend/Prime.Application/Prime.Application.csproj PrimeBackend/Prime.Application/
COPY PrimeBackend/Prime.Infrastructure/Prime.Infrastructure.csproj PrimeBackend/Prime.Infrastructure/
COPY PrimeBackend/Prime.API/Prime.API.csproj PrimeBackend/Prime.API/
RUN dotnet restore PrimeBackend/Prime.API/Prime.API.csproj

COPY Messaging.Contracts/ Messaging.Contracts/
COPY Kyntus.Identity.Jwt/ Kyntus.Identity.Jwt/
COPY Kyntus.Iam/ Kyntus.Iam/
COPY Kyntus.Messaging.Outbox/ Kyntus.Messaging.Outbox/
COPY Kyntus.Caching/ Kyntus.Caching/
COPY Shared.Kernel/ Shared.Kernel/
COPY PrimeBackend/ PrimeBackend/
# ProduceReferenceAssembly=false : évite CopyRefAssembly (MSB3883 / I/O overlay Docker).
RUN dotnet publish PrimeBackend/Prime.API/Prime.API.csproj -c Release -o /out /p:UseAppHost=false /p:ProduceReferenceAssembly=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Prime.API.dll"]
