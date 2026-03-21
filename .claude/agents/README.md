# Custom Agents

Place `.md` files here to define project-level subagents that Claude can spawn via the Agent tool.

## File Format

```markdown
---
name: agent-name
description: When to use this agent. Be specific — Claude reads this to decide which agent to pick.
model: sonnet          # optional: sonnet | opus | haiku
allowed-tools: Read, Grep, Glob, Bash
---

System prompt / instructions for this agent.
Describe the agent's persona, capabilities, and constraints.
```

## Notes

- The `description` field is critical — it determines when this agent gets selected.
- Agents run with their own context window, isolating noise from the main conversation.
- Restrict `allowed-tools` to the minimum the agent needs.
