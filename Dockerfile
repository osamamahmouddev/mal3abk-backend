# Malabak API image. Build from the repo root:  docker build -t malabak-api .
# Runs as the non-root "app" user on port 8080 (the .NET image defaults).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet restore src/Malabak.Api/Malabak.Api.csproj
RUN dotnet publish src/Malabak.Api/Malabak.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Malabak.Api.dll"]
