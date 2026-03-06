# Build stage — Alpine reduces image size from ~900MB to ~200MB (compressed)
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy project file first for layer caching on restore
COPY TaggerAgent.csproj .
RUN dotnet restore --runtime linux-x64

# Copy source and publish in one step (no separate build needed)
COPY . .
RUN dotnet publish -c Release -o /app/publish \
    --no-restore \
    --runtime linux-x64 \
    --self-contained false \
    -p:PublishReadyToRun=true

# Runtime stage — Alpine reduces image size from ~220MB to ~110MB (compressed)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "TaggerAgent.dll"]
