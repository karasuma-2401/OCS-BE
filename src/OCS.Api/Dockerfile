# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/OCS.Api/OCS.Api.csproj", "src/OCS.Api/"]
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "src/OCS.Api/OCS.Api.csproj"

COPY . .
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "src/OCS.Api/OCS.Api.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
USER $APP_UID
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OCS.Api.dll"]
