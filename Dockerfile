FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/AmIRite.Web/AmIRite.Web.csproj ./
RUN dotnet restore
COPY src/AmIRite.Web/ ./
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create a directory for the SQLite database volume mount
RUN mkdir -p /data

COPY --from=build /app ./

# Fly.io injects PORT; ASP.NET Core respects ASPNETCORE_URLS
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Point the database at the persistent volume
ENV ConnectionStrings__DefaultConnection="Data Source=/data/amirite.db"

EXPOSE 8080
ENTRYPOINT ["dotnet", "AmIRite.Web.dll"]
