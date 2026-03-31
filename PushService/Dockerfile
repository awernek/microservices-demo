FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY Contracts/Contracts.csproj Contracts/
COPY PushService/PushService.csproj PushService/
RUN dotnet restore PushService/PushService.csproj
COPY Contracts/ Contracts/
COPY PushService/ PushService/
RUN dotnet publish PushService/PushService.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "PushService.dll"]
