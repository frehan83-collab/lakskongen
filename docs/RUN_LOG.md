# RUN LOG — Autonomous Top-Tier Run

> **Normative protocol for the long autonomous run.**
> `docs/AUTONOMOUS_TOP_TIER_RUN.md` defines **WHAT** (waves W0–W7, D1–D10, Briefs A–E).
> This file defines **HOW** sessions run, **what counts as evidence**, and **where the run stands**.
> The repo is the only cross-session memory. Chat dies with the context window — if it is not
> written here (or another committed file), it never happened.

---

## 0. Protocol — every session, in order

1. **Bootstrap.** Read `AGENTS.md` → `docs/AUTONOMOUS_TOP_TIER_RUN.md` → this file
   (Status header + tail of Session ledger). Run `./tools/ci.sh` once on the current tree.
2. **Audit the prior session.** Pick ONE claim from the last session report; re-run its
   cited command; compare raw output. Log PASS/FAIL. A failed audit marks downstream
   work *suspect* until re-verified (note it in Blocked/Dead-ends).
3. **Claim before work.** Set `Active claim:` in the Status header (session #, wave, ONE
   deliverable). If a stale claim exists (>1 session old), verify it or release it.
4. **Pre-register wave criteria.** When starting a new wave, fill its row in the Wave
   register *before* implementing — done means what the row said, not what is convenient.
5. **Implement the smallest complete slice** behind its evidence gate
   (player-path test / screenshot diff / perf number / save-hash).
6. **CI.** Run `./tools/ci.sh`; record the raw result.
7. **Report.** Append the session report (template in §10); update the Status header
   (done / next / blocked / gates / last-verified artifact); update `AGENTS.md` if a
   phase/SSOT fact changed; compact context at wave boundaries.
8. **Continue** unless a STOP condition (§3) fired.

---

## 1. Evidence contract

- Every "done" = **file path + exact command + raw output** (commit SHA, CI result, test
  counts, screenshot paths, perf numbers). Nothing else counts.
- **Never cite an earlier session's green** — re-run it yourself.
- **Screenshot claims**: path + which forced state + distinctness method (md5/diff).
- **Auto-fail fake-done blacklist**: "should work", "likely fine", edited thresholds,
  skipped/ignored/deleted tests, error suppression, claiming a gate without raw output.

## 2. Scoreboard is add-only

Tests, CI config, release thresholds and D1–D10 criteria may only be **strengthened**,
never weakened. If a test looks wrong: write a `Dispute` entry in §8, mark the item
blocked, and stop — editing the scoreboard is a human gate.

## 3. STOP conditions (halt the loop, write HANDOFF NEEDED)

- **Human gate reached** (plan §12): Steam account/depot credentials; playtest protocol;
  paid-service sign-off; capsule/trailer/price approval; legal; **any push to a remote**.
- **Any spend proposal** — always a human gate (free-first, plan §7).
- **Circuit breakers** (log, no human needed to trigger):
  - CI red in two consecutive sessions;
  - the same deliverable failed twice;
  - a target metric unmoved across two waves.
- **All remaining unblocked work exhausted.**

## 4. Status header — EDIT IN PLACE

- **Run phase:** `W0 — not started`
- **Active claim:** `(none)`
- **Gate state:** `no human gates hit`
- **Last verified artifact:** `HEAD 06ee6d4 + uncommitted run-prep tree; CI 15/15 @ 2026-10-12`
- **Baseline freeze:** `main suite 684 passed / 1 skipped; CI 15/15; save-hash: TBD (session 0)`

### Session-0 note
After CI is green, create a **local baseline commit** (do NOT push — pushing is a human
gate) so every evidence claim can cite a SHA. From then on, one commit per completed
deliverable, message format: `run: <wave> <item> — <one-line outcome>`.

---

## 5. Session ledger (append-only)

| # | Date | Wave | Claim | Evidence (path + cmd + result) | Audit of prior | CI |
|---|------|------|-------|-------------------------------|----------------|----|

## 6. Wave register (pre-register BEFORE the wave)

| Wave | Done-criteria (pre-registered) | Status | Evidence/Report |
|------|-------------------------------|--------|-----------------|
| W0 | MCP inventory doc; Briefs A–E dispatched; `lakskongen-feature` + `lakskongen-screenshot-evidence` authored+validated; baseline frozen | not started | |
| W1 | Decision memos for R1–R4; skills-gap list; plan wave order updated if findings demand | not started | |
| W2 | Living fjord frames distinct per forced state (lice/red, bloom, storm, night); time-series replaces gauge cluster; a11y audit passes | not started | |
| W3 | Chosen datasets embedded offline with provenance + license notes; player-path test shows band movement | not started | |
| W4 | 3-OS exports as CI artifacts; save fuzz 10k clean; version stamping; depot script written | not started | |
| W5 | Demo slice (`forste_sesong` + `po3_red_sone`) polished; first-15-min script; demo build passes W4 gates | not started | |
| W6 | Ranked visual items shipped (perf + screenshot gates each); audio pass; store art with licenses | not started | |
| W7 | D2–D7 evidence assembled; release candidate tagged | not started | |

## 7. Decision log (autonomous decisions)

| Date | Decision | Rationale | Reversible? |
|------|----------|-----------|-------------|

## 8. Blocked / dead ends / disputes

| Item | Why | Unblock condition |
|------|-----|-------------------|

## 9. Human-gate queue

| Gate | Status | Needs from owner |
|------|--------|------------------|
| Steam partner account + depot credentials | pending | account, fee, KYC, upload creds |
| Playtest protocol (5 × 2 h) | pending | schedule + testers |
| Strategy-sweep failure criterion | pending | pre-registered criterion |
| Paid-service sign-offs | none requested | — |
| Capsule / trailer / price / Next Fest submission | pending | final art + build |
| Legal: EULA, privacy, AI-content disclosure, ratings | pending | review |
| Push to remote | not started | standing approval or per-push review |

## 10. Session report template (copy per session)

```markdown
### Session <#>: <wave> — <claim>
- **Shipped:** <artifacts + file paths>
- **Evidence:** <command + raw result; commit SHA>
- **Audit:** <prior claim verified, result>
- **CI:** <./tools/ci.sh raw result>
- **Next:** <one line>
- **Blocked:** <list or none>
- **Gates:** <human gates hit or none>
```
