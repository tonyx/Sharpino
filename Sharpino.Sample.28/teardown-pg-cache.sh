#!/bin/bash
set -e

# Get the directory of the script
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
cd "$DIR"

# Load the L2 cache connection string from .env
if [ -f .env ]; then
  export $(grep -v '^#' .env | xargs)
fi

if [ -z "$DATABASE_L2_CACHE" ]; then
  echo "DATABASE_L2_CACHE is not set in .env"
  exit 1
fi

echo "Running dbmate on $DATABASE_L2_CACHE to teardown L2 cache..."
dbmate --url "$DATABASE_L2_CACHE" drop

echo "Postgres L2 Cache Teardown Completed Successfully!"
