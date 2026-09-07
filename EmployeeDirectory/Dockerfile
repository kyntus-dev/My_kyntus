FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Messaging.Contracts/Messaging.Contracts.csproj Messaging.Contracts/
COPY Kyntus.Identity.Jwt/Kyntus.Identity.Jwt.csproj Kyntus.Identity.Jwt/
COPY Kyntus.Messaging.Outbox/Kyntus.Messaging.Outbox.csproj Kyntus.Messaging.Outbox/
COPY Kyntus.Iam/Kyntus.Iam.csproj Kyntus.Iam/
COPY Kyntus.Caching/Kyntus.Caching.csproj Kyntus.Caching/
COPY EmployeeDirectory/EmployeeDirectory.Domain/EmployeeDirectory.Domain.csproj EmployeeDirectory/EmployeeDirectory.Domain/
COPY EmployeeDirectory/EmployeeDirectory.Application/EmployeeDirectory.Application.csproj EmployeeDirectory/EmployeeDirectory.Application/
COPY EmployeeDirectory/EmployeeDirectory.Infrastructure/EmployeeDirectory.Infrastructure.csproj EmployeeDirectory/EmployeeDirectory.Infrastructure/
COPY EmployeeDirectory/EmployeeDirectory.API/EmployeeDirectory.API.csproj EmployeeDirectory/EmployeeDirectory.API/

RUN dotnet restore EmployeeDirectory/EmployeeDirectory.API/EmployeeDirectory.API.csproj

COPY Messaging.Contracts/ Messaging.Contracts/
COPY Kyntus.Identity.Jwt/ Kyntus.Identity.Jwt/
COPY Kyntus.Messaging.Outbox/ Kyntus.Messaging.Outbox/
COPY Kyntus.Iam/ Kyntus.Iam/
COPY Kyntus.Caching/ Kyntus.Caching/
COPY EmployeeDirectory/ EmployeeDirectory/

RUN dotnet publish EmployeeDirectory/EmployeeDirectory.API/EmployeeDirectory.API.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "EmployeeDirectory.API.dll"]
