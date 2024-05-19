# Use the official .NET SDK image to build the project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /patient-care-api-root
COPY ./patient-care-api.sln .
COPY ./src ./src
COPY ./tests ./tests

# --------------------  D E B U G G I N G   C O M M A N D S  for BUILD  Only ---------------------
# RUN apt-get update
# RUN apt-get install tree
# RUN apt-get install -y iproute2
#    RUN pwd; tree
# RUN pwd; find . -type d; ls -la
# RUN pwd; ls; ls -la ./Core
# RUN pwd; ls; ls -la ./Infrastructure
# RUN pwd; ls; ls -la ./Web
# -----------------------------------------------------------------------------------------------

RUN dotnet restore "patient-care-api.sln"
RUN dotnet build "patient-care-api.sln" -c Release --no-restore
RUN dotnet test "patient-care-api.sln" -c Release --no-build --no-restore # Assuming test projects are included in the solution

# Publish the Web API
WORKDIR "/patient-care-api-root/src/Web"
RUN dotnet publish "Web.csproj" -c Release -o /app/publish

# Use the official ASP.NET Core runtime image for the runtime environment
# Final   Stage | Run the application
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
ARG CERT_PASSWORD="password"
WORKDIR /app

# Copy the application files from the publish stage
COPY --from=build /app/publish .

# Install OpenSSL for generating self-signed certificates
RUN apt-get update && apt-get install -y openssl

# Set environment variables for the certificate location and password
ENV ASPNETCORE_URLS=https://+:443;http://+:80
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=${CERT_PASSWORD}
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/https/cert.pfx

# Create a directory for the certificates
RUN mkdir /https && chmod 700 /https

# Generate a self-signed certificate
RUN openssl req -newkey rsa:4096 \
                -x509 \
                -sha256 \
                -days 365 \
                -nodes \
                -out /https/cert.crt \
                -keyout /https/cert.key \
                -subj "/C=US/ST=State/L=City/O=Company/CN=localhost"

# Create a PFX file from the certificate
RUN openssl pkcs12 -export -out /https/cert.pfx -inkey /https/cert.key -in /https/cert.crt -password pass:${CERT_PASSWORD}

# Copy the startup script to the container and make it executable
RUN ls -la;
COPY ./startup_scripts/startup.sh /startup.sh
RUN chmod +x /startup.sh

# Set the entry point to use the startup script
ENTRYPOINT ["/bin/bash", "/startup.sh"]