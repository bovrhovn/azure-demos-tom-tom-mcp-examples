#!/bin/bash
set -euo pipefail

base="${1:-http://127.0.0.1:8080}"
scenarios=(handoff magentic)
if [ -n "${2:-}" ]; then
    case "$2" in
        handoff|magentic) scenarios=("$2") ;;
        *) echo "Choose handoff or magentic." >&2; exit 2 ;;
    esac
fi
work="$(mktemp -d)"
connection=""
stage="checking travel pages and configuration"
trap 'echo "FAIL: $stage (line $LINENO)." >&2' ERR
cleanup() {
    result=$?
    if [ -n "$connection" ]; then
        curl --silent --max-time 10 -b "$work/cookies" -X DELETE "$connection" >/dev/null || result=1
    fi
    rm -rf "$work"
    exit "$result"
}
trap cleanup EXIT

for page in Travel Travel/Handoff Travel/Magentic; do
    curl --silent --show-error --fail-with-body --max-time 30 "$base/$page" |
        grep -q 'data-workspace="travel"'
done
curl --silent --show-error --fail-with-body --max-time 30 -c "$work/cookies" \
    "$base/api/runtime" -o "$work/runtime.json"
csrf="$(jq -er .csrfToken "$work/runtime.json")"
curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" \
    "$base/api/travel/config" -o "$work/config.json"
jq -e '.configured == true and (.countries | length == 27) and
    ([.countries[].code] | unique | length == 27)' "$work/config.json" >/dev/null
departure="$(jq -er .minDate "$work/config.json")"
echo "PASS: all three travel pages, configuration and 27 unique EU countries."

for scenario in "${scenarios[@]}"; do
    stage="requesting $scenario ticket"
    jq -n --arg scenario "$scenario" --arg departure "$departure" \
        '{scenario:$scenario,startCountry:"NL",destinationCountry:"DE",departureDate:$departure}' >"$work/request.json"
    curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" \
        -H "X-CSRF-TOKEN: $csrf" -H 'Content-Type: application/json' \
        --data-binary "@$work/request.json" "$base/api/travel/queries" -o "$work/ticket.json"
    ticket="$(jq -er .ticket "$work/ticket.json")"
    stage="negotiating $scenario SignalR connection"
    curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" \
        -X POST "$base/hubs/travel/negotiate?negotiateVersion=1" -o "$work/negotiate.json"
    token="$(jq -er '.connectionToken | @uri' "$work/negotiate.json")"
    connection="$base/hubs/travel?id=$token"
    stage="starting $scenario long polling"
    curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" "$connection" >/dev/null
    printf '%s\036' '{"protocol":"json","version":1}' |
        curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" \
            -H 'Content-Type: text/plain' --data-binary @- "$connection" >/dev/null
    curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" \
        "$connection" -o "$work/handshake"
    stage="validating $scenario SignalR handshake"
    jq -Rse 'split("\u001e") | map(select(test("\\S")) | fromjson) |
        any(.[]; . == {})' "$work/handshake" >/dev/null
    invocation="$(jq -cn --arg ticket "$ticket" \
        '{type:4,invocationId:"travel",target:"Plan",arguments:[$ticket]}')"
    printf '%s\036' "$invocation" |
        curl --silent --show-error --fail-with-body --max-time 30 -b "$work/cookies" \
            -H 'Content-Type: text/plain' --data-binary @- "$connection" >/dev/null
    : >"$work/frames"
    finished=false
    stage="reading $scenario SignalR events"
    deadline=$((SECONDS + 420))
    while [ "$SECONDS" -lt "$deadline" ]; do
        curl --silent --show-error --fail-with-body --max-time 120 -b "$work/cookies" \
            "$connection" -o "$work/poll"
        cat "$work/poll" >>"$work/frames"
        jq -Rs 'split("\u001e") | map(select(test("\\S")) | fromjson)' "$work/frames" >"$work/messages.json"
        if jq -e 'any(.[]; .type == 3 and .invocationId == "travel")' "$work/messages.json" >/dev/null; then
            finished=true
            break
        fi
    done
    test "$finished" = true
    stage="validating $scenario workflow evidence"
    if ! jq -e --arg scenario "$scenario" '
        map(select(.type == 2 and .invocationId == "travel") | .item) as $events |
        all(.[]; .type != 3 or .error == null) and
        all($events[]; .type != "error") and
        any($events[]; .type == "completed") and
        any($events[]; .type == "weather" and .data.location == "Amsterdam") and
        any($events[]; .type == "weather" and .data.location == "Berlin") and
        any($events[]; .type == "agent-start" and .agent == "WeatherChecker") and
        if $scenario == "handoff" then
            any($events[]; .type == "agent-start" and .agent == "TravelAssistant") and
            any($events[]; .type == "agent-start" and .agent == "RoutePlanner") and
            any($events[]; .type == "result" and .data.routeChecked and .data.chargingChecked and (.data.text | length > 0))
        else
            any($events[]; .type == "plan") and
            any($events[]; .type == "task" and .data.satisfied == true) and
            any($events[]; .type == "result" and (.data.text | length > 0) and
                (.data.outcome == "postpone" or (.data.sunnyRouteVerified and .data.routeChecked and .data.chargingChecked)))
        end' "$work/messages.json" >/dev/null; then
        jq -r '.[] | if .type == 3 and .error != null then "SignalR error: \(.error)"
            elif .type == 2 and .item.type == "error" then "Workflow error: \(.item.data)"
            elif .type == 2 and .item.type == "result" then
                "Result outcome: \(.item.data.outcome); route checked: \(.item.data.routeChecked); charging checked: \(.item.data.chargingChecked)"
            elif .type == 2 and .item.type == "task" then
                "Manager task satisfied: \(.item.data.satisfied); next agent: \(.item.data.nextAgent)"
            else empty end' "$work/messages.json" >&2
        echo "FAIL: $stage." >&2
        exit 1
    fi
    curl --silent --show-error --fail-with-body --max-time 15 -b "$work/cookies" -X DELETE "$connection" >/dev/null
    connection=""
    echo "PASS: real $scenario agents, both endpoint forecasts and completed SignalR outcome."
done
echo "TRAVEL_VERIFICATION_SUCCEEDED"
