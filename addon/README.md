# Workflow Server Add-on

Home Assistant add-on with:
- `Workflow.Web` (ingress UI on port `8099`)
- `Workflow.Api` (internal API on port `5188`)

## Options

- `api_database_path` — SQLite path for workflow storage (default `/data/workflow.db`)
- `external_signal_suppression_window_seconds` — idempotency window for `POST /signals/{source}`

## Install/Update Flow

1. Add this repository to Home Assistant Add-on Store:
   `https://github.com/MaxMinsk/HAWorkflows`
2. Install **Workflow Server** add-on.
3. Configure options and start add-on.
4. Open add-on via ingress sidebar.

Image is published to:
- `ghcr.io/maxminsk/haworkflows:<version>`
- `ghcr.io/maxminsk/haworkflows:latest`
