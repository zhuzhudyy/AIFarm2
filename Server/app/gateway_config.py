import json
import logging
import os
import re
import secrets
from dataclasses import dataclass
from pathlib import Path
from threading import RLock

from pydantic import ValidationError

from app.providers import (
    FarmProvider,
    MockProvider,
    OpenAIProvider,
    ProviderInputError,
)
from app.schemas import (
    GatewayConfigSpec,
    GatewayConfigureRequest,
    GatewayProbeSpec,
)


DEFAULT_MODEL = "deepseek-v4-flash"
CONFIG_VERSION = 1
MAX_CONFIG_BYTES = 16 * 1024
_GATEWAY_INSTANCE_ID_PATTERN = re.compile(r"^[0-9a-f]{32}$")

_logger = logging.getLogger("aifarm.ai_gateway")


@dataclass(frozen=True, repr=False)
class GatewayCredentials:
    provider: str
    model: str
    api_key: str


class LocalGatewayConfigStore:
    """Stores one user-local gateway configuration outside the repository."""

    def __init__(self, path: Path | None = None) -> None:
        self.path = (path or self.default_path()).expanduser().resolve()

    @staticmethod
    def default_path() -> Path:
        override = os.getenv("AIFARM_LOCAL_CONFIG_PATH", "").strip()
        if override:
            return Path(override)

        local_app_data = os.getenv("LOCALAPPDATA", "").strip()
        if os.name == "nt" and local_app_data:
            return Path(local_app_data) / "AIFarm" / "ai-gateway.json"

        xdg_config_home = os.getenv("XDG_CONFIG_HOME", "").strip()
        base = Path(xdg_config_home) if xdg_config_home else Path.home() / ".config"
        return base / "aifarm" / "ai-gateway.json"

    def load(self) -> GatewayCredentials | None:
        if not self.path.exists():
            return None
        if self.path.is_symlink() or not self.path.is_file():
            raise RuntimeError("The local gateway config path must be a regular file.")
        if self.path.stat().st_size > MAX_CONFIG_BYTES:
            raise RuntimeError("The local gateway config file is unexpectedly large.")

        payload = json.loads(self.path.read_text(encoding="utf-8"))
        if not isinstance(payload, dict) or set(payload) != {
            "version",
            "provider",
            "model",
            "api_key",
        }:
            raise RuntimeError("The local gateway config has an invalid shape.")
        if payload["version"] != CONFIG_VERSION:
            raise RuntimeError("The local gateway config version is unsupported.")

        try:
            validated = GatewayConfigureRequest.model_validate(
                {
                    "provider": payload["provider"],
                    "model": payload["model"],
                    "api_key": payload["api_key"] or None,
                    "persist": True,
                }
            )
        except ValidationError as error:
            raise RuntimeError("The local gateway config is invalid.") from error

        api_key = (
            validated.api_key.get_secret_value().strip()
            if validated.api_key is not None
            else ""
        )
        if validated.provider == "openai" and not api_key:
            raise RuntimeError("The local gateway config is missing its API key.")
        return GatewayCredentials(
            provider=validated.provider,
            model=validated.model,
            api_key=api_key,
        )

    def save(self, credentials: GatewayCredentials) -> None:
        self.path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        if self.path.exists() and self.path.is_symlink():
            raise RuntimeError("Refusing to replace a symbolic-link config file.")

        payload = {
            "version": CONFIG_VERSION,
            "provider": credentials.provider,
            "model": credentials.model,
            "api_key": credentials.api_key,
        }
        temporary_path = self.path.with_name(
            f".{self.path.name}.{secrets.token_hex(8)}.tmp"
        )
        descriptor = os.open(
            temporary_path,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL,
            0o600,
        )
        try:
            with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
                json.dump(payload, stream, ensure_ascii=False, separators=(",", ":"))
                stream.write("\n")
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary_path, self.path)
            os.chmod(self.path, 0o600)
        finally:
            if temporary_path.exists():
                temporary_path.unlink()

    def clear(self) -> None:
        if not self.path.exists():
            return
        if self.path.is_symlink() or not self.path.is_file():
            raise RuntimeError("Refusing to delete a non-regular config path.")
        self.path.unlink()


class GatewayProviderRuntime:
    """Owns the single shared provider and swaps it atomically between requests."""

    def __init__(
        self,
        provider: FarmProvider | None = None,
        store: LocalGatewayConfigStore | None = None,
    ) -> None:
        self._lock = RLock()
        self._store = store or LocalGatewayConfigStore()
        self._provider: FarmProvider
        self._model: str
        self._api_key: str
        self._source: str
        self._persisted: bool
        self._instance_id = self._instance_id_from_environment()

        if provider is not None:
            self._provider = provider
            self._model = str(getattr(provider, "model", DEFAULT_MODEL))
            self._api_key = ""
            self._source = "injected"
            self._persisted = False
            return

        environment_provider = os.getenv("AIFARM_PROVIDER")
        if environment_provider is not None:
            credentials = self._credentials_from_environment(environment_provider)
            self._activate(credentials, source="environment", persisted=False)
            return

        try:
            stored = self._store.load()
        except (OSError, RuntimeError, json.JSONDecodeError):
            _logger.warning(
                "local_gateway_config_invalid action=use_mock path_redacted=true"
            )
            stored = None

        if stored is not None:
            self._activate(stored, source="local_config", persisted=True)
        else:
            self._activate(
                GatewayCredentials("mock", DEFAULT_MODEL, ""),
                source="runtime",
                persisted=False,
            )

    @property
    def provider(self) -> FarmProvider:
        with self._lock:
            return self._provider

    def status(self) -> GatewayConfigSpec:
        with self._lock:
            return GatewayConfigSpec(
                provider=self._provider.name,
                model=self._model if self._provider.name == "openai" else None,
                api_key_required=self._provider.requires_api_key,
                api_key_configured=self._provider.api_key_configured,
                persisted=self._persisted,
                source=self._source,
                instance_id=self._instance_id,
            )

    def configure(self, request: GatewayConfigureRequest) -> GatewayConfigSpec:
        with self._lock:
            supplied_key = (
                request.api_key.get_secret_value().strip()
                if request.api_key is not None
                else ""
            )
            if request.provider == "openai":
                api_key = supplied_key or self._api_key
                if not api_key:
                    raise ProviderInputError(
                        "api_key_required",
                        "An API key is required when the provider is openai.",
                    )
            else:
                api_key = ""

            credentials = GatewayCredentials(
                provider=request.provider,
                model=request.model,
                api_key=api_key,
            )
            candidate = self._build_provider(credentials)
            if request.persist:
                self._store.save(credentials)

            self._provider = candidate
            self._model = credentials.model
            self._api_key = credentials.api_key
            self._source = "runtime"
            self._persisted = request.persist
            return self.status()

    def clear(self) -> GatewayConfigSpec:
        with self._lock:
            self._store.clear()
            self._provider = MockProvider()
            self._model = DEFAULT_MODEL
            self._api_key = ""
            self._source = "runtime"
            self._persisted = False
            return self.status()

    def probe(self) -> GatewayProbeSpec:
        provider = self.provider
        result = provider.probe()
        model = str(getattr(provider, "model", "")).strip() or None
        return GatewayProbeSpec(
            ok=result.ok,
            provider=provider.name,
            model=model,
            code=result.code,
            message=result.message,
        )

    def _activate(
        self,
        credentials: GatewayCredentials,
        *,
        source: str,
        persisted: bool,
    ) -> None:
        self._provider = self._build_provider(credentials)
        self._model = credentials.model
        self._api_key = credentials.api_key
        self._source = source
        self._persisted = persisted

    @staticmethod
    def _build_provider(credentials: GatewayCredentials) -> FarmProvider:
        if credentials.provider == "mock":
            return MockProvider()
        if credentials.provider == "openai":
            return OpenAIProvider(
                api_key=credentials.api_key,
                model=credentials.model,
            )
        raise RuntimeError("AIFARM_PROVIDER must be either 'mock' or 'openai'.")

    @staticmethod
    def _credentials_from_environment(provider_name: str) -> GatewayCredentials:
        normalized = provider_name.strip().lower()
        if normalized not in {"mock", "openai"}:
            raise RuntimeError("AIFARM_PROVIDER must be either 'mock' or 'openai'.")
        return GatewayCredentials(
            provider=normalized,
            model=os.getenv("OPENAI_MODEL", DEFAULT_MODEL).strip(),
            api_key=os.getenv("OPENAI_API_KEY", "").strip(),
        )

    @staticmethod
    def _instance_id_from_environment() -> str | None:
        instance_id = os.getenv("AIFARM_GATEWAY_INSTANCE_ID")
        if instance_id is None:
            return None
        if _GATEWAY_INSTANCE_ID_PATTERN.fullmatch(instance_id) is None:
            raise RuntimeError(
                "AIFARM_GATEWAY_INSTANCE_ID must be exactly 32 lowercase "
                "hexadecimal characters."
            )
        return instance_id
