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