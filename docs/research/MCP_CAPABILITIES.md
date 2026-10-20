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

### Root cause (confirmed empirically, session 0)
1. **Headless-editor boots in CI wipe the live editor's state.** Two CI steps boot a
   headless *editor* (not the game): `step_godot_mcp_export_verify`
   (`godot --headless --export-release`) and `step_gdscript_scan`
   (`godot --headless --editor --quit-after 3`). The toolkit plugin generates a NEW
   session token (overwriting the shared `…/project_instance_<hash>/mcp_token`) and
   registers/deregisters the project's entry file (`entries/<hash>.json`), deleting
   it on exit — which also empties `projects.json`. Proven by controlled runs:
   scene validation (`validate_scenes.sh`, runtime boot) leaves token+registry
   intact; each editor-boot step changes the token hash and empties `entries/` +
   `by_path` on every run (raw before/after in §5). The live editor keeps an
   in-memory token that no longer matches disk → all WS auth fails
   (`close 1008 'invalid token'`, all auth shapes).
2. **Registry key mismatch.** The npm bridge resolves its registry key as
   `GODOT_MCP_PROJECT_PATH ?? process.cwd()`; opencode spawns it with cwd = **repo
   root** (`…/lakskongen`), while the editor registers under the **Godot project**
   (`…/lakskongen/godot`). Even a healthy registry would not match.

### Fix applied (session 0, verified)
1. **CI sandbox (both editor-boot steps)** — `tools/ci.sh` runs
   `step_godot_mcp_export_verify` and `step_gdscript_scan` with
   `GODOT_MCP_TOKEN_PATH` pointed at a throwaway file, so the live editor's token
   survives CI. Integration-tested: token SHA byte-identical across both steps and
   across a full green CI run afterwards.
2. **Self-sufficient repair tool** — `python3 tools/mcp_bridge_alias.py` derives
   the live editor facts itself (WS port + pid via lsof, newest token file, LSP
   6005), writes the bridge-facing alias under the repo-root key, and **verifies
   with a real WS auth handshake**. Exit codes: `0` repaired+authed; `2` token
   out of sync → restart the editor and re-run; `1` editor not found.
3. **Editor restart** (once, when token was already clobbered) — fresh token +
   registration, alias + probe green.

### Operating rules from this
- **After every CI run** (entry file churn): run `python3 tools/mcp_bridge_alias.py`
  — expect `OK: alias written …` + `OK: auth probe authed` (exit 0).
- If the probe fails (exit 2): restart the Godot editor, run the script again.
- The alias must be re-run after editor restarts too (new pid/started_at).
- Never authenticate by hand to port 6550 — the bridge owns that path; the script's
  probe is the only sanctioned direct handshake (read-only `auth`).

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
— these run with `--no-mcp --godot-bin "$GODOT_BIN"` and do not depend on the
editor bridge. The shader check is not headless: the dummy renderer parses no
shader code, so it checks uniform hints statically and compiles every shader
in a small window (`xvfb-run` on Linux); the old headless load passed broken
shaders (review F958).

---

## 5. Raw verification log (session 0)

```
# Root cause — controlled before/after (local times)
BEFORE: entries=4a6b05a65177.json token=c4dbcc175ba8a56f mtime=20:42:41
$ bash tools/validate_scenes.sh          # runtime boot (CI step 9 equivalent)
AFTER:  entries=4a6b05a65177.json token=c4dbcc175ba8a56f mtime=20:42:41   ← untouched

$ GODOT_BIN=/opt/homebrew/bin/godot-mono python3 …/godot_mcp_ops.py export-verify   # CI step 14
AFTER:  entries=(empty) token=5113118f67125046 mtime=20:43:23 projects.json keys: []   ← wipe confirmed
        (headless editor boot: new token + register/deregister of the same entry file)

# With BOTH editor-boot steps sandboxed (tools/ci.sh), against a live editor:
pre:  d64fef4584c25fbd @20:51:16
$ env GODOT_MCP_TOKEN_PATH=$S1 godot --headless --editor --quit-after 3     # step 10 shape
$ GODOT_MCP_TOKEN_PATH=$S2 … export-verify                                  # step 14 shape
post: d64fef4584c25fbd @20:51:16   ← token byte-identical; only entry churn remains
$ python3 tools/mcp_bridge_alias.py
OK: alias written (… -> port 6550, pid 14546 …)
OK: auth probe authed              [exit 0 — no editor restart needed]

# Repair — self-sufficient script
$ python3 tools/mcp_bridge_alias.py      # with stale token:
FAIL: auth probe failed (Close(code=1008 'invalid token')) … restart the editor and re-run   [exit 2]
$ # …editor restarted…
$ python3 tools/mcp_bridge_alias.py
OK: alias written (/users/fredrik.j.hansen/lakskongen -> port 6550, pid 13942, token …/mcp_token)
OK: auth probe authed                    [exit 0]

# Native tool path (earlier, same editor generation):
tools.godot.editor_get_console({limit:8}) → fulfilled (real console buffer)
tools.godot.scene_get_tree({max_depth:1}) → fulfilled (active scene: AIFeedingPanel.tscn)
```

---

## 6. Notes on other servers

- **blender**: the running Blender instance holds a default scene (`Scene`, Cube).
  The project's asset `.blend` was NOT open (verified:
  `get_blendfile_summary_path_info` → `is_saved:false`, empty `filepath`). Before
  asset work in S7/W6, open the right file explicitly — do not assume.
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
