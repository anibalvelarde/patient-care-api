# Use the official .NET SDK image to build the project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /patient-care-api-root
COPY ./patient-care-api.sln .
COPY ./src ./src
COPY ./tests ./tests

RUN dotnet restore "patient-care-api.sln"
RUN dotnet build "patient-care-api.sln" -c Release --no-restore
RUN dotnet test "patient-care-api.sln" -c Release --no-build --no-restore # Assuming test projects are included in the solution

# Publish the Web API
WORKDIR "/patient-care-api-root/src/Web"
RUN dotnet publish "Web.csproj" -c Release -o /app/publish

# Use the official ASP.NET Core runtime image for the runtime environment
# Final   Stage | Run the application
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

# Copy the application files from the publish stage
COPY --from=build /app/publish .

# Install OpenSSL for generating self-signed certificates
RUN apt-get update && apt-get install -y dos2unix

# Copy the startup script to the container and make it executable
COPY ./build/startup_scripts/startup.sh /startup.sh
RUN chmod +x /startup.sh
RUN dos2unix /startup.sh

# Set the entry point to use the startup script
ENTRYPOINT ["/bin/bash", "/startup.sh"]