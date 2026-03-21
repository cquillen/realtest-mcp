# Custom Slash Commands

Place `.md` files here to create project-level slash commands available as `/command-name`.

## File Format

```markdown
---
description: Short description shown in the command picker
allowed-tools: Bash, Read, Write, Edit, Glob, Grep
---

The prompt body that Claude will receive when this command is invoked.
Use $ARGUMENTS to capture anything typed after the command name.
```

## Example

A file named `review.md` becomes `/review` and can be invoked as `/review src/main.ts`.
