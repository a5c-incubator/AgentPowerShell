# Configuration Reference

Configuration is loaded from YAML into `AgentPowerShellConfig`.

## `server`

- `ipc_socket`
- `http_port`
- `http_bind`

## `auth`

- `mode`: `none`, `apikey`, `oidc`, `hybrid`
- `api_key`
- `oidc.issuer`
- `oidc.audience`
- `oidc.client_id`
- `oidc.client_secret`
- `oidc.access_token_lifetime_minutes`
- `oidc.refresh_token_lifetime_minutes`

## `logging`

- `level`
- `console`
- `file`
- `structured`

## `sessions`

- `max_concurrent`
- `idle_timeout_minutes`
- `max_lifetime_minutes`
- `reap_interval_seconds`

## `policy`

- `default_policy`
- `watch_for_changes`

## `events`

Each entry in `stores` accepts:

- `type`
- `path`
- `max_size_mb`
- `max_backups`

## `approval`

- `mode`
- `timeout_seconds`
- `rest_api_endpoint`
- `totp_secrets_path`
- `webauthn_secrets_path`

## `llm_proxy`

- `enabled`
- `listen_port`
- `providers`
- `requests_per_minute`
- `tokens_per_minute`

## `shim`

- `fail_mode`
- `max_consecutive_failures`
