# ── Stage 1: Build ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore
COPY ["NCS.CBT.csproj", "."]
RUN dotnet restore "./NCS.CBT.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "NCS.CBT.csproj" -c Release -o /app/build

# ── Stage 2: Publish ────────────────────────────────────────────────────────
FROM build AS publish
RUN dotnet publish "NCS.CBT.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ── Stage 3: Runtime ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# Run as non-root user
RUN adduser --disabled-password --gecos "" appuser \
    && mkdir -p /app/dp-keys \
    && chown -R appuser:appuser /app
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "NCS.CBT.dll"]
