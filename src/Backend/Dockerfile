FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy and restore
COPY src/Shared/OmniConnect.Shared.csproj src/Shared/
COPY src/Backend/OmniConnect.Backend.csproj src/Backend/
RUN dotnet restore src/Backend/OmniConnect.Backend.csproj

# Copy everything else and build
COPY . .
WORKDIR /src/src/Backend
RUN dotnet publish -c Release -o /app

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 5000
ENTRYPOINT ["dotnet", "OmniConnect.Backend.dll"]
