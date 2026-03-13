# Lakskongen — Agent Team Protocol

## Team Structure

| Role | Agent | Model | Scope |
|------|-------|-------|-------|
| **Orchestrator** | Opus (main) | claude-opus-4-6 | Route tasks, track state, QC, priority calls |
| **Builder-1** | Sonnet | claude-sonnet-4-5 | Complex multi-file features, architecture, bridge work |
| **Builder-2** | GLM Flash | glm-4.7-flash (local) | Single-file edits, config tweaks, existing code modifications |
| **Reviewer** | Opus (inline) | claude-opus-4-6 | Verify artifacts, push back on gaps |
| **Executive Producer** | Fredrik J. Hansen | Human | Strategic decisions, approval gates |

## Model Capabilities (Verified 2026-03-13)

### Sonnet (Builder-1) — Primary
- ✅ Multi-file features, cross-system integration
- ✅ New file creation from scratch (code + data)
- ✅ Complex logic, defensive coding
- ⏱️ ~1-3 min per task
- 💰 ~$0.40/run

### GLM Flash (Builder-2) — Helper
- ✅ Single-file edits with clear context
- ✅ Modifying existing code (well-defined task)
- ❌ Creating new files from scratch (fails consistently)
- ❌ Multi-system integration (needs cross-file context)
- ⏱️ ~5-7 min per task
- 💰 FREE

## Task Routing Rules

| Task Type | Route To |
|-----------|----------|
| New feature (multi-file) | Sonnet |
| New data files (JSON, config) | Sonnet |
| Single-file code edit | GLM Flash |
| Existing file modification (clear scope) | GLM Flash |
| Cross-system integration | Sonnet |
| Architecture decisions | Opus (orchestrator) |
| QC / Code review | Opus (orchestrator) |

## Handoff Format

Every completed task:
1. What changed (files + summary)
2. Verified via `git diff --stat`
3. Committed with descriptive message
4. Next action identified

## Constraints
- ONE local model at a time (hardware limit)
- Sonnet (cloud) can run parallel with local models
- All artifacts in `shared/` or committed to git
- 64K+ context window required for subagents
