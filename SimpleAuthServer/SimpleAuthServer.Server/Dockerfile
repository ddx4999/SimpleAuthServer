FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SimpleAuthServer/SimpleAuthServer.Server/SimpleAuthServer.Server.csproj", "SimpleAuthServer/SimpleAuthServer.Server/"]
RUN dotnet restore "SimpleAuthServer/SimpleAuthServer.Server/SimpleAuthServer.Server.csproj"
COPY . .
WORKDIR "/src/SimpleAuthServer/SimpleAuthServer.Server"
RUN dotnet build "SimpleAuthServer.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SimpleAuthServer.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "SimpleAuthServer.Server.dll"]
