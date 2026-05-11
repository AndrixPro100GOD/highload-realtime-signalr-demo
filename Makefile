BASE_URL ?= http://localhost:8080
CONNECTIONS ?= 1000
LIGHT_CONNECTIONS ?= 1000
MEDIUM_CONNECTIONS ?= 3000
HEAVY_CONNECTIONS ?= 5000
RAMP_UP ?= 60
WARM_UP ?= 30
STEADY ?= 120
RAMP_DOWN ?= 15
PAYLOAD_BYTES ?= 128
SEND_INTERVAL_MS ?= 1000
CONNECT_CONCURRENCY ?= 16
CONNECT_ACQUIRE_TIMEOUT_MS ?= 250
CONNECT_TIMEOUT_MS ?= 15000
CONNECT_RETRY_DELAY_MS ?= 500
MAX_FAIL_COUNT ?= 50000
PREFLIGHT_TIMEOUT_MS ?= 5000
EARLY_ERROR_LOG_EVERY ?= 100
GROUP ?= benchmark
APP_SCALE ?= 3
NGINX_SCALE ?= 2
API_SCALE ?= 2
VIDEO_URL ?= http://localhost:8081/video/index.m3u8
SERVER_PROJECT := Server/Server.csproj
API_PROJECT := Api/Api.csproj
LOADTEST_PROJECT := LoadTester/LoadTester.csproj
K6_SCRIPT := tests/load/signalr.js

.PHONY: restore build run-server run-api compose-up compose-down compose-scale compose-up-hybrid scale-realtime scale-nginx loadtest loadtest-light loadtest-medium loadtest-heavy loadtest-fanout loadtest-api loadtest-video-smoke k6

restore:
	dotnet restore

build:
	dotnet build $(SERVER_PROJECT)
	dotnet build $(API_PROJECT)
	dotnet build $(LOADTEST_PROJECT)

run-server:
	dotnet run --project $(SERVER_PROJECT)

run-api:
	dotnet run --project $(API_PROJECT)

compose-up:
	docker compose up --build -d

compose-down:
	docker compose down --remove-orphans

compose-scale:
	docker compose up --build -d --scale realtime-svc=$(APP_SCALE)

compose-up-hybrid:
	docker compose up --build -d --scale realtime-svc=$(APP_SCALE) --scale nginx-l7=$(NGINX_SCALE) --scale api-svc=$(API_SCALE)

scale-realtime:
	docker compose up --build -d --scale realtime-svc=$(APP_SCALE)

scale-nginx:
	docker compose up --build -d --scale nginx-l7=$(NGINX_SCALE)

loadtest:
	dotnet run --project $(LOADTEST_PROJECT) -- --base-url=$(BASE_URL) --connections=$(CONNECTIONS) --warm-up=$(WARM_UP) --ramp-up=$(RAMP_UP) --steady=$(STEADY) --ramp-down=$(RAMP_DOWN) --payload-bytes=$(PAYLOAD_BYTES) --send-interval-ms=$(SEND_INTERVAL_MS) --connect-concurrency=$(CONNECT_CONCURRENCY) --connect-acquire-timeout-ms=$(CONNECT_ACQUIRE_TIMEOUT_MS) --connect-timeout-ms=$(CONNECT_TIMEOUT_MS) --connect-retry-delay-ms=$(CONNECT_RETRY_DELAY_MS) --max-fail-count=$(MAX_FAIL_COUNT) --preflight-timeout-ms=$(PREFLIGHT_TIMEOUT_MS) --early-error-log-every=$(EARLY_ERROR_LOG_EVERY) --group=$(GROUP) --traffic-profile=targeted --scenario=signalr-targeted

loadtest-light:
	dotnet run --project $(LOADTEST_PROJECT) -- --base-url=$(BASE_URL) --connections=$(LIGHT_CONNECTIONS) --warm-up=30 --warm-up-connections=100 --ramp-up=60 --steady=120 --ramp-down=20 --payload-bytes=64 --send-interval-ms=1000 --connect-concurrency=16 --connect-acquire-timeout-ms=250 --connect-timeout-ms=15000 --connect-retry-delay-ms=500 --max-fail-count=$(MAX_FAIL_COUNT) --preflight-timeout-ms=$(PREFLIGHT_TIMEOUT_MS) --early-error-log-every=$(EARLY_ERROR_LOG_EVERY) --group=$(GROUP) --traffic-profile=targeted --scenario=signalr-light-targeted

loadtest-medium:
	dotnet run --project $(LOADTEST_PROJECT) -- --base-url=$(BASE_URL) --connections=$(MEDIUM_CONNECTIONS) --warm-up=60 --warm-up-connections=300 --ramp-up=180 --steady=300 --ramp-down=30 --payload-bytes=96 --send-interval-ms=1000 --connect-concurrency=16 --connect-acquire-timeout-ms=250 --connect-timeout-ms=15000 --connect-retry-delay-ms=500 --max-fail-count=$(MAX_FAIL_COUNT) --preflight-timeout-ms=$(PREFLIGHT_TIMEOUT_MS) --early-error-log-every=$(EARLY_ERROR_LOG_EVERY) --group=$(GROUP) --traffic-profile=targeted --scenario=signalr-medium-targeted

loadtest-heavy:
	dotnet run --project $(LOADTEST_PROJECT) -- --base-url=$(BASE_URL) --connections=$(HEAVY_CONNECTIONS) --warm-up=180 --warm-up-connections=100 --ramp-up=600 --steady=600 --ramp-down=60 --payload-bytes=128 --send-interval-ms=1000 --connect-concurrency=16 --connect-acquire-timeout-ms=250 --connect-timeout-ms=30000 --connect-retry-delay-ms=1000 --max-fail-count=$(MAX_FAIL_COUNT) --preflight-timeout-ms=$(PREFLIGHT_TIMEOUT_MS) --early-error-log-every=$(EARLY_ERROR_LOG_EVERY) --group=$(GROUP) --traffic-profile=targeted --scenario=signalr-heavy-targeted

loadtest-fanout:
	dotnet run --project $(LOADTEST_PROJECT) -- --base-url=$(BASE_URL) --connections=$(CONNECTIONS) --warm-up=30 --warm-up-connections=100 --ramp-up=$(RAMP_UP) --steady=$(STEADY) --ramp-down=$(RAMP_DOWN) --payload-bytes=$(PAYLOAD_BYTES) --send-interval-ms=250 --connect-concurrency=$(CONNECT_CONCURRENCY) --connect-acquire-timeout-ms=$(CONNECT_ACQUIRE_TIMEOUT_MS) --connect-timeout-ms=$(CONNECT_TIMEOUT_MS) --connect-retry-delay-ms=$(CONNECT_RETRY_DELAY_MS) --max-fail-count=$(MAX_FAIL_COUNT) --preflight-timeout-ms=$(PREFLIGHT_TIMEOUT_MS) --early-error-log-every=$(EARLY_ERROR_LOG_EVERY) --group=$(GROUP) --traffic-profile=mixed --batch-every=2 --scenario=signalr-fanout-mixed

loadtest-api:
	k6 run -e BASE_URL=$(BASE_URL) -e VUS=$(CONNECTIONS) tests/load/api-smoke.js

loadtest-video-smoke:
	curl -fsS $(VIDEO_URL) > /dev/null

k6:
	k6 run -e BASE_URL=$(BASE_URL) -e VUS=$(CONNECTIONS) -e RAMP_UP=$(RAMP_UP)s -e STEADY=$(STEADY)s -e RAMP_DOWN=$(RAMP_DOWN)s -e PAYLOAD_BYTES=$(PAYLOAD_BYTES) -e GROUP_NAME=$(GROUP) $(K6_SCRIPT)
