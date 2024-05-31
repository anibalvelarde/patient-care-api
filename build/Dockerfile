# Use the official .NET SDK image to build the project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy the solution and project files
COPY ./patient-care-api.sln .
COPY ./src/Core/Core.csproj ./src/Core/
COPY ./src/Infrastructure/Infrastructure.csproj ./src/Infrastructure/
COPY ./src/Web/Web.csproj ./src/Web/
COPY ./tests/Core.Tests/Core.Tests.csproj ./tests/Core.Tests/
COPY ./tests/Infrastructure.Tests/Infrastructure.Tests.csproj ./tests/Infrastructure.Tests/
COPY ./tests/Web.Tests/Web.Tests.csproj ./tests/Web.Tests/

# Copy the remaining project files
COPY ./src ./src
COPY ./tests ./tests

# Restore the dependencies for the Web project and Test project
RUN dotnet restore "./src/Core/Core.csproj"
RUN dotnet restore "./src/Infrastructure/Infrastructure.csproj"
RUN dotnet restore "./src/Web/Web.csproj"
RUN dotnet restore "./tests/Core.Tests/Core.Tests.csproj"
RUN dotnet restore "./tests/Infrastructure.Tests/Infrastructure.Tests.csproj"
RUN dotnet restore "./tests/Web.Tests/Web.Tests.csproj"

# Build and test the solution
RUN dotnet build "patient-care-api.sln" -c Release --no-restore
RUN dotnet test "patient-care-api.sln" -c Release --no-restore

# Publish the Web API
WORKDIR /app/src/Web
ARG BUILD_VERSION
RUN dotnet publish "Web.csproj" -c Release -o /app/publish --no-restore /p:Version=$BUILD_VERSION

# Use the official ASP.NET Core runtime image for the runtime environment
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
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
