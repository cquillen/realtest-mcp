# Hooks

Hooks are shell commands that fire automatically on Claude Code lifecycle events.
Configure them in `settings.json` under the `hooks` key.

## Supported Events

| Event         | Fires...                                      |
|---------------|-----------------------------------------------|
| PreToolUse    | Before any tool call                          |
| PostToolUse   | After any tool call                           |
| Notification  | When Claude sends a notification              |
| Stop          | When Claude finishes a response               |

## settings.json Format

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "bash .claude/hooks/post-bash.sh"
          }
        ]
      }
    ],
    "Stop": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "bash .claude/hooks/on-stop.sh"
          }
        ]
      }
    ]
  }
}
```

## Hook Script Contract

- Scripts receive event context via stdin as JSON.
- Exit code `0` = success (allow the action to proceed).
- Exit code non-zero = block the action and surface the error to Claude.
- Write feedback to stdout; Claude will see it.
