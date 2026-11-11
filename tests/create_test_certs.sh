#!/usr/bin/env bash
set -euo pipefail

work=$1
mkdir -p "$work/broker" "$work/edge"
umask 077

openssl req -x509 -newkey rsa:2048 -noenc -days 1 -sha256 \
  -subj /CN=Telemetry-Test-CA -addext 'basicConstraints=critical,CA:TRUE' \
  -addext 'keyUsage=critical,keyCertSign,cRLSign' \
  -keyout "$work/ca.key" -out "$work/ca.pem"
openssl req -new -newkey rsa:2048 -noenc -subj /CN=127.0.0.1 \
  -keyout "$work/broker/server.key" -out "$work/server.csr"
openssl req -new -newkey rsa:2048 -noenc -subj /CN=pump-02 \
  -keyout "$work/edge/client.key" -out "$work/client.csr"
printf 'subjectAltName=IP:127.0.0.1\nextendedKeyUsage=serverAuth\n' > "$work/server.ext"
printf 'extendedKeyUsage=clientAuth\n' > "$work/client.ext"
openssl x509 -req -days 1 -sha256 -in "$work/server.csr" -extfile "$work/server.ext" \
  -CA "$work/ca.pem" -CAkey "$work/ca.key" -CAcreateserial -out "$work/broker/server.pem"
openssl x509 -req -days 1 -sha256 -in "$work/client.csr" -extfile "$work/client.ext" \
  -CA "$work/ca.pem" -CAkey "$work/ca.key" -CAcreateserial -out "$work/edge/client.pem"
cp "$work/ca.pem" "$work/broker/ca.pem"
# The test broker runs as a different UID inside the container.
chmod 644 "$work"/broker/*
