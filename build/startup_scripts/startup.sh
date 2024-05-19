#!/bin/bash

# Set default ports
HTTP_PORT=5245
HTTPS_PORT=44380

# Adjust ports based on environment
if [ "$ASPNETCORE_ENVIRONMENT" == "QA" ]; then
    HTTP_PORT=8000
    HTTPS_PORT=8001
elif [ "$ASPNETCORE_ENVIRONMENT" == "PROD" ]; then
    HTTP_PORT=80
    HTTPS_PORT=443
fi

# Start the application
dotnet Web.dll --urls="http://*:$HTTP_PORT;https://*:$HTTPS_PORT"
