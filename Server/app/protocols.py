"""Small HTTP adapters. Protocol selection is explicit and never inferred from a model name."""

import json
import re
from dataclasses import dataclass
from typing import Any
from urllib.parse import parse_qsl, quote, urlencode, urlsplit, urlunsplit

import httpx


class UpstreamError(RuntimeError):
    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code
        self.message = message


def resolve_endpoint(base_url: str, protocol: str, model: str) -> str:
    parsed = urlsplit(base_url.strip())
    if parsed.scheme not in {"http", "https"} or not parsed.hostname:
        raise ValueError("模型服务地址必须是有效的 http:// 或 https:// 地址。")
    if parsed.username or parsed.password or parsed.fragment:
        raise ValueError("地址不可包含用户名、密码或 # 片段；密钥请填入 API Key。")
    if any(k.lower() in {"key", "api_key", "api-key", "apikey", "token", "access_token", "access-token", "authorization"} for k, _ in parse_qsl(parsed.query)):
        raise ValueError("地址中不可包含密钥参数；密钥请填入 API Key。")
    path = parsed.path.rstrip("/")
    suffixes = {"chat_completions": "/chat/completions", "responses": "/responses", "anthropic": "/messages"}
    if protocol not in {*suffixes, "gemini"}:
        raise ValueError("不支持的协议，请明确选择 Chat Completions、Responses、Anthropic 或 Gemini。")
    known_suffix = next((s for s in suffixes.values() if path.endswith(s)), None)
    is_gemini_endpoint = path.endswith(":generateContent")
    if known_suffix or is_gemini_endpoint:
        expected = suffixes.get(protocol)
        if (known_suffix and known_suffix != expected) or (is_gemini_endpoint and protocol != "gemini"):
            raise ValueError("完整请求端点与选择的协议不一致。")
        return urlunsplit(parsed._replace(path=path))
    if protocol == "gemini":
        if not re.search(r"/v\d+(?:beta\d*)?$", path):
            path += "/v1beta"
        path += "/models/" + quote(model.removeprefix("models/"), safe="-._") + ":generateContent"
    else:
        if not re.search(r"/v\d+(?:beta\d*)?$", path):
            path += "/v1"
        path += suffixes[protocol]
    return urlunsplit(parsed._replace(path=path))


def safe_endpoint(endpoint: str) -> str:
    parsed = urlsplit(endpoint)
    query = urlencode([(key, "[redacted]") for key, _ in parse_qsl(parsed.query)])
    return urlunsplit(parsed._replace(query=query))


def parse_json_output(text: str) -> dict[str, Any]:
    text = text.strip()
    fence = re.fullmatch(r"```(?:json)?\s*([\s\S]*?)\s*```", text, re.IGNORECASE)
    if fence:
        text = fence.group(1)
    value = json.loads(text)
    if not isinstance(value, dict):
        raise ValueError("输出必须是一个 JSON 对象。")
    return value


def diagnose(error: Exception, secret: str = "") -> UpstreamError:
    if isinstance(error, UpstreamError):
        return error
    name = type(error).__name__
    if isinstance(error, (httpx.TimeoutException, TimeoutError)) or name == "APITimeoutError":
        return UpstreamError("timeout", "上游推理超时，请检查服务负载或稍后重新连接。")
    if isinstance(error, (httpx.ConnectError, ConnectionError)) or name == "APIConnectionError":
        return UpstreamError("connection_failed", "无法连接模型服务，请检查服务地址、网络和 HTTPS 证书。")
    status = getattr(error, "status_code", None)
    response = getattr(error, "response", None)
    if response is not None:
        status = response.status_code
        raw = response.text[:4000]
        try:
            body = response.json()
            info = body.get("error", body) if isinstance(body, dict) else {}
            detail = str(info.get("message", "")) if isinstance(info, dict) else str(info)
        except (ValueError, AttributeError):
            detail = ""
    else:
        raw, detail = str(error), ""
    lowered = raw.lower()
    if any(word in lowered for word in ("insufficient_quota", "insufficient balance", "credit balance", "billing", "quota exceeded")) or status == 402:
        code, message = "quota_exceeded", "余额或配额不足，请检查账号计费与配额。"
    elif status == 401 or name == "AuthenticationError":
        code, message = "authentication_failed", "鉴权失败，请检查 API Key。"
    elif status == 403:
        code, message = "permission_denied", "账号无权使用此服务或模型。"
    elif status == 429 or name == "RateLimitError":
        code, message = "rate_limited", "上游限流，居民将暂时使用本地规则并稍后重试。"
    elif status == 404 and any(word in lowered for word in ("model_not_found", "model not found", "model does not exist", "unknown model")):
        code, message = "model_not_found", "模型名称不存在或账号无权访问该模型。"
    elif status in {404, 405}:
        code, message = "endpoint_error", "请求路径或协议错误，请核对最终请求地址。"
    elif status in {400, 422}:
        code, message = "protocol_error", "模型服务拒绝请求格式，请检查协议和模型名称。"
    elif isinstance(error, (ValueError, KeyError, TypeError, IndexError, AttributeError)) or name in {"ValidationError", "ProviderResponseError", "ProviderRefusalError"}:
        code, message = "output_parse_error", "模型输出不符合结构或允许的行为约束。"
    else:
        code, message = "upstream_error", "模型服务返回异常，已保留本地生活。"
    if detail:
        if secret:
            detail = detail.replace(secret, "[redacted]")
        detail = re.sub(r"(?i)(bearer\s+|(?:api[\s_-]?key|token|authorization)\s*[:=]\s*)[^\s,;]+", r"\1[redacted]", detail)
        detail = re.sub(r"https?://[^\s]+", "[endpoint]", detail)
        message += " " + detail[:160]
    return UpstreamError(code, message[:300])


@dataclass
class AdapterReply:
    text: str
    request_id: str = ""


class ProtocolAdapter:
    def __init__(self, *, endpoint: str, model: str, api_key: str, timeout: float, output_mode: str = "text", client: httpx.Client | None = None) -> None:
        self.endpoint, self.model = endpoint, model
        self._key, self.timeout = api_key, timeout
        self.output_mode = output_mode
        self._owns_client = client is None
        self._client = client or httpx.Client(timeout=timeout, follow_redirects=False)
        self._disabled_options: set[str] = set()

    def __del__(self):
        # An obsolete provider remains referenced by its in-flight calls; release its
        # connection pool only after those calls no longer own it.
        if getattr(self, "_owns_client", False):
            self._client.close()

    def headers(self) -> dict[str, str]:
        headers = {"Content-Type": "application/json"}
        if self._key:
            headers["Authorization"] = "Bearer " + self._key
        return headers

    def payload(self, system: str, user: str, schema: dict | None, name: str, max_tokens: int) -> dict:
        raise NotImplementedError

    def extract(self, data: dict) -> str:
        raise NotImplementedError

    def optional_parameter(self) -> str:
        return ""

    def complete(self, system: str, user: str, schema: dict | None = None, name: str = "output", max_tokens: int = 2048) -> AdapterReply:
        body = self.payload(system, user, schema, name, max_tokens)
        option = self.optional_parameter()
        if option in self._disabled_options:
            body.pop(option, None)
        for attempt in range(2):
            try:
                response = self._client.post(self.endpoint, headers=self.headers(), json=body, timeout=self.timeout, follow_redirects=False)
                # Never follow redirects: that could disclose credentials to another host.
                if response.is_redirect:
                    raise UpstreamError("endpoint_error", "模型端点返回重定向，请直接填写最终服务地址。")
                if response.status_code in {400, 422} and attempt == 0 and option in body:
                    message = response.text.lower()
                    if option.lower() in message and any(word in message for word in ("unsupported", "not supported", "unknown", "unrecognized", "not allowed")):
                        self._disabled_options.add(option)
                        body.pop(option, None)
                        continue
                response.raise_for_status()
                data = response.json()
                text = self.extract(data)
                if not text.strip():
                    raise ValueError("上游未返回正文。")
                return AdapterReply(text, str(data.get("id", data.get("responseId", ""))))
            except (httpx.HTTPError, ValueError, KeyError, TypeError, IndexError, AttributeError, UpstreamError) as error:
                raise diagnose(error, self._key) from None
        raise UpstreamError("protocol_error", "模型服务不支持所请求的输出格式。")


class ChatCompletionsAdapter(ProtocolAdapter):
    def optional_parameter(self) -> str:
        return "response_format"

    def payload(self, system: str, user: str, schema: dict | None, name: str, max_tokens: int) -> dict:
        result = {"model": self.model, "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}]}
        if schema and self.output_mode == "json":
            result["response_format"] = {"type": "json_object"}
        elif schema and self.output_mode == "schema":
            result["response_format"] = {"type": "json_schema", "json_schema": {"name": name, "schema": schema}}
        return result

    def extract(self, data: dict) -> str:
        message = data["choices"][0]["message"]
        if message.get("refusal"):
            raise ValueError("模型拒绝生成内容。")
        return message["content"]


class ResponsesAdapter(ProtocolAdapter):
    def optional_parameter(self) -> str:
        return "text"

    def payload(self, system: str, user: str, schema: dict | None, name: str, max_tokens: int) -> dict:
        result = {"model": self.model, "instructions": system, "input": user, "max_output_tokens": max_tokens}
        if schema and self.output_mode == "json":
            result["text"] = {"format": {"type": "json_object"}}
        elif schema and self.output_mode == "schema":
            result["text"] = {"format": {"type": "json_schema", "name": name, "schema": schema}}
        return result

    def extract(self, data: dict) -> str:
        if data.get("status") in {"failed", "incomplete"}:
            raise ValueError("模型回复未完成。")
        return data.get("output_text") or "".join(part.get("text", "") for item in data.get("output", []) for part in item.get("content", []) if part.get("type") == "output_text")


class AnthropicAdapter(ProtocolAdapter):
    def headers(self) -> dict[str, str]:
        headers = {"Content-Type": "application/json", "anthropic-version": "2023-06-01"}
        if self._key:
            headers["x-api-key"] = self._key
        return headers

    def payload(self, system: str, user: str, schema: dict | None, name: str, max_tokens: int) -> dict:
        return {"model": self.model, "system": system, "messages": [{"role": "user", "content": user}], "max_tokens": max_tokens}

    def extract(self, data: dict) -> str:
        if data.get("stop_reason") == "max_tokens":
            raise ValueError("模型回复被截断。")
        return "".join(part.get("text", "") for part in data["content"] if part.get("type") == "text")


class GeminiAdapter(ProtocolAdapter):
    def headers(self) -> dict[str, str]:
        headers = {"Content-Type": "application/json"}
        if self._key:
            headers["x-goog-api-key"] = self._key
        return headers

    def optional_parameter(self) -> str:
        return "generationConfig"

    def payload(self, system: str, user: str, schema: dict | None, name: str, max_tokens: int) -> dict:
        result = {"systemInstruction": {"parts": [{"text": system}]}, "contents": [{"role": "user", "parts": [{"text": user}]}]}
        if schema and self.output_mode in {"json", "schema"}:
            result["generationConfig"] = {"responseMimeType": "application/json"}
        return result

    def extract(self, data: dict) -> str:
        return "".join(part.get("text", "") for part in data["candidates"][0]["content"]["parts"])


ADAPTERS = {"chat_completions": ChatCompletionsAdapter, "responses": ResponsesAdapter, "anthropic": AnthropicAdapter, "gemini": GeminiAdapter}
