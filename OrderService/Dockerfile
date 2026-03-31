# Build context deve ser a raiz do repositório (não o subdiretório OrderService),
# pois OrderService tem ProjectReference para Contracts.
# docker compose usa: build: { context: ., dockerfile: OrderService/Dockerfile }
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY Contracts/Contracts.csproj Contracts/
COPY OrderService/OrderService.csproj OrderService/
RUN dotnet restore OrderService/OrderService.csproj
COPY Contracts/ Contracts/
COPY OrderService/ OrderService/
RUN dotnet publish OrderService/OrderService.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "OrderService.dll"]
