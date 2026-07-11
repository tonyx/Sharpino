#!/bin/bash
set -e

# Get the directory of the script
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
cd "$DIR"

echo "Stopping and removing SQL Server container and volumes..."
docker compose down sqledge -v

echo "Teardown Completed Successfully!"
