COMPOSE ?= docker compose
COMPOSE_PROJECT ?= perenearchive
TEST_COMPOSE_PROJECT ?= perenearchive-test
ARGS ?=

DOCKER_HOST := $(shell \
	if [ -S /var/run/docker.sock ]; then \
		echo unix:///var/run/docker.sock; \
	elif [ -S /run/user/$$(id -u)/podman/podman.sock ]; then \
		echo unix:///run/user/$$(id -u)/podman/podman.sock; \
	elif [ -S /run/user/$$(id -u)/docker.sock ]; then \
		echo unix:///run/user/$$(id -u)/docker.sock; \
	fi)
export DOCKER_HOST

.PHONY: help docker-env docker-build docker-run docker-run-bg docker-down docker-reset docker-logs docker-ps docker-shell docker-exec dotnet dotnet-new test docker-test docker-test-shell get-url get-url-nas https-cert archive-group archive-share

help:
	@printf '%s\n' \
		'Before running: copy .env.example to .env and set PERENE_ARCHIVE_ROOT' \
		'make docker-build              Build the .NET 10 SDK image' \
		'make dotnet-new                Generate the Blazor solution and xUnit project' \
		'make docker-run                Start the app with hot reload' \
		'make docker-run-bg             Start the app in the background' \
		'make docker-down               Stop the app' \
		'make docker-reset              Stop the app and delete Docker volumes' \
		'make docker-logs               Follow application logs' \
		'make docker-shell              Open a new .NET SDK container shell' \
		'make docker-exec               Open the running web container shell' \
		'make dotnet ARGS="build"       Run any dotnet command in Docker' \
		'make test                      Run tests in an isolated stack' \
		'make archive-group             Create or verify the host perenearchive group' \
		'make archive-share USER=name [PERENE_ARCHIVE_ROOT=/path]  Grant an existing account archive-group access' \
		'make get-url                   Show the URL to access the app from other LAN devices' \
		'make https-cert                Generate the optional self-signed LAN HTTPS certificate'

docker-env:
	@echo "export DOCKER_HOST=$(DOCKER_HOST)"

docker-build:
	$(COMPOSE) -p $(COMPOSE_PROJECT) build

dotnet-new: docker-build
	$(COMPOSE) -p $(COMPOSE_PROJECT) run --rm --no-deps webapp dotnet new sln -n PereneArchive
	$(COMPOSE) -p $(COMPOSE_PROJECT) run --rm --no-deps webapp dotnet new blazor -o WebApp --framework net10.0 --interactivity WebAssembly
	$(COMPOSE) -p $(COMPOSE_PROJECT) run --rm --no-deps webapp dotnet new xunit -o WebApp.Tests --framework net10.0
	$(COMPOSE) -p $(COMPOSE_PROJECT) run --rm --no-deps webapp dotnet sln PereneArchive.slnx add WebApp/WebApp/WebApp.csproj WebApp/WebApp.Client/WebApp.Client.csproj WebApp.Tests/WebApp.Tests.csproj
	$(COMPOSE) -p $(COMPOSE_PROJECT) run --rm --no-deps webapp dotnet add WebApp.Tests/WebApp.Tests.csproj reference WebApp/WebApp/WebApp.csproj

docker-run:
	$(COMPOSE) -p $(COMPOSE_PROJECT) up --build

docker-run-bg:
	$(COMPOSE) -p $(COMPOSE_PROJECT) up --build --detach

docker-down:
	$(COMPOSE) -p $(COMPOSE_PROJECT) down --remove-orphans

docker-reset:
	$(COMPOSE) -p $(COMPOSE_PROJECT) down --volumes --remove-orphans

docker-logs:
	$(COMPOSE) -p $(COMPOSE_PROJECT) logs --follow webapp

docker-ps:
	$(COMPOSE) -p $(COMPOSE_PROJECT) ps

docker-shell:
	$(COMPOSE) -p $(COMPOSE_PROJECT) run --rm --build webapp bash

docker-exec:
	$(COMPOSE) -p $(COMPOSE_PROJECT) exec webapp bash

dotnet:
	$(COMPOSE) -p $(COMPOSE_PROJECT) run --rm --no-deps --build webapp dotnet $(ARGS)

test: docker-test

docker-test:
	@status=0; \
	$(COMPOSE) -p $(TEST_COMPOSE_PROJECT) -f docker-compose.test.yml run --rm --build tests || status=$$?; \
	$(COMPOSE) -p $(TEST_COMPOSE_PROJECT) -f docker-compose.test.yml down --volumes --remove-orphans; \
	exit $$status

docker-test-shell:
	$(COMPOSE) -p $(TEST_COMPOSE_PROJECT) -f docker-compose.test.yml run --rm --build tests bash

https-cert:
	@mkdir -p https
	$(COMPOSE) -p $(COMPOSE_PROJECT) run --rm --no-deps --build webapp sh scripts/generate-https-cert.sh

archive-group:
	@sudo sh scripts/setup-archive-group.sh

archive-share:
	@if [ "$(origin USER)" != "command line" ] || [ -z "$(USER)" ]; then \
		echo 'Usage: make archive-share USER=<existing-account>' >&2; \
		exit 2; \
	fi
	@archive_root="$${PERENE_ARCHIVE_ROOT:-$$(sed -n 's/^PERENE_ARCHIVE_ROOT=//p' .env 2>/dev/null | head -n 1)}"; \
	archive_root="$${archive_root:-/home/PereneArchive}"; \
	sudo env PERENE_ARCHIVE_ROOT="$$archive_root" sh scripts/share-archive.sh "$(USER)"

get-url:
	@iface=$$(ip -o link show | awk -F': ' '{print $$2}' | grep -m1 '^wl'); \
	if [ -z "$$iface" ]; then \
		echo "No Wi-Fi interface found (looked for one starting with 'wl')." >&2; \
		exit 1; \
	fi; \
	ip=$$(ip -4 -o addr show "$$iface" | awk '{print $$4}' | cut -d/ -f1); \
	if [ -z "$$ip" ]; then \
		echo "Interface $$iface has no IPv4 address (not connected?)." >&2; \
		exit 1; \
	fi; \
	port=$$(grep -m1 '^WEBAPP_PORT=' .env 2>/dev/null | cut -d= -f2); \
	port=$${port:-8080}; \
	echo "Access PereneArchive on http://$$ip:$$port"; \
	if [ -f https/perene.pfx ]; then \
		httpsPort=$$(grep -m1 '^HTTPS_WEBAPP_PORT=' .env 2>/dev/null | cut -d= -f2); \
		httpsPort=$${httpsPort:-8443}; \
		echo "Access PereneArchive on https://$$ip:$$httpsPort (import ./https/perene.crt on the client first)"; \
	fi

get-url-nas:
	@iface=$$(ip route | awk '/^default/ {print $$5; exit}'); \
	ip=$$(ip -4 -o addr show "$$iface" | awk '{print $$4}' | cut -d/ -f1); \
	port=$$(grep -m1 '^WEBAPP_PORT=' .env 2>/dev/null | cut -d= -f2); \
	port=$${port:-8080}; \
	echo "Access PereneArchive on http://$$ip:$$port"; \
	if [ -f https/perene.pfx ]; then \
		httpsPort=$$(grep -m1 '^HTTPS_WEBAPP_PORT=' .env 2>/dev/null | cut -d= -f2); \
		httpsPort=$${httpsPort:-8443}; \
		echo "Access PereneArchive on https://$$ip:$$httpsPort (import ./https/perene.crt on the client first)"; \
	fi
