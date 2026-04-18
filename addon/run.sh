#!/usr/bin/with-contenv bashio
set -euo pipefail

API_PORT=5188
WEB_PORT=8099
API_BASE_URL="http://127.0.0.1:${API_PORT}"

api_database_path="$(bashio::config 'api_database_path')"
workspace_path="$(bashio::config 'workspace_path')"
run_checkpoint_path="$(bashio::config 'run_checkpoint_path')"
mcp_config_path="$(bashio::config 'mcp_config_path')"
suppression_window_seconds="$(bashio::config 'external_signal_suppression_window_seconds')"

mkdir -p "$(dirname "${api_database_path}")"
mkdir -p "${workspace_path}"
mkdir -p "${run_checkpoint_path}"
mkdir -p "$(dirname "${mcp_config_path}")"

unset WorkflowNodes__EnabledPacks__0 || true
unset WorkflowNodes__DisabledPacks__0 || true

bashio::log.info "Starting Workflow.Api on :${API_PORT}"
ASPNETCORE_URLS="http://0.0.0.0:${API_PORT}" \
WorkflowStorage__DatabasePath="${api_database_path}" \
WorkflowArtifacts__WorkspacePath="${workspace_path}" \
WorkflowRuns__CheckpointPath="${run_checkpoint_path}" \
WorkflowMcp__ConfigPath="${mcp_config_path}" \
WorkflowRuns__ExternalSignalSuppressionWindowSeconds="${suppression_window_seconds}" \
/opt/workflow/api/Workflow.Api &
api_pid=$!

bashio::log.info "Starting Workflow.Web on :${WEB_PORT} (ingress)"
ASPNETCORE_URLS="http://0.0.0.0:${WEB_PORT}" \
ASPNETCORE_CONTENTROOT="/opt/workflow/web" \
Frontend__UseDevServer="false" \
Api__BaseUrl="${API_BASE_URL}" \
/opt/workflow/web/Workflow.Web &
web_pid=$!

term_handler() {
  bashio::log.info "Stopping Workflow Server"
  kill -TERM "${api_pid}" "${web_pid}" 2>/dev/null || true
  wait "${api_pid}" "${web_pid}" 2>/dev/null || true
}

trap term_handler TERM INT

set +e
wait -n "${api_pid}" "${web_pid}"
exit_code=$?
set -e

bashio::log.warning "One of services exited (code ${exit_code}); stopping both."
term_handler
exit "${exit_code}"
