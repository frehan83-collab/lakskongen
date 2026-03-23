# Challenge-First Development Protocol

## The Rule: No Code Before Challenge

Every sprint follows this flow:

```
PHASE 1: PLAN (Orchestrator)
  → Break vision into tasks
  → Assign roles to agents

PHASE 2: PROPOSE (Each agent, 2-3 min)
  → Read VISION.md
  → Read relevant existing code
  → Write a PROPOSAL to shared/collab/proposals/<task-id>.md:
    - What I plan to build
    - Which files I'll modify
    - Key design decisions
    - How it connects to other systems
    - What I think could go wrong

PHASE 3: CHALLENGE (All agents review each other, 2-3 min)
  → Each agent reads OTHER agents' proposals
  → Write challenges to shared/collab/challenges/<task-id>.md:
    - "Does this match VISION.md?"
    - "This approach will break X because..."
    - "Better alternative: ..."
    - "Missing: VISION.md says X but your plan ignores it"
    - "This conflicts with <other-agent>'s plan for..."

PHASE 4: RESOLVE (Orchestrator reviews, 1 min)
  → Opus reads all proposals + challenges
  → Writes FINAL SPEC to shared/collab/specs/<task-id>.md
  → Includes accepted challenges and rejected ones with reasoning

PHASE 5: BUILD (Agents code against approved specs)
  → Each agent builds ONLY what the spec says
  → If they discover something unexpected, write to questions/
  → Orchestrator relays and resolves
```

## Challenge Quality Rules

Good challenges:
- "VISION.md says 'one more turn syndrome' but your Dashboard has no next-action suggestion"
- "Your PauseMenu doesn't handle the campaign timer — VISION says seasonal time pressure"
- "The HUD speed buttons should include 10x/30x for endgame — VISION mentions 'Anno-style scaling'"

Bad challenges:
- "Looks good to me" (not a challenge)
- "I would do it differently" (no specific reason)
- "This is wrong" (no alternative proposed)

## Vision Alignment Checklist

Every proposal MUST address:
- [ ] Does this support "one more turn" syndrome?
- [ ] Does this help the first-2-hours experience?
- [ ] Is it "easy to start, endlessly deep"?
- [ ] Does it show visible results of player actions?
- [ ] Norwegian authenticity maintained?
- [ ] Would a real fish farmer find this useful/realistic?

## Agent Roles in Challenge Phase

| Role | Perspective |
|------|-------------|
| Architect (Opus) | System coherence, VISION alignment |
| Builder A (Sonnet) | Code quality, integration risk |
| Builder B (Kimi/DeepSeek) | Performance, edge cases, alternatives |
| Builder C (Gemini) | UX, player experience, accessibility |
