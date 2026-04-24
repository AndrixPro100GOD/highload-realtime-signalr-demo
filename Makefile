BASE_URL ?= http://localhost:8080
CONNECTIONS ?= 1000
RAMP_UP ?= 60
STEADY ?= 120
RAMP_DOWN ?= 15
PAYLOAD_BYTES ?= 128
GROUP ?= benchmark
APP_SCALE ?= 3
SERVER_PROJECT := Server/Server.csproj
LOADTEST_PROJECT := LoadTester/LoadTester.csproj
K6_SCRIPT := tests/load/signalr.js

.PHONY: restore build run-server compose-up compose-down compose-scale loadtest k6

restore:
	dotnet restore

build:
	dotnet build $(SERVER_PROJECT)
	dotnet build $(LOADTEST_PROJECT)

run-server:
	dotnet run --project $(SERVER_PROJECT)

compose-up:
	docker compose up --build -d

compose-down:
	docker compose down --remove-orphans

compose-scale:
	docker compose up --build -d --scale app=$(APP_SCALE)

loadtest:
	dotnet run --project $(LOADTEST_PROJECT) -- --base-url=$(BASE_URL) --connections=$(CONNECTIONS) --ramp-up=$(RAMP_UP) --steady=$(STEADY) --ramp-down=$(RAMP_DOWN) --payload-bytes=$(PAYLOAD_BYTES) --group=$(GROUP)

k6:
	k6 run -e BASE_URL=$(BASE_URL) -e VUS=$(CONNECTIONS) -e RAMP_UP=$(RAMP_UP)s -e STEADY=$(STEADY)s -e RAMP_DOWN=$(RAMP_DOWN)s -e PAYLOAD_BYTES=$(PAYLOAD_BYTES) -e GROUP_NAME=$(GROUP) $(K6_SCRIPT)
