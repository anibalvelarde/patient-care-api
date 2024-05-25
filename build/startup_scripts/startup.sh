#!/bin/bash

# Set default ports
HTTP_PORT=5245
#HTTPS_PORT=44380

# Adjust ports based on environment
if [ "$ASPNETCORE_ENVIRONMENT" == "QA" ]; then
    HTTP_PORT=8000
 #   HTTPS_PORT=8001
fi
if [ "$ASPNETCORE_ENVIRONMENT" == "PROD" ]; then
    HTTP_PORT=80
#    HTTPS_PORT=443
fi

# echo ENV Vars
echo "for the Database: -----------------------------------------------:"
printenv | grep DATABASE
echo "for the ASP NET CORE: -------------------------------------------:"
printenv | grep ASPNETCORE
echo "-----------------------------------------------------------------:"

# Start the application
# dotnet Web.dll --urls="http://*:$HTTP_PORT;https://*:$HTTPS_PORT"
dotnet Web.dll --urls="http://*:$HTTP_PORT"
