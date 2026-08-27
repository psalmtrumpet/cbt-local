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

# Download face-api.js model weights at build time (no CDN dependency at runtime)
RUN mkdir -p /app/wwwroot/face-api-weights && \
    cd /app/wwwroot/face-api-weights && \
    BASE=https://raw.githubusercontent.com/justadudewhohacks/face-api.js/master/weights && \
    curl -sLO $BASE/tiny_face_detector_model-weights_manifest.json && \
    curl -sLO $BASE/tiny_face_detector_model-shard1 && \
    curl -sLO $BASE/face_landmark_68_tiny_model-weights_manifest.json && \
    curl -sLO $BASE/face_landmark_68_tiny_model-shard1 && \
    curl -sLO $BASE/face_recognition_model-weights_manifest.json && \
    curl -sLO $BASE/face_recognition_model-shard1 && \
    curl -sLO $BASE/face_recognition_model-shard2 && \
    ls -lh /app/wwwroot/face-api-weights/ && \
    curl -sLo /app/wwwroot/js/face-api.min.js \
      https://cdn.jsdelivr.net/npm/face-api.js@0.22.2/dist/face-api.min.js && \
    echo "face-api.js size: $(wc -c < /app/wwwroot/js/face-api.min.js) bytes"

# Run as non-root user
RUN adduser --disabled-password --gecos "" appuser \
    && mkdir -p /app/data/snapshots /app/data/recordings \
    && chown -R appuser:appuser /app
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "NCS.CBT.dll"]
