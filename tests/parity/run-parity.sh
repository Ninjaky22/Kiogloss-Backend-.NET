#!/usr/bin/env bash
# End-to-end parity check between the Spring backend and the .NET backend.
#
#   tests/parity/run-parity.sh /path/to/Kiogloss-Backend
#
# 1. starts a throw-away PostgreSQL 15 container (port 55432)
# 2. runs the Spring backend on :8100 against an empty database (Hibernate creates the schema and
#    DatabaseSeeder loads the catalog)
# 3. clones that database for the .NET backend and runs it on :8001
# 4. executes tests/parity/parity.py against both and stops everything
#
# Requirements: docker, Java 17, python3. .NET SDK 10 is used if installed, otherwise the
# mcr.microsoft.com/dotnet/sdk:10.0 image. Secrets (JWT, Cloudinary, mail) are read from the
# Spring application.properties so both sides sign tokens with the same key.
set -euo pipefail

SPRING_DIR=${1:?usage: run-parity.sh /path/to/Kiogloss-Backend}
SPRING_DIR=$(cd "$SPRING_DIR" && pwd)
NET_DIR=$(cd "$(dirname "$0")/../.." && pwd)
PG=kiogloss-parity-pg
PG_PORT=55432
WORK=$(mktemp -d)
PROPS="$SPRING_DIR/src/main/resources/application.properties"
prop() { grep -E "^$1=" "$PROPS" | head -1 | cut -d= -f2-; }

cleanup() {
  [[ -n "${SPRING_PID:-}" ]] && kill "$SPRING_PID" 2>/dev/null || true
  pkill -f "spring.datasource.url=jdbc:postgresql://localhost:$PG_PORT/parity_a" 2>/dev/null || true
  [[ -n "${NET_CONTAINER:-}" ]] && docker rm -f "$NET_CONTAINER" >/dev/null 2>&1 || true
  [[ -n "${NET_PID:-}" ]] && kill "$NET_PID" 2>/dev/null || true
  docker rm -f "$PG" >/dev/null 2>&1 || true
}
trap cleanup EXIT

wait_for() { # url, log
  for _ in $(seq 1 120); do
    curl -s -o /dev/null "$1" && return 0
    sleep 1
  done
  echo "timeout waiting for $1"; tail -50 "$2"; exit 1
}

echo "==> PostgreSQL"
docker rm -f "$PG" >/dev/null 2>&1 || true
docker run -d --name "$PG" -e POSTGRES_PASSWORD=1234 -p $PG_PORT:5432 postgres:15-alpine >/dev/null
until docker exec "$PG" pg_isready -U postgres >/dev/null 2>&1; do sleep 1; done
sleep 2
docker exec "$PG" psql -q -U postgres -c "CREATE DATABASE parity_a" -c "CREATE DATABASE parity_b"

echo "==> Spring backend on :8100"
(cd "$SPRING_DIR" && bash ./mvnw -q -DskipTests spring-boot:run \
  -Dspring-boot.run.arguments="--spring.datasource.url=jdbc:postgresql://localhost:$PG_PORT/parity_a --spring.datasource.password=1234 --server.port=8100 --app.upload.dir=$WORK/spring-uploads --spring.jpa.show-sql=false --logging.level.org.hibernate.SQL=INFO" \
  > "$WORK/spring.log" 2>&1) &
SPRING_PID=$!
wait_for "http://localhost:8100/API/V1.0/tags" "$WORK/spring.log"

echo "==> cloning the seeded database for .NET"
docker exec "$PG" sh -c "pg_dump -U postgres parity_a | psql -q -U postgres parity_b" >/dev/null

echo "==> .NET backend on :8001"
NET_ENV=(
  "TZ=${TZ_NAME:-America/Bogota}"
  "Urls=http://0.0.0.0:8001"
  "ConnectionStrings__Default=Host=localhost;Port=$PG_PORT;Database=parity_b;Username=postgres;Password=1234"
  "Jwt__Secret=$(prop jwt.secret)"
  "Cloudinary__ApiKey=$(prop cloudinary.api-key)"
  "Cloudinary__ApiSecret=$(prop cloudinary.api-secret)"
  "App__UploadDir=$WORK/uploads"
)
if command -v dotnet >/dev/null 2>&1; then
  (cd "$NET_DIR/src/Kiogloss.Api" && env "${NET_ENV[@]}" dotnet run --no-launch-profile > "$WORK/net.log" 2>&1) &
  NET_PID=$!
else
  NET_CONTAINER=kiogloss-parity-net
  args=(); for e in "${NET_ENV[@]}"; do args+=(-e "$e"); done
  docker run -d --name "$NET_CONTAINER" --network host --user "$(id -u):$(id -g)" \
    -v "$NET_DIR":/src -w /src/src/Kiogloss.Api -v "$WORK":"$WORK" \
    -e DOTNET_CLI_HOME=/tmp -e NUGET_PACKAGES=/tmp/nuget -e DOTNET_NOLOGO=1 "${args[@]}" \
    mcr.microsoft.com/dotnet/sdk:10.0 dotnet run --no-launch-profile >/dev/null
  ( docker logs -f "$NET_CONTAINER" > "$WORK/net.log" 2>&1 ) &
fi
wait_for "http://localhost:8001/API/V1.0/tags" "$WORK/net.log"

echo "==> parity"
set +e
python3 "$NET_DIR/tests/parity/parity.py" --a http://localhost:8100/API/V1.0 --b http://localhost:8001/API/V1.0 \
  --report "$NET_DIR/tests/parity/parity-report.json" "${@:2}"
STATUS=$?
echo "report: $NET_DIR/tests/parity/parity-report.json   logs: $WORK"
exit $STATUS
