#!/usr/bin/env sh
set -eu

group_name=perenearchive
account=${1:-}
archive_root=${PERENE_ARCHIVE_ROOT:-}

if [ -z "$account" ]; then
    echo "Usage: make archive-share USER=<existing-account>" >&2
    exit 2
fi

if [ "$(id -u)" -ne 0 ]; then
    echo "This script must run with elevated authority (for example: make archive-share USER=$account)." >&2
    exit 1
fi

for command in getent usermod chgrp chmod find stat; do
    if ! command -v "$command" >/dev/null 2>&1; then
        echo "Required host tool '$command' is unavailable. This NAS may require its own archive-sharing workflow." >&2
        exit 1
    fi
done

if ! getent passwd "$account" >/dev/null 2>&1; then
    echo "Host account '$account' does not exist; no changes were made." >&2
    exit 1
fi

if ! getent group "$group_name" >/dev/null 2>&1; then
    echo "Host group '$group_name' does not exist. Run 'make archive-group' first." >&2
    exit 1
fi

if [ -z "$archive_root" ] || [ ! -d "$archive_root" ]; then
    echo "PERENE_ARCHIVE_ROOT must identify an existing archive directory; no changes were made." >&2
    exit 1
fi

if [ ! -r "$archive_root" ] || [ ! -w "$archive_root" ] || [ ! -x "$archive_root" ]; then
    echo "Archive root is not accessible to root at the configured PERENE_ARCHIVE_ROOT; no changes were made." >&2
    exit 1
fi

uploads_path="$archive_root/.uploads"
if [ -e "$uploads_path" ]; then
    uploads_owner=$(stat -c '%U:%G' "$uploads_path")
    if [ "$uploads_owner" != "root:root" ]; then
        echo "Root-only .uploads must remain root:root; found '$uploads_owner'. No changes were made." >&2
        exit 1
    fi
fi

echo "Granting archive-group access to existing account '$account' for '$archive_root'."
usermod -aG "$group_name" "$account"

# Never traverse or change the root-only resumable-upload staging directory.
find "$archive_root" -path "$uploads_path" -prune -o -exec chgrp "$group_name" {} +
find "$archive_root" -path "$uploads_path" -prune -o -type d -exec chmod 2770 {} +
find "$archive_root" -path "$uploads_path" -prune -o -type f -exec chmod g+rw,o-rwx {} +

for sticky_directory in "$archive_root/Videos/Cuts" "$archive_root/Videos/VideoComposition"; do
    if [ -d "$sticky_directory" ]; then
        chgrp "$group_name" "$sticky_directory"
        chmod 3770 "$sticky_directory"
    fi
done

echo "Archive sharing is configured. '$account' must start a new login session before supplementary group membership applies."
