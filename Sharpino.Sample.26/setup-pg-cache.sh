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

echo "Running dbmate on $DATABASE_L2_CACHE to set up L2 cache..."
dbmate --migrations-dir dbcache --url "$DATABASE_L2_CACHE" up

echo "Postgres L2 Cache Setup Completed Successfully!"
