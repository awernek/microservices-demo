FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY Contracts/Contracts.csproj Contracts/
COPY SmsService/SmsService.csproj SmsService/
RUN dotnet restore SmsService/SmsService.csproj
COPY Contracts/ Contracts/
COPY SmsService/ SmsService/
RUN dotnet publish SmsService/SmsService.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "SmsService.dll"]
