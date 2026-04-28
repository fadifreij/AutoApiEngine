# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution file (if you have one)
COPY ../BE/*.sln ./

# Copy csproj files individually (for better layer caching)
COPY ../BE/AutoApiEngine.ApiService/*.csproj AutoApiEngine.ApiService/
COPY ../BE/AutoApiEngine.Domain/*.csproj AutoApiEngine.Domain/
COPY ../BE/AutoApiEngine.Persistence/*.csproj AutoApiEngine.Persistence/
COPY ../BE/AutoApiEngine.Presentation/*.csproj AutoApiEngine.Presentation/
COPY ../BE/AutoApiEngine.ServiceAbstraction/*.csproj AutoApiEngine.ServiceAbstraction/
COPY ../BE/AutoApiEngine.Services/*.csproj AutoApiEngine.Services/

# Restore dependencies
RUN dotnet restore AutoApiEngine.Services/AutoApiEngine.Services.csproj

# Copy the full source
COPY ../BE/. .

# Publish the main project
RUN dotnet publish AutoApiEngine.Services/AutoApiEngine.Services.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Expose port (adjust if needed)
EXPOSE 8080

# Run the app
ENTRYPOINT ["dotnet", "AutoApiEngine.Services.dll"]