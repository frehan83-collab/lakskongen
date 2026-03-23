# Agent Collaboration Protocol

## How It Works

Agents can ask each other questions via a shared message board.
The orchestrator (Opus) monitors and relays messages between agents.

## For Agents: How to Ask a Question

1. Write your question to `shared/collab/questions/<your-task-id>.md`:
```
TO: T81
FROM: T80
QUESTION: What signals does Dashboard.gd emit? I need to connect PauseMenu to know if dashboard is visible.
STATUS: waiting
```

2. Continue working on other parts of your task while waiting.
3. Check `shared/collab/answers/<your-task-id>.md` for responses.

## For Agents: How to Answer

If you see a question file addressed to you in `shared/collab/questions/`:
1. Read the question
2. Write your answer to `shared/collab/answers/<asking-task-id>.md`:
```
FROM: T81
ANSWER: Dashboard.gd emits `panel_toggled(visible: bool)` and `tab_changed(tab_name: String)`.
```

## Active Agents Registry

Check `shared/collab/agents.md` for currently running agents and their responsibilities.
