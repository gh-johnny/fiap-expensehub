# ExpenseHub — every command runs inside containers. Host requirements: git, docker (Compose v2), make.
# Run `make help` to list the targets.

SHELL := /usr/bin/env bash
.SHELLFLAGS := -euo pipefail -c
.DEFAULT_GOAL := help

SDK_IMAGE ?= mcr.microsoft.com/dotnet/sdk:10.0.402
SOLUTION := sources/ExpenseHub.slnx
UNIT_TESTS := sources/ExpenseHub.UnitTests/ExpenseHub.UnitTests.csproj
INTEGRATION_TESTS := sources/ExpenseHub.IntegrationTests/ExpenseHub.IntegrationTests.csproj
COMPOSE ?= docker compose
SERVICE ?=
TTY := $(shell [ -t 0 ] && echo -it)
# NuGet cache shared with any host tooling and kept outside the repository (never scanned or committed).
NUGET_CACHE ?= $(HOME)/.nuget/packages

# Runs the .NET SDK container as the host user so generated files never belong to root.
DOTNET := docker run --rm $(TTY) \
	--user $(shell id -u):$(shell id -g) \
	-e HOME=/tmp/home -e DOTNET_CLI_HOME=/tmp/home \
	-e NUGET_PACKAGES=/nuget -v "$(NUGET_CACHE)":/nuget \
	-e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
	-e GITHUB_ACTIONS -e GITHUB_SHA \
	-v "$(CURDIR)":/work -w /work $(SDK_IMAGE)
# Local tools resolve through a per-container cache, so targets that call them restore first (fast, offline).
TOOLS := dotnet tool restore --verbosity quiet > /dev/null

.PHONY: help
help: ## List every target with its description
	@awk 'BEGIN {FS = ":.*## "; printf "\nExpenseHub — containerized commands\n\n"} \
		/^[a-zA-Z0-9_-]+:.*## / {printf "  \033[36m%-20s\033[0m %s\n", $$1, $$2} \
		/^## / {printf "\n\033[1m%s\033[0m\n", substr($$0, 4)}' $(MAKEFILE_LIST)

## Build and quality

.PHONY: restore
restore: ## Restore NuGet packages and local tools (dotnet-ef, reportgenerator)
	@mkdir -p "$(NUGET_CACHE)"
	$(DOTNET) bash -c 'dotnet tool restore && dotnet restore $(SOLUTION)'

.PHONY: build
build: restore ## Compile the solution treating warnings as errors
	$(DOTNET) dotnet build $(SOLUTION) --no-restore -warnaserror

.PHONY: format
format: restore ## Check formatting and analyzer style rules without changing files
	$(DOTNET) dotnet format $(SOLUTION) --no-restore --verify-no-changes --severity warn

.PHONY: format-fix
format-fix: restore ## Apply formatting and style fixes
	$(DOTNET) dotnet format $(SOLUTION) --no-restore --severity warn

.PHONY: quality
quality: restore ## Run the professor's code-quality pipeline locally (PowerShell inside the SDK image)
	$(DOTNET) pwsh -NoProfile -File scripts/Invoke-CodeQuality.ps1

## Tests

.PHONY: test
test: test-unit test-integration ## Run unit and integration suites

.PHONY: test-unit
test-unit: build ## Run unit tests (no database, network or broker)
	$(DOTNET) dotnet test $(UNIT_TESTS) --no-build

.PHONY: test-integration
test-integration: build ## Run integration tests against real SQLite files
	$(DOTNET) dotnet test $(INTEGRATION_TESTS) --no-build

.PHONY: coverage
coverage: build ## Unit coverage with the 95 % line+branch gate; HTML report in artifacts/coverage/report
	$(DOTNET) bash -c '$(TOOLS) && dotnet test $(UNIT_TESTS) --no-build -p:CollectCoverage=true \
		&& dotnet reportgenerator -reports:artifacts/coverage/unit/coverage.cobertura.xml \
			-targetdir:artifacts/coverage/report -reporttypes:"Html;TextSummary;MarkdownSummaryGithub" \
		&& cat artifacts/coverage/report/Summary.txt | head -20'

.PHONY: coverage-integration
coverage-integration: build ## Informational coverage of the integration suite (I/O adapters)
	$(DOTNET) bash -c '$(TOOLS) && dotnet test $(INTEGRATION_TESTS) --no-build -p:CollectCoverage=true \
		&& dotnet reportgenerator -reports:artifacts/coverage/integration/coverage.cobertura.xml \
			-targetdir:artifacts/coverage/integration-report -reporttypes:"Html;TextSummary" \
		&& head -20 artifacts/coverage/integration-report/Summary.txt'

.PHONY: mutation
mutation: build ## Mutation testing (Stryker.NET) of Core Reports/** with the unit suite; HTML in artifacts/mutation
	$(DOTNET) bash -c '$(TOOLS) && cd sources/ExpenseHub.UnitTests \
		&& dotnet stryker --config-file stryker-config.json --output /work/artifacts/mutation'

.PHONY: demo
demo: ## Run the HTTP demonstration (104 CP2 scenarios) against a disposable API and SQLite file
	$(DOTNET) pwsh -NoProfile -File scripts/Invoke-ExpenseHubVerification.ps1

.PHONY: verify
verify: build format coverage test-integration ## Everything a PR must pass: build, format, coverage gate, integration

## Runtime (Docker Compose)

.env:
	@./scripts/new-env.sh

.PHONY: env
env: .env ## Create .env from .env.example with random local secrets (never committed)

.PHONY: up
up: .env ## Build images and start the stack in the background, waiting for health checks
	$(COMPOSE) up -d --build --wait

.PHONY: down
down: ## Stop and remove containers; volumes (database, broker) are preserved
	$(COMPOSE) down

.PHONY: ps
ps: ## Show service state and health
	$(COMPOSE) ps

.PHONY: logs
logs: ## Follow logs (SERVICE=api|worker|rabbitmq, default: all)
	$(COMPOSE) logs -f --tail 100 $(SERVICE)

.PHONY: restart
restart: ## Restart one service: make restart SERVICE=worker
	@test -n "$(SERVICE)" || (echo "Usage: make restart SERVICE=api|worker|rabbitmq" && exit 1)
	$(COMPOSE) restart $(SERVICE)

## Release (published images)

RELEASE := $(COMPOSE) -f compose.release.yaml
# Plain HTTP over bash /dev/tcp inside the API container (the same probe as its health check): no curl on the host.
VERSION_PROBE := exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /version HTTP/1.0\r\n\r\n" >&3 && tail -1 <&3

.PHONY: release-pull
release-pull: .env ## Pull the published images for IMAGE_TAG (set in .env)
	$(RELEASE) pull

.PHONY: release-up
release-up: .env ## Start RabbitMQ + the published API and worker, waiting for health checks
	$(RELEASE) up -d --wait

.PHONY: release-ps
release-ps: ## Show the release stack and the image of each container
	$(RELEASE) ps --format 'table {{.Service}}\t{{.Image}}\t{{.Status}}'

.PHONY: release-logs
release-logs: ## Follow release logs (SERVICE=api|worker|rabbitmq, default: all)
	$(RELEASE) logs -f --tail 100 $(SERVICE)

.PHONY: release-stop-worker
release-stop-worker: ## Stop only the worker (messages wait in the queue)
	$(RELEASE) stop worker

.PHONY: release-start-worker
release-start-worker: ## Start the worker again
	$(RELEASE) up -d --wait worker

.PHONY: release-restart-worker
release-restart-worker: ## Restart only the worker
	$(RELEASE) restart worker

.PHONY: release-recreate
release-recreate: .env ## Recreate the API and worker containers; volumes (database, keys, broker) are kept
	$(RELEASE) up -d --force-recreate --wait api worker

.PHONY: release-version
release-version: ## Show the commit served by /version and the revision label of both running containers
	@printf '%-24s' "api GET /version"; $(RELEASE) exec -T api bash -c '$(VERSION_PROBE)'; echo
	@for service in api worker; do \
		printf '%-24s' "$$service label revision"; \
		docker inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}  ({{ .Config.Image }})' \
			"$$($(RELEASE) ps -q $$service)"; \
	done

EXPERIMENTS_IMAGE ?= docker:28.5.1-cli

.PHONY: experiments
experiments: release-up ## Run E01–E05 and the broker-outage diagnosis on the published images; report in artifacts/experiments
	docker run --rm --network fiap-expensehub-release_default \
		-v /var/run/docker.sock:/var/run/docker.sock -v "$(CURDIR)":"$(CURDIR)" -w "$(CURDIR)" \
		-e HOST_UID=$(shell id -u) $(EXPERIMENTS_IMAGE) \
		sh -c 'apk add --no-cache --quiet bash curl jq > /dev/null && bash scripts/run-experiments.sh'

.PHONY: release-down
release-down: ## Stop and remove the release containers; volumes are preserved
	$(RELEASE) down

.PHONY: shell
shell: ## Interactive shell inside the SDK container
	$(DOTNET) bash

.PHONY: migration
migration: restore ## Add an EF Core migration: make migration NAME=AddSomething
	@test -n "$(NAME)" || (echo "Usage: make migration NAME=MigrationName" && exit 1)
	$(DOTNET) bash -c '$(TOOLS) && dotnet ef migrations add $(NAME) --project sources/ExpenseHub.Core --startup-project sources/ExpenseHub.Api'

.PHONY: clean
clean: ## Remove build outputs and reports (keeps the NuGet cache and Docker volumes)
	find sources -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
	rm -rf artifacts
