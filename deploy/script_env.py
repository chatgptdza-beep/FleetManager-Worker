import os


def require_env(name: str) -> str:
    value = os.getenv(name)
    if value and value.strip():
        return value.strip()
    raise RuntimeError(f"Missing required environment variable: {name}")


def optional_env(name: str) -> str | None:
    value = os.getenv(name)
    if value and value.strip():
        return value.strip()
    return None


def require_uuid_list(name: str) -> list[str]:
    value = require_env(name)
    items = [item.strip() for item in value.split(",") if item.strip()]
    if not items:
        raise RuntimeError(f"Environment variable {name} must contain at least one value.")
    return items


def sql_string_list(values: list[str]) -> str:
    return ",".join(f"'{value}'" for value in values)


SSH_HOST = require_env("FLEETMANAGER_DEPLOY_HOST")
SSH_USERNAME = optional_env("FLEETMANAGER_DEPLOY_USER") or "root"
SSH_PASSWORD = require_env("FLEETMANAGER_DEPLOY_PASSWORD")
