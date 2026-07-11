#!/bin/bash
set -e

# Get the directory of the script
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
cd "$DIR"

echo "Starting SQL Server container (sqledge)..."
docker compose up -d sqledge

echo "Waiting for SQL Server to become healthy..."
while true; do
  STATUS=$(docker inspect --format='{{json .State.Health.Status}}' $(docker compose ps -q sqledge) 2>/dev/null || echo "unknown")
  if [ "$STATUS" = "\"healthy\"" ]; then
    break
  fi
  echo -n "."
  sleep 2
done
echo ""
echo "SQL Server is healthy!"

echo "Initializing database 'sharpinoCache'..."
# Recreate database to ensure clean, reproducible setup
docker compose exec -T sqledge /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Sharpino@1234' -No \
  -Q "IF DB_ID('sharpinoCache') IS NOT NULL ALTER DATABASE sharpinoCache SET SINGLE_USER WITH ROLLBACK IMMEDIATE; IF DB_ID('sharpinoCache') IS NOT NULL DROP DATABASE sharpinoCache; CREATE DATABASE sharpinoCache;"

echo "Applying schema from sql-cache-schema.sql..."
docker compose exec -T sqledge /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Sharpino@1234' -No \
  -d sharpinoCache -i /dev/stdin < sql-cache-schema.sql

echo "SQL Cache Setup Completed Successfully!"
