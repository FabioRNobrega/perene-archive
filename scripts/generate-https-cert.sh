#!/usr/bin/env sh
# Generates a self-signed X.509 certificate/key/PFX for the optional Kestrel HTTPS endpoint.
# Run via `make https-cert` inside the SDK image; reads HTTPS_SAN_HOSTS and HTTPS_CERT_PASSWORD
# from the environment (populated from .env by Docker Compose). Writes into ./https (mounted at
# /workspace/https inside the container).

set -eu

OUT_DIR="/workspace/https"
KEY_PATH="$OUT_DIR/perene.key"
CRT_PATH="$OUT_DIR/perene.crt"
PFX_PATH="$OUT_DIR/perene.pfx"

if [ -z "${HTTPS_CERT_PASSWORD:-}" ]; then
    echo "HTTPS_CERT_PASSWORD is not set. Set it in .env before running 'make https-cert'." >&2
    exit 1
fi

mkdir -p "$OUT_DIR"

SAN="DNS:localhost,IP:127.0.0.1"
IFS=','
set -- $(printf '%s' "${HTTPS_SAN_HOSTS:-}" | tr ';' ',')
for host in "$@"; do
    host=$(printf '%s' "$host" | sed 's/^ *//; s/ *$//')
    [ -z "$host" ] && continue
    case "$host" in
        *[!0-9.]*)
            SAN="$SAN,DNS:$host"
            ;;
        *)
            SAN="$SAN,IP:$host"
            ;;
    esac
done

echo "Generating self-signed certificate with SAN: $SAN"

openssl req -x509 -newkey rsa:2048 -nodes \
    -keyout "$KEY_PATH" \
    -out "$CRT_PATH" \
    -days 825 \
    -subj "/CN=PereneArchive" \
    -addext "subjectAltName=$SAN"

openssl pkcs12 -export \
    -inkey "$KEY_PATH" \
    -in "$CRT_PATH" \
    -out "$PFX_PATH" \
    -passout "pass:$HTTPS_CERT_PASSWORD"

echo "Wrote $KEY_PATH, $CRT_PATH, and $PFX_PATH."
