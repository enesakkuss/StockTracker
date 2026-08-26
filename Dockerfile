# ==============================================================================
# Stock Tracker — Production Multi-Stage Dockerfile (.NET 8 + Playwright)
# ==============================================================================

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project definitions for optimized layer caching
COPY ["StockTracker.sln", "./"]
COPY ["src/StockTracker.Domain/StockTracker.Domain.csproj", "src/StockTracker.Domain/"]
COPY ["src/StockTracker.Application/StockTracker.Application.csproj", "src/StockTracker.Application/"]
COPY ["src/StockTracker.Infrastructure/StockTracker.Infrastructure.csproj", "src/StockTracker.Infrastructure/"]
COPY ["src/StockTracker.Api/StockTracker.Api.csproj", "src/StockTracker.Api/"]
COPY ["tests/StockTracker.Tests/StockTracker.Tests.csproj", "tests/StockTracker.Tests/"]

RUN dotnet restore

# Copy all source files
COPY . .

# Build and publish Api
WORKDIR /src/src/StockTracker.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ==============================================================================
# Runtime Stage
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install native dependencies for Headless Chromium / Playwright
RUN apt-get update && apt-get install -y --no-install-recommends \
    libnss3 \
    libnspr4 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdrm2 \
    libxkbcommon0 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxrandr2 \
    libgbm1 \
    libasound2 \
    libpango-1.0-0 \
    libcairo2 \
    ca-certificates \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Copy build artifacts
COPY --from=build /app/publish .

# Install Playwright Chromium browser binaries (Supports both x64 and ARM64)
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN NODE_BIN=$(find /app/.playwright/node -name node -type f | head -n 1) && \
    $NODE_BIN /app/.playwright/package/cli.js install chromium \
    && chmod -R 777 /ms-playwright

# Create persistent storage directories
RUN mkdir -p /var/data && chmod 777 /var/data

# Environment configuration
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:5000 \
    ConnectionStrings__DefaultConnection="Data Source=/var/data/stocktracker.db;Cache=Shared" \
    Browser__Headless=true

EXPOSE 5000

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:5000/health/live || exit 1

ENTRYPOINT ["dotnet", "StockTracker.Api.dll"]
