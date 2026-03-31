# Build context deve ser a raiz do repositório (não o subdiretório NotificationService),
# pois NotificationService tem ProjectReference para Contracts.
# docker compose usa: build: { context: ., dockerfile: NotificationService/Dockerfile }
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY Contracts/Contracts.csproj Contracts/
COPY NotificationService/NotificationService.csproj NotificationService/
RUN dotnet restore NotificationService/NotificationService.csproj
COPY Contracts/ Contracts/
COPY NotificationService/ NotificationService/
RUN dotnet publish NotificationService/NotificationService.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "NotificationService.dll"]
