# Lakskongen — Agent Team Protocol

## Team Structure

| Role | Agent | Model | Scope |
|------|-------|-------|-------|
| **Orchestrator** | Opus (main) | `anthropic/claude-opus-4-6` | Architecture, orchestration, creative design, critical decisions. Never builds. |
| **Builder-1** | Sonnet | `anthropic/claude-sonnet-4-5` | Multi-file features, UI wiring, debugging, general development |
| **Builder-2** | DeepSeek V3.2 | `openrouter/deepseek/deepseek-v3.2` | Math, algorithms, simulation logic, optimization (free credits) |
| **Builder-3** | GPT-5.4 | `openai/gpt-5.4` | Top-tier builder alongside Sonnet. Complex reasoning, coding, review, analysis |
| **Local Helper** | GLM Flash / Qwen3 | `ollama/glm-4.7-flash` / `ollama/qwen3:8b` | Trivial tasks ONLY: heartbeats, status, templates, syntax fixes |
| **Executive Producer** | Fredrik J. Hansen | Human | Strategic decisions, approval gates |

## Model Capabilities (Verified 2026-03-13)

### Sonnet (Builder-1) — Primary
- ✅ Multi-file features, cross-system integration
- ✅ New file creation from scratch (code + data)
- ✅ Complex logic, defensive coding
- ⏱️ ~1-3 min per task
- 💰 ~$0.40/run

### GPT-5.4 (Builder-3) — Top-Tier Workhorse
- ✅ Complex reasoning, coding, multi-step agent workflows
- ✅ 1M token context window — entire codebases, long trajectories
- ✅ Built-in computer use for build-run-verify-fix loops
- ✅ Native compaction support for longer agent trajectories
- ✅ Tool search with deferred loading (reduces token usage)
- ✅ Production-quality code, polished UI, multi-file changes
- ⏱️ ~1-3 min per task (similar to Sonnet)
- 💰 OpenAI credits — flagship frontier model

### GLM Flash (Local Helper) — Trivial Tasks Only
- ✅ Single-file edits with clear context
- ✅ Modifying existing code (well-defined task)
- ❌ Creating new files from scratch (fails consistently)
- ❌ Multi-system integration (needs cross-file context)
- ⏱️ ~5-7 min per task
- 💰 FREE

## Task Routing Rules (Strict Policy)

| Task Type | Route To |
|-----------|----------|
| Trivial / heartbeats / status / templates | Ollama (GLM Flash or Qwen3) |
| Math / algorithms / simulation / optimization | DeepSeek V3.2 |
| General dev / multi-file features / UI wiring | Sonnet OR GPT-5.4 (split work, both top-tier) |
| Architecture / creative / safety-critical | Opus (orchestrator) |

**Default:** DeepSeek or Ollama unless task clearly needs Claude.
**Always:** Break multi-step tasks into sub-tasks, route each individually.

## Fallback Chain

```
Sonnet fails → GPT-5.4 → DeepSeek → Sonnet
Ollama fails → Qwen3 → GPT-5.4
DeepSeek fails → Sonnet
Any timeout → Check git log before re-running
```

## Handoff Format

Every completed task:
1. What changed (files + summary)
2. Verified via `git diff --stat`
3. Committed with descriptive message
4. Next action identified

## Constraints
- ONE local model at a time (hardware limit)
- Sonnet + DeepSeek + GPT-5.4 can run in parallel (all cloud)
- All artifacts in `shared/` or committed to git
- 64K+ context window required for subagents
- DeepSeek needs tight scope (exact files) and 15+ min timeout
