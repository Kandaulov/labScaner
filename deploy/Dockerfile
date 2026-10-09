# Образ приложения labScaner: сборка на SDK, запуск на ASP.NET runtime (Ubuntu 24.04).
# Сборка из корня репозитория:  docker build -f deploy/Dockerfile -t labscaner .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/LabScaner.Core/LabScaner.Core.csproj src/LabScaner.Core/
COPY src/LabScaner.Infrastructure/LabScaner.Infrastructure.csproj src/LabScaner.Infrastructure/
COPY src/LabScaner.Jobs/LabScaner.Jobs.csproj src/LabScaner.Jobs/
COPY src/LabScaner.Web/LabScaner.Web.csproj src/LabScaner.Web/
RUN dotnet restore src/LabScaner.Web/LabScaner.Web.csproj
COPY src/ src/
ARG VERSION=0.0.0-local
RUN dotnet publish src/LabScaner.Web/LabScaner.Web.csproj -c Release -o /app --no-restore \
    -p:InformationalVersion=$VERSION -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# OCR сканов — локально (ADR-019); curl — для проверки живости контейнера.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-rus tesseract-ocr-eng curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
RUN mkdir -p /app/keys && chown -R $APP_UID /app/keys
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DataProtection__KeysPath=/app/keys \
    DOTNET_gcServer=0
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "LabScaner.Web.dll"]
