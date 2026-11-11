#!/usr/bin/env bash
set -euo pipefail

edge=$1
work=$2
dotnet run --project "$edge/tests/PublisherChecks" -c Release
dotnet build "$edge/src/Publisher" -c Release --nologo
publisher="$edge/src/Publisher/bin/Release/net10.0/Publisher.dll"

dotnet "$publisher" simulate > "$work/simulated.jsonl"
python3 tests/verify_edge.py "$work/simulated.jsonl"

ALARM_THRESHOLD_C=28 docker compose -f compose.yaml -f compose.sensor.yaml up -d --no-deps alerts
curl --fail --silent --retry 20 --retry-all-errors --retry-delay 1 http://localhost:5182/health/ready

printf 'a1 01 4b 46 7f ff 0c 10 72 : crc=72 YES\na1 01 4b 46 7f ff 0c 10 72 t=30125\n' > "$work/w1_slave"
export MQTT_TLS=1
export MQTT_CA_FILE="$work/ca.pem"
export MACHINE_ID=pump-02
MQTT_CLIENT_CERT="$work/edge/client.pem" MQTT_CLIENT_KEY="$work/edge/client.key" \
  dotnet "$publisher" sensor "$work/w1_slave" --once > "$work/sensor.jsonl"
python3 tests/verify_edge.py "$work/simulated.jsonl" "$work/sensor.jsonl"

if dotnet "$publisher" sensor "$work/w1_slave" --once > "$work/denied.jsonl"; then
  echo "The TLS listener accepted a client without a certificate."
  exit 1
fi
test ! -s "$work/denied.jsonl"
echo "PASS: the TLS listener rejected a client without a certificate."
