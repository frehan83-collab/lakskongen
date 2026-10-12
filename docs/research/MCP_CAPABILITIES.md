# MCP_CAPABILITIES.md — MCP inventory & repair log (run W0.1)

*Session: run session 0 · Date: 2026-10-12 · Owner: autonomous agent*
*Scope: verify every configured MCP server in this environment, map each to a run
task, record raw evidence, and document the godot-bridge repair.*

---

## 1. Summary

| MCP | Transport | Status (2026-10-12) | Owned tasks in this run |
|-----|-----------|---------------------|-------------------------|
| `godot` (godot_mcp_toolkit v1.0.3 + `@npgamedev/godot-mcp-server`) | local stdio bridge → editor WS `127.0.0.1:6550` | **working** after repair (§3) | S1 living fjord, S2 time-series UI, scene/layout work, playtest control, runtime screenshots |
| `blender` | local (`uv` server, Blender add-on) | **connected** (Blender running, scene reachable) | S7 assets, W6 visual track (via `blender-asset-pipeline`) |
| `firecrawl` (hosted, keyless session) | hosted HTTP via harness | **connected** | R1–R5 research, fact verification, S6/D8 store copy checks |
| `opencode` (internal) | harness-native | available | session/worktree management, model info |
| `claude-devfleet` | — | **not connected** (no MCP tools registered; see §6) | n/a — parallel research uses harness `subagent` instead |

Raw connectivity evidence for blender + firecrawl (session 0):

```jsonc
// tools.blender.get_objects_summary() — fulfilled
{"status":"ok","result":{"status":"ok","scene_name":"Scene","active_workspace":"Layout",
 "active_object":"Cube","object_mode":"OBJECT","camera_object":"Camera", ...}}

// tools.firecrawl.firecrawl_search({query:"Godot 4.7 release notes 2026", limit:2}) — fulfilled
{"success":true, "data":[{"url":"https://godotengine.org/releases/4.7/"}, ...]}
```

---

## 2. godot MCP — tool inventory (the big new lever)

36 tools exposed to the agent (plus on-demand groups via `discover_tools`:
runtime_advanced, signals, animation_authoring, tilemap, 3d_tools, procedural,
audio, navigation, lsp_code_analysis, …). Grouped:

| Group | Tools |
|-------|-------|
| Scene graph (editor) | `scene_open`, `scene_create`, `scene_get_tree`, `scene_query`, `scene_create_node`, `scene_delete_node`, `scene_spatial_map`, `editor_save_scene` |
| Node/property (editor) | `node_manage`, `node_get_property`, `node_set_property`, `node_get_property_list`, `node_call_method`, `node_set_script`, `node_groups` |
| Signals | `signal_list`, `signal_manage` |
| Scripts | `script_read`, `script_write`, `script_edit`, `script_check` (offline validation) |
| Project/config | `project_get_settings`, `project_set_setting`, `autoload_manage`, `folder_create`, `extensions_refresh` |
| Runtime (game) | `game_start`, `game_stop`, `input_simulate`, `runtime_get_script_vars`, `runtime_screenshot`, `debugger_get_log` |
| Editor introspection | `editor_get_console`, `discover_tools`, `control_set_layout` |

**Verified working** (session 0, raw results):

```jsonc
// tools.godot.editor_get_console({limit:8}) — fulfilled
{"_summary":"8 lines (of 9 total)","entries":"[{\"level\":\"info\",
 \"message\":\"[MCPServer] listening on 127.0.0.1:6550\", ...},
 {\"message\":\"[MCPRegistry] registered /users/fredrik.j.hansen/lakskongen/godot on port 6550\" ...}]"}

// tools.godot.scene_get_tree({max_depth:1}) — fulfilled
{"success":true,"tree":"{\"class\":\"PanelContainer\",\"name\":\"AIFeedingPanel\", ...}"}
```

---

## 3. godot MCP repair — root cause & fix (read before scene work)

### Symptom
Every native godot tool call failed:
`AUTH_FAILED: no registry entry for /Users/fredrik.j.hansen/lakskongen — is the
Godot editor running with the MCP toolkit plugin? Reconnect or relaunch.`

### Root cause (two independent defects)
1. **Registry emptied + token clobbered.** The toolkit keeps a machine-wide
   registry (`~/Library/Application Support/godot-mcp-toolkit/projects.json`,
   projections of `entries/*.json`) and a per-project session token
   (`…/app_userdata/Lakskongen/addons/godot_mcp_toolkit/project_instance_4a6b05a65177/mcp_token`).
   The CI headless scene-validation instance (every `./tools/ci.sh` run) boots its
   own MCP runtime, rewrites the shared token file, and its cleanup pass emptied
   `entries/` (dir mtime 19:42 = CI run), leaving the long-running editor from
   Friday with an in-memory token that no longer matched the on-disk file
   (`WS close 1008 'invalid token'` for every auth shape — probe recorded in §5).
2. **Registry key mismatch.** The npm bridge resolves its registry key as
   `GODOT_MCP_PROJECT_PATH ?? process.cwd()`; opencode spawns it with cwd = **repo
   root** (`…/lakskongen`), while the editor registers under the **Godot project**
   (`…/lakskongen/godot`). Even a healthy registry would not match.

### Fix applied (session 0, verified)
1. Restarted the Godot editor (`--path …/lakskongen/godot --editor`). On boot it
   logs `[MCPServer] session token written …`, `[MCPServer] listening on
   127.0.0.1:6550`, `[MCPRegistry] registered …/godot on port 6550` (raw lines in
   §5). Token + registry entry regenerated.
2. Ran `python3 tools/mcp_bridge_alias.py` — clones the live editor entry under
   the repo-root key the bridge actually looks up:
   `OK: alias written (/users/fredrik.j.hansen/lakskongen -> port 6550.0, pid 11371.0, token …/mcp_token)`.
3. Re-called native tools → working (raw results §2/§5).

### Operating rules from this
- **After every `./tools/ci.sh` run** (headless instances rebuild the
  projection): run `python3 tools/mcp_bridge_alias.py` before the next godot MCP
  call. Exit code 0 + one `OK:` line confirms.
- If the editor is restarted, re-run the alias script (new pid/started_at).
- Never authenticate by hand to port 6550 — the bridge owns that path.

### Durable fix (deferred — needs a harness restart; do not do mid-session)
Give the bridge the right key at spawn time in `opencode.json` (exact V2 syntax
verified in `docs/research/MCP_EVALUATION.md`, Brief D) — wrap the command:

```jsonc
"godot": {
  "type": "local",
  "command": ["env", "GODOT_MCP_PROJECT_PATH=/Users/fredrik.j.hansen/lakskongen/godot",
              "npx", "-y", "@npgamedev/godot-mcp-server"],
  "enabled": true
}
```

…then the alias script becomes unnecessary. Applying it requires an opencode
service restart (a STOP-worthy risk mid-run; deferred to the next natural
harness restart — noted in `docs/RUN_LOG.md` decision log).

---

## 4. CI-side godot MCP steps (already in `tools/ci.sh`)

`step_godot_mcp_autoload_audit`, `step_godot_mcp_shader_check`,
`step_godot_mcp_export_verify` + the `godot-mcp-ops` skill commands (scene-diff)
— these run through the Python client/headless paths and passed 15/15 in the
session-0 CI run. They do not depend on the editor bridge.

---

## 5. Raw verification log (session 0)

```
$ lsof -nP -iTCP:6550 -sTCP:LISTEN
Godot   11371 fredrik.j.hansen  107u  IPv4 … TCP 127.0.0.1:6550 (LISTEN)

$ tail godot_editor_boot.log
Godot MCP Toolkit v1.0.3 — by NPGameDev · npgamedev.com
[MCPServer] port: scanning 6550-6560 (default)
[MCPServer] session token written to user://addons/godot_mcp_toolkit/project_instance_4a6b05a65177/mcp_token
[MCPServer] listening on 127.0.0.1:6550
[MCPRegistry] registered /users/fredrik.j.hansen/lakskongen/godot on port 6550

# pre-repair probe (all three shapes): WS close 1008 'invalid token'
# — because the on-disk token had been clobbered by a headless CI instance.

$ python3 tools/mcp_bridge_alias.py
OK: alias written (/users/fredrik.j.hansen/lakskongen -> port 6550.0, pid 11371.0, token …/project_instance_4a6b05a65177/mcp_token)

# post-repair native calls: fulfilled (see §2 raw results)
```

---

## 6. Notes on other servers

- **blender**: the running Blender instance holds a default scene (`Scene`, Cube).
  The project's asset `.blend` was NOT open. Before asset work in S7/W6, verify
  the right file is open (`get_blendfile_summary_path_info`) — do not assume.
- **firecrawl**: keyless hosted session with usage limits; tools used by
  research briefs: `firecrawl_search`, `firecrawl_scrape`, plus
  `firecrawl_developer_search` / `firecrawl_gov_search` / `firecrawl_research_*`
  categories when applicable. No API key configured → treat limits as real.
- **claude-devfleet**: surfaced by the owner as an option; not connected as an
  MCP server here (no `devfleet_*` tools registered). If wanted later:
  `claude mcp add devfleet --transport http http://localhost:18801/mcp` with a
  running DevFleet instance — then it can be evaluated like any MCP (Brief D
  covers the evaluation pattern). Parallel research currently uses the harness
  `subagent` tool per the run plan §3.2.

---

## 7. Task mapping (which MCP owns what)

- **S1 living fjord / S2 time-series UI / any scene work** → `godot` MCP (editor
  scene APIs + runtime_screenshot) *after* `tools/mcp_bridge_alias.py`.
- **Playtest control + input simulation** → `godot` runtime tools.
- **Asset QA + authoring** → `blender` MCP + `.claude/skills/ecc/blender-asset-pipeline`.
- **Research (R1–R5)** → `firecrawl` (Firecrawl-first per run plan §6).
- **Visual evidence** → `tools/capture_screenshots.sh` + `visual-qa` skill
  (see `lakskongen-screenshot-evidence` skill for the end-to-end recipe).
