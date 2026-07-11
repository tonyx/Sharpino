#!/bin/bash
set -e

# Get the directory of the script
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
cd "$DIR"

echo "Starting Redis L2 cache container..."
docker compose up -d redis-cache

echo "Waiting for Redis to become healthy..."
while true; do
  STATUS=$(docker inspect --format='{{json .State.Health.Status}}' $(docker compose ps -q redis-cache) 2>/dev/null || echo "unknown")
  if [ "$STATUS" = "\"healthy\"" ]; then
    break
  fi
  echo -n "."
  sleep 2
done
echo ""
echo "Redis is healthy!"

# Confirm connectivity with redis-cli
docker compose exec -T redis-cache redis-cli ping | grep -q PONG && echo "Redis PING: OK"

echo ""
echo "Redis L2 Cache Setup Completed Successfully!"
echo ""
echo "Connection: localhost:6380"
echo ""
echo "To use Redis as L2 cache, set the following in appSettings.json:"
echo "  \"Cache\": {"
echo "    \"L2SqlCacheEnabled\": true,"
echo "    \"L2CacheProvider\": \"Redis\","
echo "    \"L2CacheConnectionString\": \"localhost:6380\","
echo "    \"L2PgNotifyBackplaneEnabled\": false,"
echo "    \"L2RedisBackplaneEnabled\": true,"
echo "    \"L2RedisBackplaneChannel\": \"sharpino_cache_eviction\""
echo "  }"
