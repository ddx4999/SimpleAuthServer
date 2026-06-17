FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["SimpleAuthServer.csproj", "."]
RUN dotnet restore "./SimpleAuthServer.csproj"
COPY . .
RUN dotnet build "SimpleAuthServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SimpleAuthServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "SimpleAuthServer.dll"]