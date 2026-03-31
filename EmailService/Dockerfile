FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY Contracts/Contracts.csproj Contracts/
COPY EmailService/EmailService.csproj EmailService/
RUN dotnet restore EmailService/EmailService.csproj
COPY Contracts/ Contracts/
COPY EmailService/ EmailService/
RUN dotnet publish EmailService/EmailService.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "EmailService.dll"]
