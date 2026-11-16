#!/bin/bash
set -euo pipefail

work="$(mktemp -d)"
session=""
base="http://127.0.0.1:${ASPNETCORE_HTTP_PORTS:-8080}"
dotnet /app/TT.Web.LocationIntelligence.dll >"$work/app.log" 2>&1 &
app_pid=$!
cleanup() {
    result=$?
    if [ -n "$session" ] && [ -f "$work/runtime.json" ]; then
        curl --silent --show-error --fail-with-body --max-time 30 \
            -b "$work/cookies" -H "X-CSRF-TOKEN: $(jq -r .csrfToken "$work/runtime.json")" \
            -X DELETE "$base/api/sessions/$session" >/dev/null || result=1
    fi
    if [ "$result" -ne 0 ]; then
        echo "Verification failed; application diagnostics follow."
        cat "$work/app.log"
    fi
    kill "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
    rm -rf "$work"
    exit "$result"
}
trap cleanup EXIT

for setting in Cosmos__Endpoint Agent__Endpoint; do
    endpoint="${!setting}"
    host="${endpoint#https://}"
    host="${host%%[:/]*}"
    addresses="$(getent ahostsv4 "$host" | awk '{print $1}' | sort -u)"
    test -n "$addresses"
    while read -r address; do
        case "$address" in
            10.*|192.168.*|172.1[6-9].*|172.2[0-9].*|172.3[01].*) ;;
            *) echo "FAIL: $host resolved to a non-private address."; exit 1 ;;
        esac
    done <<< "$addresses"
    echo "PASS: $setting resolves through private DNS ($addresses)."
done

ready=false
for attempt in $(seq 1 90); do
    if curl --silent --fail --max-time 10 "$base/health/ready" > /dev/null; then
        ready=true
        break
    fi
    kill -0 "$app_pid"
    sleep 5
done
test "$ready" = true
echo "PASS: application readiness and managed-identity Cosmos metadata access."
curl --silent --show-error --fail-with-body --max-time 30 "$base/" -o "$work/index.html"
grep -q '/dist/' "$work/index.html"
asset="$(grep -oE '/dist/[^\" ]+\.js' "$work/index.html" | head -n 1)"
test -n "$asset"
curl --silent --show-error --fail-with-body --max-time 30 "$base$asset" >/dev/null
echo "PASS: Razor page and compiled client assets."
curl --silent --show-error --fail-with-body --max-time 30 -c "$work/cookies" \
    "$base/api/runtime" -o "$work/runtime.json"
jq -e '.agentConfigured == true and .emulator == false' "$work/runtime.json" >/dev/null
csrf="$(jq -r .csrfToken "$work/runtime.json")"
curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" \
    -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' \
    -d '{"title":"Deployment verification (temporary)"}' "$base/api/sessions/" -o "$work/session.json"
session="$(jq -er .id "$work/session.json")"
curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" \
    "$base/api/sessions/$session" | jq -e --arg id "$session" '.id == $id' >/dev/null
curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" \
    "$base/api/sessions/" | jq -e --arg id "$session" 'any(.items[]; .id == $id)' >/dev/null
curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" \
    -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' -X PUT \
    -d '{"title":"Deployment verification updated"}' "$base/api/sessions/$session" >/dev/null
echo "PASS: Cosmos create, point read, query and update using managed identity."
request_id="$(cat /proc/sys/kernel/random/uuid)"
jq -n --arg id "$request_id" \
    '{requestId:$id,content:"Use a TomTom tool to geocode Amsterdam, Netherlands. Reply briefly with its coordinates."}' >"$work/message.json"
curl --silent --show-error --fail-with-body --max-time 240 -b "$work/cookies" \
    -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' \
    --data-binary "@$work/message.json" "$base/api/sessions/$session/messages" -o "$work/reply.json"
jq -e --arg model "$Agent__Model" \
    '.messages[-1] | .status == "completed" and .model == $model and (.content | length > 0) and .outputTokens > 0' \
    "$work/reply.json" >/dev/null
echo "PASS: real Foundry model reply with TomTom tools and persisted token usage."
curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" \
    -H "X-CSRF-TOKEN: $csrf" -X DELETE "$base/api/sessions/$session" >/dev/null
session=""
echo "PASS: Cosmos delete; temporary conversation removed."
echo "DEPLOYMENT_VERIFICATION_SUCCEEDED"
