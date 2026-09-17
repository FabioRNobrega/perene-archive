#!/usr/bin/env sh
set -eu

group_name=perenearchive

if [ "$(id -u)" -ne 0 ]; then
    echo "This script must run with elevated authority (for example: make archive-group)." >&2
    exit 1
fi

for command in getent groupadd; do
    if ! command -v "$command" >/dev/null 2>&1; then
        echo "Required host tool '$command' is unavailable. This NAS may require its own group-management workflow." >&2
        exit 1
    fi
done

if getent group "$group_name" >/dev/null 2>&1; then
    echo "Host group '$group_name' already exists; leaving its GID unchanged."
else
    groupadd "$group_name"
    echo "Created host group '$group_name' with an available GID."
fi
