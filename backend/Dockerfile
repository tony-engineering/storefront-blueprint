# Build context: ./backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until the project file changes.
COPY src/Storefront.Api/Storefront.Api.csproj src/Storefront.Api/
RUN dotnet restore src/Storefront.Api/Storefront.Api.csproj

COPY src/ src/
RUN dotnet publish src/Storefront.Api/Storefront.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The aspnet image listens on 8080 and provides a non-root user.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Storefront.Api.dll"]
