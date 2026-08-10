# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, keyed only on project files, so code edits don't invalidate the
# package layer on every build.
COPY ConsoleMatch.Core/ConsoleMatch.Core.csproj ConsoleMatch.Core/
COPY DatingApp.Api/DatingApp.Api.csproj DatingApp.Api/
RUN dotnet restore DatingApp.Api/DatingApp.Api.csproj

COPY ConsoleMatch.Core/ ConsoleMatch.Core/
COPY DatingApp.Api/ DatingApp.Api/
RUN dotnet publish DatingApp.Api/DatingApp.Api.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Run as the non-root user shipped in the base image.
USER $APP_UID

# Hosts route to whatever this listens on; 8080 is the convention for non-root.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DatingApp.Api.dll"]
