#!/usr/bin/env bash
# Запускает команду и выносит строки ошибок в аннотации GitHub Actions,
# чтобы причину падения было видно без скачивания логов.
set -o pipefail
log=$(mktemp)
"$@" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}
if [ "$status" -ne 0 ]; then
  grep -iE "error [A-Z]+[0-9]+|error:|\[FAIL\]|Failed [A-Za-z]|Assert\.|Exception|Traceback|line [0-9]+, in" "$log" \
    | sed -E 's/^[[:space:]]+//' | sort -u | head -40 \
    | while IFS= read -r line; do echo "::error::${line}"; done
fi
exit "$status"
