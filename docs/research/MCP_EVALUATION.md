# MCP Server Evaluation — LakskongenSim

**Date:** 2026-10-12 · **Harness:** OpenCode V2 · **Scope:** candidate MCP servers for this repo, plus a verified V2 config shape, a Godot bridge fix snippet, and standalone (no-config) tests of the top two candidates. No config/key/signup/purchase was used or modified; scratch files live under `$TMPDIR/opencode/mcp-eval/`.

---

## Executive summary

1. **Context7** is the highest-value, lowest-friction addition: MIT, free tier, verified working both as stdio (`npx @upstash/context7-mcp`, v4.3.0 handshake below) and documented by OpenCode itself as a remote server.
2. **GitHub MCP Server** (official) is the strongest repo-ops candidate: MIT, free with a GitHub account, and its local binary (v2.0.2, 2026-10-08) passed a full stdio handshake on this machine without a token.
3. OpenCode V2 config shape **verified from the official docs**: servers live under `mcp.servers`; local servers support `environment`, `cwd`, `disabled`; remote servers support `url`/`headers`/`oauth`; `{env:NAME}` substitution is the documented way to inject secrets.
4. The Godot bridge project-path fix is a one-field change: set `GODOT_MCP_PROJECT_PATH=/Users/fredrik.j.hansen/lakskongen/godot` in the `godot` server's `environment` (snippet in §1); the local `tools/mcp_bridge_alias.py` workaround then becomes redundant.
5. **fal.ai** is a hosted, usage-priced media-generation service (image/video/audio/3D); no standing free tier on its pricing page and it needs an account + OAuth. Useful for Steam/marketing art later — **defer**.
6. **ElevenLabs-class audio**: the official local MCP is MIT on PyPI but its GitHub repo was **archived 2026-08-20**; the new hosted MCP is agent-management (Claude directory). Free tier is 10k credits/mo; needs an API key — **verify later** if VO/SFX work starts.
7. **Screenshot captioning**: community `ai-vision-mcp` (MIT, npx) covers captioning, comparison, object detection and a design-audit tool — a good fit for screenshot QA, but needs a Gemini API key (free tier exists) and is a small community project — **verify/pilot**.
8. **Figma MCP** remote server works on all plans/seat types (desktop server needs a paid Dev/Full seat); the repo already links Figma design artifacts (e.g. `design/FIGMA_LINKS.md`), so it is worth **verifying** when design work resumes.
9. **SQLite MCP**: the reference server was archived 2025-05-29 and this project persists via JSON (no SQLite store) — **defer**. **Steamworks community servers**: credentials + Steam Guard 2FA on publishing is a human gate — **document only**.
10. **Top 2 to adopt next: Context7, then GitHub MCP** (remote OAuth, local binary as fallback) — plus the Godot env fix as a separate, owner-approved config edit.

---

## 1. OpenCode V2 MCP config shape (verified) + Godot bridge fix

Verified against the official V2 docs fetched 2026-10-12 ([opencode.ai/v2/docs/mcp-servers](https://opencode.ai/v2/docs/mcp-servers)):

- Servers live under **`mcp.servers.<name>`**; *"V2 does not place server names directly under `mcp`."*
- **Local** server fields: `type: "local"`, `command: [...]`, `cwd`, `environment` (*"String variables added to OpenCode's inherited process environment"*), `disabled` (*"Use `disabled`, not an `enabled` field"*), `codemode`, `timeout`, `protocol`.
- **Remote** server fields: `type: "remote"`, `url`, `headers`, `oauth`, `disabled`.
- Secrets are injected with `{env:NAME}` (shell `$NAME` is **not** expanded in JSON strings).
- Observation: this repo's current `opencode.json` uses the flat, pre-V2 layout (`mcp.godot`, `"enabled": true`); it was **not modified** (off-limits to this task) — flagged for the owner.

**Exact Godot fix snippet** (wrap the bridge so it resolves the Godot project dir, not the repo root — the bridge resolves its registry key as `GODOT_MCP_PROJECT_PATH ?? process.cwd()`, see `tools/mcp_bridge_alias.py`):

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "servers": {
      "godot": {
        "type": "local",
        "command": ["npx", "-y", "@npgamedev/godot-mcp-server"],
        "environment": {
          "GODOT_MCP_PROJECT_PATH": "/Users/fredrik.j.hansen/lakskongen/godot"
        }
      }
    }
  }
}
```

One-line alternative if an entry must keep the old flat shape: `"command": ["env", "GODOT_MCP_PROJECT_PATH=/Users/fredrik.j.hansen/lakskongen/godot", "npx", "-y", "@npgamedev/godot-mcp-server"]`. **Confidence: High** (V2 docs + local bridge script). Package check: `@npgamedev/godot-mcp-server` v1.0.3, MIT (npm registry, 2026-10-12).

---

## 2. Candidate sections

### 2.1 Context7 — live library docs → **adopt**
- **What it does:** resolves a library to a Context7 ID (`resolve-library-id`) and returns current docs/code snippets (`query-docs`); keeps agents grounded in Godot 4.7 / .NET 10 APIs instead of stale training data.
- **Install (V2 remote, no key):** `"context7": { "type": "remote", "url": "https://mcp.context7.com/mcp" }`. With an optional free key, the docs' own example adds `"oauth": false` and `"headers": { "CONTEXT7_API_KEY": "{env:CONTEXT7_API_KEY}" }`. Local fallback: `"command": ["npx", "-y", "@upstash/context7-mcp"]` (production-tested below).
- **License:** MIT ([upstash/context7](https://github.com/upstash/context7)).
- **Cost:** usable without a key at basic rate limits; free account = 1,000 API calls/month; Pro $10/seat/mo (2,000 calls/seat, then $5/1k) ([context7.com/plans](https://context7.com/plans), fetched 2026-10-12).
- **Suitability:** high — Godot/C# docs churn fast; this directly supports the "verify library API" gap.
- **Verdict: ADOPT.** **Confidence: High** (handshake test + official docs + pricing page).

### 2.2 GitHub MCP Server (official) — repo/CI/PR ops → **adopt**
- **What it does:** official GitHub server with toolsets for repos, issues, PRs, actions, code security, dependabot, secret protection, gists, notifications, etc.; `--read-only` and lockdown modes exist for safety ([github/github-mcp-server](https://github.com/github/github-mcp-server)).
- **Install (V2 remote, recommended):** `"github": { "type": "remote", "url": "https://api.githubcopilot.com/mcp/" }` then authorize via `/mcps` (OAuth). Local fallback: `"command": ["/path/to/github-mcp-server", "stdio"]` with `"environment": { "GITHUB_PERSONAL_ACCESS_TOKEN": "{env:GITHUB_PERSONAL_ACCESS_TOKEN}" }`.
- **License:** MIT (repo metadata). **Cost:** free with a GitHub account; remote server in public preview ([GitHub docs](https://docs.github.com/en/copilot/how-tos/copilot-in-your-ide/customize-copilot/extend-copilot-with-tools-and-context/set-up-the-github-mcp-server), [GitHub blog guide](https://github.blog/ai-and-ml/generative-ai/a-practical-guide-on-how-to-use-the-github-mcp-server/)).
- **Suitability:** high — CI triage, PR review, release management; complements the existing `gh`-CLI-based `github-ops` skill. Note the deprecated `@modelcontextprotocol/server-github` npm package (2025.4.8, deprecated) should be avoided in favor of the official server.
- **Verdict: ADOPT (verify OAuth flow).** **Confidence: High** (binary + handshake tested; free/MIT from official repo/docs).

### 2.3 fal.ai — media generation (+ vision models) → **verify / defer**
- **What it does:** hosted sandbox for 1,000+ generative media models — image, video, audio (incl. ElevenLabs TTS/SFX via fal), 3D; plus a docs-only search server. Could produce Steam capsule art, trailers, and (via vision models) screenshot captioning.
- **Install (V2 remote, OAuth):** `"fal": { "type": "remote", "url": "https://mcp.fal.ai/mcp-relay" }` — *"Connect with browser sign-in using `https://mcp.fal.ai/mcp-relay`"* for generation; the keyless `https://fal.ai/docs/mcp` only searches public docs ([fal MCP docs](https://fal.ai/docs/documentation/serverless/mcp), fetched 2026-10-12). An older key-based endpoint appears in fal's 2025 announcement ([fal blog](https://blog.fal.ai/connect-your-ai-to-1-000-models-with-the-fal-mcp-server/)).
- **License:** hosted service (client repos on [github.com/fal-ai](https://github.com/fal-ai)); community npm wrapper `fal-ai-mcp-server` v2.1.4 MIT ([raveenb/fal-mcp-server](https://github.com/raveenb/fal-mcp-server)) exists as an alternative.
- **Cost:** usage-based per model — e.g. Nano Banana image $0.0398–$0.15, Kling v3 video $0.14/s, ElevenLabs SFX $0.002/s ([fal.ai/pricing](https://fal.ai/pricing), fetched 2026-10-12); **no standing free tier listed**, but a grants program is linked ([fal.ai/grants](https://fal.ai/grants)).
- **Suitability:** medium — valuable for marketing/Steam assets and optional vision work, but off the critical path and requires human signup + spend.
- **Verdict: DEFER (revisit at Steam-store phase).** **[no-test: needs account/OAuth + paid usage].** **Confidence: Medium-High** (official docs + pricing fetched; no runtime test).

### 2.4 ElevenLabs-class audio-gen MCP → **verify (human key gate) / defer**
- **What it does:** TTS/SFX/music/voice tools from the official ElevenLabs local MCP server; the new hosted MCP (OAuth) manages ElevenAgents and ships via Claude's connectors directory ([blog, Aug 17 2026, updated Sep 6 2026](https://elevenlabs.io/blog/elevenlabs-mcp-in-claude)).
- **Install (V2 local):** `"elevenlabs": { "type": "local", "command": ["uvx", "elevenlabs-mcp"], "environment": { "ELEVENLABS_API_KEY": "{env:ELEVENLABS_API_KEY}" } }`.
- **License:** MIT (PyPI 0.12.2 metadata). **Caveat:** the GitHub repo [elevenlabs/elevenlabs-mcp](https://github.com/elevenlabs/elevenlabs-mcp) is **archived** (last push 2026-08-20 per GitHub API); PyPI package still installs, and the blog says the local server "remains available for developer workflows."
- **Cost:** Free plan 10k credits/mo (no commercial license); Starter $6/mo (30k, commercial license) — current promos $1 first month; Creator $22/mo ([elevenlabs.io/pricing](https://elevenlabs.io/pricing), fetched 2026-10-12).
- **Suitability:** medium — VO/SFX could elevate a demo/trailer but is not needed by the sim; commercial use requires a paid plan.
- **Verdict: DEFER until VO/SFX or trailer work is scheduled; then verify the archived-package path or the hosted MCP.** **[no-test: needs key].** **Confidence: Medium-High** (pricing + blog + registry verified).

### 2.5 Screenshot-vision/captioner — `ai-vision-mcp` → **verify (pilot)**
- **What it does:** npx-installable MCP exposing `analyze_image` (modes: general, palette, hierarchy, components), `compare_images` (2–4 images), `detect_objects_in_image`, and `audit_design` (pixel analysis + WCAG contrast validation + Gemini critique) ([tan-yong-sheng/ai-vision-mcp](https://github.com/tan-yong-sheng/ai-vision-mcp)).
- **Install (V2 local):** `"ai-vision": { "type": "local", "command": ["npx", "-y", "ai-vision-mcp"], "environment": { "IMAGE_PROVIDER": "google", "VIDEO_PROVIDER": "google", "GEMINI_API_KEY": "{env:GEMINI_API_KEY}" } }` (Vertex AI alternative documented in the README).
- **License:** MIT (repo; npm `ai-vision-mcp` v0.0.7). **Cost:** none for the server; underlying Gemini API has a Free tier (rate-limited) and paid tiers ([Gemini rate limits](https://ai.google.dev/gemini-api/docs/rate-limits), updated 2026-10-09).
- **Suitability:** medium-high for this repo's screenshot-evidence workflow — captioning/diffing frames from `tools/capture_screenshots.sh` and the `visual-qa`/`lakskongen-screenshot-evidence` skills, with machine-readable evidence for RUN_LOG. Trade-offs: small community project (80 stars, last push 2026-04-06) and screenshots are sent to Google's API.
- **Verdict: VERIFY via a short pilot (one screenshot, free Gemini key), then adopt if captions are useful.** **[no-test: needs key].** **Confidence: Medium** (README-level verification only).

### 2.6 Figma MCP → **verify (when design work resumes); human OAuth gate**
- **What it does:** design context from selected frames, code generation hints, Code Connect mappings, FigJam/Make retrieval; write-to-canvas for agents (remote only, free during beta).
- **Install (V2 remote):** `"figma": { "type": "remote", "url": "https://mcp.figma.com/mcp" }` — remote is the preferred endpoint ([Figma help](https://help.figma.com/hc/en-us/articles/32132100833559-Guide-to-the-Figma-MCP-server), [figma/mcp-server-guide](https://github.com/figma/mcp-server-guide)).
- **License:** hosted service (guide repo has no license). **Cost:** remote server on **all seats and plans**; the desktop server (localhost, via the Figma desktop app, docs: [local installation](https://developers.figma.com/docs/figma-mcp-server/local-server-installation/)) requires a **Dev or Full seat on paid plans**. Some remote features are "free during the beta period."
- **Suitability:** medium — the repo references Figma in `design/FIGMA_LINKS.md`, `design/ART_DIRECTION.md`, `design/handoff.md` and the `design-token-sync` skill; useful the next time UI/theme work happens (Post-4.5).
- **Verdict: VERIFY (consume-only design context is free-plan-eligible; OAuth sign-in is a human step).** **Confidence: High** (official help center fetched).

### 2.7 SQLite MCP → **defer**
- **What it does:** query/inspect a SQLite database (list tables, read/write SQL). The official reference server was **archived 2025-05-29** ([servers-archived](https://github.com/modelcontextprotocol/servers-archived/tree/main/src/sqlite), last push 2025-05-28 per GitHub API; [modelcontextprotocol/servers](https://github.com/modelcontextprotocol/servers)).
- **Install (V2 local, community alternative):** `"sqlite": { "type": "local", "command": ["npx", "-y", "mcp-server-sqlite-npx", "/absolute/path/to/database.db"] }` ([johnnyoshika/mcp-server-sqlite-npx](https://github.com/johnnyoshika/mcp-server-sqlite-npx); npm v0.8.0, ISC; repo MIT).
- **License:** MIT/ISC depending on fork. **Cost:** free.
- **Suitability:** none evident now — the simulation persists via JSON (`SaveJson`/`GameState`); no SQLite store is part of the stack, and only docs mention SQLite incidentally.
- **Verdict: DEFER / reject-until-needed** (revisit if editor tooling or telemetry ever adds a database). **Confidence: High** on archival; Medium on alternatives.

### 2.8 Steamworks community servers → **document only (human gate)**
- **What it does:** community servers for Steam Web API data (profile/library/stats/store/Workshop) and, in some forks, Steamworks publishing assistance. Concrete options: [sandraschi/steam-mcp](https://github.com/sandraschi/steam-mcp) (MIT, v0.3.2, FastMCP, needs `STEAM_API_KEY`; optional SteamCMD for publishing; 3 stars) and [dsp/mcp-server-steam](https://github.com/dsp/mcp-server-steam) (MIT, 25 stars, last push 2025-02-18).
- **Install (V2 local, adapted from the project README — verify before use):** `"steam": { "type": "local", "command": ["uv", "--directory", "/path/to/steam-mcp", "run", "steam-mcp"], "environment": { "STEAM_API_KEY": "{env:STEAM_API_KEY}", "STEAM_ID": "{env:STEAM_ID}" } }`.
- **License:** MIT (both). **Cost:** free (Steam Web API key is free; Steam Direct fee is a store cost, separate).
- **Suitability:** low now — store data could inform marketing, but publishing flows require Steam **credentials + Steam Guard 2FA**, a human-controlled gate; community servers are low-maturity.
- **Verdict: REJECT for agent automation; DOCUMENT ONLY.** **[no-test: needs Steam credentials/2FA].** **Confidence: Medium** (READMEs verified; not installed).

---

## 3. Cost table

| MCP | What it does | Free tier | Paid from | License | Verdict |
|---|---|---|---|---|---|
| Context7 | Live library docs for agents | Yes: keyless basic limits; 1,000 calls/mo with free account | $10/seat/mo (2,000 calls, $5/1k after) | MIT | **Adopt** |
| GitHub MCP (official) | Repos, issues, PRs, Actions, security toolsets | Yes: free with GitHub account | — (GitHub plan irrelevant) | MIT | **Adopt (verify OAuth)** |
| fal.ai | Image/video/audio/3D generation; docs search server | No standing tier (grants program) | Usage-based, e.g. $0.0398/image, $0.14/s video | Hosted; community wrapper MIT | **Defer** |
| ElevenLabs (audio) | TTS/SFX/music/voice; hosted agent MCP | 10k credits/mo (no commercial use) | $6/mo Starter; $22 Creator | MIT (archived repo) | **Verify later** |
| ai-vision-mcp | Image captioning, comparison, detection, design audit | Server free; Gemini API free tier | Gemini paid tiers | MIT | **Verify (pilot)** |
| Figma MCP | Design context/code, Code Connect, write-to-canvas | Remote on all plans (free plan OK) | Desktop server needs paid Dev/Full seat | Hosted service | **Verify** |
| SQLite MCP | Inspect/query SQLite DBs | Yes | — | MIT/ISC (community) | **Defer** |
| Steam (community) | Steam Web API data; publishing helpers | Free API key | Steam Direct fee is separate | MIT | **Document only** |

---

## 4. Production-test log (no config touched)

**Environment:** macOS arm64, Node v25.9.0, npx 11.12.1, `uv`/`uvx` installed, **Docker daemon NOT running** (`failed to connect to the docker API ... docker.sock: no such file or directory`), so Docker was replaced by the official binary. Scratch dir: `/private/var/.../T/opencode/mcp-eval/` (handshake script + binary only).

### Test 1 — Context7 local stdio (PASS)
Command: `node handshake.js npx -y @upstash/context7-mcp` (script sends `initialize` → `notifications/initialized` → `tools/list`, then kills the process). Raw output (trimmed with […]):

```
[STDERR] Context7 Documentation MCP Server v4.3.0 running on stdio
{"result":{"protocolVersion":"2024-11-05","capabilities":{...},"serverInfo":{"name":"Context7","version":"4.3.0","websiteUrl":"https://context7.com","description":"Context7 provides up-to-date documentation and code examples for libraries and frameworks."},"instructions":"Use this server to fetch current documentation whenever the user asks about a library, framework, SDK, API, CLI tool, or cloud service…"},"jsonrpc":"2.0","id":1}
{"result":{"tools":[{"name":"resolve-library-id",…},{"name":"query-docs",…}]},"jsonrpc":"2.0","id":2}
```

Result: initialize + tool catalog returned, no API key required. Package v4.3.0 = npm latest (MIT).

### Test 2 — GitHub MCP Server binary stdio (PASS)
Commands (raw):
`curl -sL -o gh.tar.gz "https://github.com/github/github-mcp-server/releases/download/v2.0.2/github-mcp-server_Darwin_arm64.tar.gz" && tar xzf gh.tar.gz && ./github-mcp-server --help`

```
A GitHub MCP server that handles various tools and resources.
Usage: github-mcp-server [command]
Available Commands: completion, generate-docs, help, http, list-scopes, stdio
Flags: ... --read-only, --lockdown-mode, --toolsets strings (actions, code_quality, code_security, dependabot, issues, labels, notifications, orgs, projects, pull_requests, repos, secret_protection, …)
```

Then the no-token handshake: `node handshake.js ./github-mcp-server stdio`, raw output (trimmed):

```
[STDERR] msg="starting server" version=2.0.2 host="" readOnly=false lockdownEnabled=false
[STDERR] GitHub MCP Server running on stdio
{"jsonrpc":"2.0","id":1,"result":{"capabilities":{...},"serverInfo":{"name":"github-mcp-server","version":"2.0.2"},"protocolVersion":"2024-11-05","instructions":"The GitHub MCP Server provides tools to interact with GitHub platform.…"}}
[STDERR] msg="session initialized" session_id=""
{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"add_comment_to_pending_review",…]}}
```

Result: server initializes and advertises tools without a token; tool **calls** would still require auth (PAT or OAuth) — not exercised.

### Untested candidates (with reason)
- **fal.ai** — **[no-test: needs account/OAuth sign-in + paid usage]** (hosted endpoint; no keyless generation path).
- **ElevenLabs** — **[no-test: needs key]** (`ELEVENLABS_API_KEY`; package not run to avoid an unauthenticated hang/download).
- **ai-vision-mcp** — **[no-test: needs key]** (`GEMINI_API_KEY`).
- **Figma MCP** — **[no-test: needs OAuth sign-in]** (remote requires account authorization; desktop server needs paid seat + app).
- **SQLite MCP** — not a top-2 pick; deferred (keyless but no use case).
- **Steam** — **[no-test: needs Steam credentials]**/2FA human gate; document only.

---

## 5. Final recommendation

1. **Adopt Context7 now.** Paste the remote snippet from §2.1 (no key required; add a free key only if rate limits bite). It shipped verified in both transports and directly targets the Godot 4.7/C# knowledge gap.
2. **Adopt GitHub MCP next**, preferring the remote server (`https://api.githubcopilot.com/mcp/`) so auth is OAuth via `/mcps`; keep the verified v2.0.2 local binary (tested above) as a fallback with `GITHUB_PERSONAL_ACCESS_TOKEN`, optionally `--read-only` to start.
3. **Separate owner action:** apply the Godot `environment: GODOT_MCP_PROJECT_PATH` fix in §1 to `opencode.json` (V2 shape) and restart the service; until then keep re-running `tools/mcp_bridge_alias.py` after CI/editor restarts.

Deferred watchlist: fal.ai (Steam/marketing phase), ElevenLabs (VO/SFX phase), ai-vision-mcp (screenshot-QA pilot), Figma (next UI cycle). Sources are linked inline; pages fetched 2026-10-12 unless dated. Nothing outside this file was modified.
