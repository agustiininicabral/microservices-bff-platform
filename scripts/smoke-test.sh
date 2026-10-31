#!/usr/bin/env bash
set -euo pipefail

export Jwt__SigningKey="$(openssl rand -base64 48)"
export ASPNETCORE_ENVIRONMENT=Development

log_dir="$(mktemp -d)"
pids=()

cleanup() {
  for pid in "${pids[@]:-}"; do
    kill "$pid" 2>/dev/null || true
  done
  for pid in "${pids[@]:-}"; do
    wait "$pid" 2>/dev/null || true
  done
  rm -f src/Identity.Api/identity.db* src/Catalog.Api/catalog.db* src/Orders.Api/orders.db*
  rm -rf "$log_dir"
}
trap cleanup EXIT

start_service() {
  local name="$1"
  local project="$2"
  local port="$3"
  (cd "src/$project" && dotnet "bin/Release/net8.0/$project.dll" --urls "http://localhost:$port") >"$log_dir/$name.log" 2>&1 &
  pids+=("$!")
}

wait_for_gateway() {
  for attempt in $(seq 1 45); do
    if curl --silent --fail http://localhost:5080/health/ready >/dev/null; then
      return 0
    fi
    sleep 1
  done
  echo "Gateway did not become ready. Service logs:" >&2
  for log in "$log_dir"/*.log; do
    echo "===== $log =====" >&2
    cat "$log" >&2
  done
  return 1
}

assert_status() {
  local expected="$1"
  local actual="$2"
  local description="$3"
  if [[ "$actual" != "$expected" ]]; then
    echo "$description: expected HTTP $expected, received $actual" >&2
    exit 1
  fi
}

start_service identity Identity.Api 5100
start_service catalog Catalog.Api 5101
start_service orders Orders.Api 5102
start_service web-bff Web.Bff 5103
start_service gateway ApiGateway 5080

wait_for_gateway

unauthorized_status="$(curl --silent --output /dev/null --write-out '%{http_code}' http://localhost:5080/api/catalog/products)"
assert_status 401 "$unauthorized_status" "Anonymous catalog request"

email="smoke-$(date +%s%N)@example.com"
password="SmokeTestPassword123"

register_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --request POST http://localhost:5080/api/auth/register \
  --header 'Content-Type: application/json' \
  --data "{\"email\":\"$email\",\"password\":\"$password\"}")"
assert_status 201 "$register_status" "User registration"

login_response="$(curl --silent --fail \
  --request POST http://localhost:5080/api/auth/login \
  --header 'Content-Type: application/json' \
  --data "{\"email\":\"$email\",\"password\":\"$password\"}")"
token="$(jq -r '.accessToken // empty' <<<"$login_response")"
if [[ -z "$token" ]]; then
  echo "Login did not return an access token." >&2
  exit 1
fi

catalog_response="$(curl --silent --fail http://localhost:5080/api/catalog/products \
  --header "Authorization: Bearer $token")"
jq -e 'length == 3' <<<"$catalog_response" >/dev/null
jq -e '[.[] | select(.category == "Peripherals")] | length == 2' <<<"$catalog_response" >/dev/null

dashboard_response="$(curl --silent --fail http://localhost:5080/api/web/dashboard \
  --header "Authorization: Bearer $token")"
jq -e '.products | length == 3' <<<"$dashboard_response" >/dev/null
jq -e '.orders | length == 0' <<<"$dashboard_response" >/dev/null

order_response="$(curl --silent --fail \
  --request POST http://localhost:5080/api/orders \
  --header "Authorization: Bearer $token" \
  --header 'Content-Type: application/json' \
  --data '{"items":[{"productId":"10000000-0000-0000-0000-000000000001","quantity":2}]}')"
order_id="$(jq -r '.id // empty' <<<"$order_response")"
if [[ -z "$order_id" ]]; then
  echo "Order creation did not return an order identifier." >&2
  exit 1
fi

orders_response="$(curl --silent --fail http://localhost:5080/api/orders \
  --header "Authorization: Bearer $token")"
jq -e --arg id "$order_id" 'length == 1 and .[0].id == $id' <<<"$orders_response" >/dev/null

second_email="smoke-other-$(date +%s%N)@example.com"
curl --silent --fail --request POST http://localhost:5080/api/auth/register \
  --header 'Content-Type: application/json' \
  --data "{\"email\":\"$second_email\",\"password\":\"$password\"}" >/dev/null
second_login="$(curl --silent --fail --request POST http://localhost:5080/api/auth/login \
  --header 'Content-Type: application/json' \
  --data "{\"email\":\"$second_email\",\"password\":\"$password\"}")"
second_token="$(jq -r '.accessToken' <<<"$second_login")"
ownership_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  "http://localhost:5080/api/orders/$order_id" \
  --header "Authorization: Bearer $second_token")"
assert_status 404 "$ownership_status" "Cross-user order access"

echo "Smoke test passed: Gateway, JWT authentication, Catalog, Orders, BFF aggregation, persistence, and user isolation."
