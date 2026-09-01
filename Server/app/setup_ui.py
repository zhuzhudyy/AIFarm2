import json


def render_setup_page(csrf_token: str, script_nonce: str) -> str:
    page = """<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>AIFarm API 设置</title>
  <style>
    :root {
      color-scheme: dark;
      --bg: #08110e;
      --panel: #102019;
      --panel-strong: #14291f;
      --line: #2b4b3b;
      --text: #edf7ef;
      --muted: #9cb5a6;
      --accent: #75d492;
      --accent-strong: #3d9b61;
      --warn: #f3c567;
      --danger: #ef8e83;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      background:
        radial-gradient(circle at 20% 0%, #173526 0, transparent 36rem),
        linear-gradient(145deg, var(--bg), #07100d 60%, #0d1712);
      color: var(--text);
      font-family: Inter, "Microsoft YaHei UI", "PingFang SC", system-ui, sans-serif;
    }
    main { width: min(940px, calc(100% - 32px)); margin: 0 auto; padding: 48px 0 64px; }
    header { margin-bottom: 24px; }
    .eyebrow { color: var(--accent); font-weight: 800; letter-spacing: .16em; font-size: 12px; }
    h1 { margin: 8px 0 10px; font-size: clamp(32px, 6vw, 54px); line-height: 1.05; }
    header p { color: var(--muted); max-width: 720px; line-height: 1.7; }
    .grid { display: grid; grid-template-columns: 1.25fr .75fr; gap: 20px; }
    .card {
      background: color-mix(in srgb, var(--panel) 92%, transparent);
      border: 1px solid var(--line);
      border-radius: 18px;
      padding: 24px;
      box-shadow: 0 24px 70px rgba(0, 0, 0, .28);
    }
    h2 { margin: 0 0 20px; font-size: 20px; }
    label { display: block; margin: 16px 0 7px; color: #cce1d2; font-size: 14px; font-weight: 700; }
    input, select {
      width: 100%; border: 1px solid #365c48; background: #091510; color: var(--text);
      border-radius: 11px; padding: 13px 14px; font: inherit; outline: none;
    }
    input:focus, select:focus { border-color: var(--accent); box-shadow: 0 0 0 3px rgba(117,212,146,.13); }
    .hint { margin: 7px 0 0; color: var(--muted); font-size: 12px; line-height: 1.55; }
    .remember { display: flex; align-items: flex-start; gap: 10px; margin: 18px 0 0; color: #cce1d2; }
    .remember input { width: 18px; height: 18px; margin: 1px 0 0; accent-color: var(--accent-strong); }
    .remember span { font-size: 13px; line-height: 1.5; }
    .actions { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 22px; }
    button {
      border: 1px solid #3d6a50; background: #183628; color: var(--text); border-radius: 11px;
      padding: 11px 15px; font: inherit; font-weight: 800; cursor: pointer;
    }
    button.primary { background: var(--accent-strong); border-color: var(--accent); }
    button.danger { color: #ffd7d2; border-color: #70423d; background: #321c1a; }
    button:hover { filter: brightness(1.12); }
    button:disabled { cursor: wait; opacity: .55; }
    dl { margin: 0; display: grid; gap: 14px; }
    .status-row { border-bottom: 1px solid #223d30; padding-bottom: 13px; }
    dt { color: var(--muted); font-size: 12px; margin-bottom: 5px; }
    dd { margin: 0; font-weight: 800; overflow-wrap: anywhere; }
    .pill { display: inline-flex; align-items: center; gap: 7px; color: var(--accent); }
    .pill::before { content: ""; width: 8px; height: 8px; background: currentColor; border-radius: 50%; }
    .notice {
      margin-top: 18px; min-height: 48px; padding: 12px 14px; border-radius: 10px;
      color: var(--muted); background: #0a1711; border: 1px solid #254333; line-height: 1.55;
    }
    .notice.success { color: #bff3cd; border-color: #3e7e55; }
    .notice.error { color: #ffd0ca; border-color: #7d4942; }
    .security { margin-top: 20px; color: var(--muted); font-size: 13px; line-height: 1.7; }
    code { color: #b8efc8; background: #08110d; padding: 2px 6px; border-radius: 5px; }
    @media (max-width: 760px) { .grid { grid-template-columns: 1fr; } main { padding-top: 28px; } }
  </style>
</head>
<body>
  <main>
    <header>
      <div class="eyebrow">AIFARM · LOCAL GATEWAY</div>
      <h1>API 设置</h1>
      <p>为四名居民配置同一个模型入口。API Key 只提交给当前电脑上的 Python 网关；游戏客户端不会保存、打印或直接使用它。</p>
    </header>
    <div class="grid">
      <section class="card" aria-labelledby="settings-title">
        <h2 id="settings-title">模型配置</h2>
        <form id="settings-form">
          <label for="provider">运行模式</label>
          <select id="provider" name="provider">
            <option value="openai">DeepSeek（OpenAI 兼容）</option>
            <option value="mock">完全离线 Mock</option>
          </select>

          <label for="model">模型 ID</label>
          <input id="model" name="model" value="deepseek-v4-flash" maxlength="128" spellcheck="false">
          <p class="hint">当前网关固定连接 DeepSeek，不允许从 UI 改写服务端点。</p>

          <label for="api-key">API Key</label>
          <input id="api-key" name="api-key" type="password" maxlength="512"
                 autocomplete="new-password" spellcheck="false" placeholder="留空可沿用当前已配置密钥">
          <p class="hint">密钥不会回显。保存成功后输入框会立即清空。</p>

          <label class="remember">
            <input id="persist" type="checkbox" checked>
            <span>记住到当前系统用户的本地配置目录（仓库、Unity 存档与 PlayerPrefs 之外）</span>
          </label>

          <div class="actions">
            <button class="primary" type="submit">保存并启用</button>
            <button id="probe" type="button">测试连接</button>
            <button id="offline" type="button">切换离线</button>
            <button id="clear" class="danger" type="button">清除本地配置</button>
          </div>
        </form>
        <div id="notice" class="notice" role="status" aria-live="polite">正在读取网关状态…</div>
      </section>

      <aside class="card" aria-labelledby="status-title">
        <h2 id="status-title">当前状态</h2>
        <dl>
          <div class="status-row"><dt>Provider</dt><dd id="status-provider" class="pill">读取中</dd></div>
          <div class="status-row"><dt>共享模型</dt><dd id="status-model">—</dd></div>
          <div class="status-row"><dt>API Key</dt><dd id="status-key">—</dd></div>
          <div class="status-row"><dt>配置来源</dt><dd id="status-source">—</dd></div>
          <div><dt>本地持久化</dt><dd id="status-persisted">—</dd></div>
        </dl>
        <p class="security">页面不加载第三方脚本或字体，配置接口只接受回环地址和同源请求。远程调用失败时，四名居民仍使用确定性的本地回退。</p>
      </aside>
    </div>
  </main>
  <script nonce="__SCRIPT_NONCE__">
    "use strict";
    const csrfToken = __CSRF_TOKEN__;
    const form = document.getElementById("settings-form");
    const providerInput = document.getElementById("provider");
    const modelInput = document.getElementById("model");
    const keyInput = document.getElementById("api-key");
    const persistInput = document.getElementById("persist");
    const notice = document.getElementById("notice");
    const buttons = Array.from(document.querySelectorAll("button"));

    function setBusy(busy) { buttons.forEach(button => { button.disabled = busy; }); }
    function show(message, kind = "") { notice.textContent = message; notice.className = `notice ${kind}`.trim(); }
    function sourceLabel(source) {
      return ({ environment: "进程环境变量", local_config: "本地用户配置", runtime: "当前运行时", injected: "测试注入" })[source] || source;
    }
    function renderStatus(status) {
      document.getElementById("status-provider").textContent = status.provider === "openai" ? "DeepSeek" : "Offline Mock";
      document.getElementById("status-model").textContent = status.model || "—";
      document.getElementById("status-key").textContent = status.api_key_configured ? "已配置（已隐藏）" : "未配置";
      document.getElementById("status-source").textContent = sourceLabel(status.source);
      document.getElementById("status-persisted").textContent = status.persisted ? "是" : "否";
      providerInput.value = status.provider;
      if (status.model) modelInput.value = status.model;
    }
    async function request(path, options = {}) {
      const response = await fetch(path, { cache: "no-store", ...options });
      const payload = await response.json();
      if (!response.ok) {
        const message = payload.error?.message || payload.detail || "网关拒绝了请求。";
        throw new Error(message);
      }
      return payload;
    }
    async function refresh() {
      const status = await request("/v1/gateway-config");
      renderStatus(status);
      show("配置页已连接到本机网关。", "success");
    }
    async function configure(provider) {
      const body = {
        provider,
        model: modelInput.value.trim() || "deepseek-v4-flash",
        api_key: keyInput.value.trim() || null,
        persist: persistInput.checked
      };
      const status = await request("/v1/gateway-config", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-AIFarm-CSRF": csrfToken },
        body: JSON.stringify(body)
      });
      keyInput.value = "";
      renderStatus(status);
      return status;
    }
    form.addEventListener("submit", async event => {
      event.preventDefault(); setBusy(true);
      try { await configure(providerInput.value); show("配置已生效，四名居民共用这一 Provider 与模型。", "success"); }
      catch (error) { show(error.message, "error"); }
      finally { setBusy(false); }
    });
    document.getElementById("probe").addEventListener("click", async () => {
      setBusy(true); show("正在测试鉴权和模型可用性…");
      try {
        const result = await request("/v1/gateway-config/probe", {
          method: "POST", headers: { "X-AIFarm-CSRF": csrfToken }
        });
        show(result.message, result.ok ? "success" : "error");
      } catch (error) { show(error.message, "error"); }
      finally { setBusy(false); }
    });
    document.getElementById("offline").addEventListener("click", async () => {
      setBusy(true);
      try { await configure("mock"); show("已切换到完全离线 Mock。", "success"); }
      catch (error) { show(error.message, "error"); }
      finally { setBusy(false); }
    });
    document.getElementById("clear").addEventListener("click", async () => {
      if (!window.confirm("清除本地 API 配置并切换到离线 Mock？")) return;
      setBusy(true);
      try {
        const status = await request("/v1/gateway-config/clear", {
          method: "POST", headers: { "X-AIFarm-CSRF": csrfToken }
        });
        keyInput.value = ""; renderStatus(status); show("本地配置已清除。", "success");
      } catch (error) { show(error.message, "error"); }
      finally { setBusy(false); }
    });
    refresh().catch(error => show(error.message, "error"));
  </script>
</body>
</html>
"""
    return page.replace("__CSRF_TOKEN__", json.dumps(csrf_token)).replace(
        "__SCRIPT_NONCE__",
        script_nonce,
    )
