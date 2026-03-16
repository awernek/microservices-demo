# Estágio 1: build
# Usa a imagem do SDK que tem todas as ferramentas para compilar
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY *.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /out

# Estágio 2: runtime
# Usa uma imagem menor (só tem o que precisa para rodar, sem ferramentas de build)
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .

ENTRYPOINT ["dotnet", "OrderService.dll"]
