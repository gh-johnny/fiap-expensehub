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
	$(DOTNET) bash -c 'dotnet test $(UNIT_TESTS) --no-build -p:CollectCoverage=true \
		&& dotnet reportgenerator -reports:artifacts/coverage/unit/coverage.cobertura.xml \
			-targetdir:artifacts/coverage/report -reporttypes:"Html;TextSummary;MarkdownSummaryGithub" \
		&& cat artifacts/coverage/report/Summary.txt | head -20'

.PHONY: coverage-integration
coverage-integration: build ## Informational coverage of the integration suite (I/O adapters)
	$(DOTNET) bash -c 'dotnet test $(INTEGRATION_TESTS) --no-build -p:CollectCoverage=true \
		&& dotnet reportgenerator -reports:artifacts/coverage/integration/coverage.cobertura.xml \
			-targetdir:artifacts/coverage/integration-report -reporttypes:"Html;TextSummary" \
		&& head -20 artifacts/coverage/integration-report/Summary.txt'

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
up: .env ## Build and start RabbitMQ, API and worker in the background
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

.PHONY: shell
shell: ## Interactive shell inside the SDK container
	$(DOTNET) bash

.PHONY: migration
migration: restore ## Add an EF Core migration: make migration NAME=AddSomething
	@test -n "$(NAME)" || (echo "Usage: make migration NAME=MigrationName" && exit 1)
	$(DOTNET) dotnet ef migrations add $(NAME) --project sources/ExpenseHub.Core --startup-project sources/ExpenseHub.Api

.PHONY: clean
clean: ## Remove build outputs and reports (keeps the NuGet cache and Docker volumes)
	find sources -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
	rm -rf artifacts
