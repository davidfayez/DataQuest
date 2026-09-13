# syntax=docker/dockerfile:1

# ---- build stage -------------------------------------------------------------
# .NET 10 SDK builds a self-contained framework-dependent publish. Using the SDK
# image means the host does not need any .NET runtime installed — the container
# carries everything, which is what makes this deployable to any container host.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Central build + package management files apply to every project below them, so
# they must be present before restore. Copied first for layer caching.
COPY Directory.Build.props Directory.Packages.props ./

# Project files first so `restore` is cached until a dependency actually changes.
COPY src/DataVerification.Domain/DataVerification.Domain.csproj src/DataVerification.Domain/
COPY src/DataVerification.Application/DataVerification.Application.csproj src/DataVerification.Application/
COPY src/DataVerification.Infrastructure/DataVerification.Infrastructure.csproj src/DataVerification.Infrastructure/
COPY src/DataVerification.API/DataVerification.API.csproj src/DataVerification.API/

RUN dotnet restore src/DataVerification.API/DataVerification.API.csproj

# Now the rest of the source and publish.
COPY src/ src/
RUN dotnet publish src/DataVerification.API/DataVerification.API.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

# ---- runtime stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Uploads live here; mount a persistent volume at this path on the host so files
# survive a redeploy (disk storage is otherwise ephemeral on container hosts).
RUN mkdir -p /app/storage
ENV Storage__RootPath=/app/storage
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# Bind to the port the platform provides (Railway/Render set $PORT); fall back to
# 8080 for hosts that expect a fixed port (Azure, Fly, plain Docker).
CMD ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet DataVerification.API.dll"]
