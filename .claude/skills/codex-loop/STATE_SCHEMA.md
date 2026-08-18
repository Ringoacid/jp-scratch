# codex-loop `state.json` schema (v1)

`state.json` is the machine-readable source of truth for task progress. Markdown plans/reviews/reports are artifacts; workflow resumption is controlled by this file.

Core fields:

```json
{
  "schemaVersion": 1,
  "taskId": "Passkeyログイン追加-20260818-011530",
  "taskName": "Passkeyログイン追加",
  "createdAt": "2026-08-18T01:15:30+09:00",
  "updatedAt": "2026-08-18T01:42:10+09:00",
  "status": "active",
  "phase": "awaiting-fix-approval",
  "nextAction": "ユーザーの修正プラン承認待ち",
  "executionMode": "mcp",
  "luna": {
    "model": "gpt-5.6-luna",
    "threadId": "...",
    "threadCreatedAt": "...",
    "contextResetCount": 0
  },
  "plans": [],
  "reviews": [],
  "reports": [],
  "approvals": [],
  "latestPlan": null,
  "latestReview": null,
  "latestReport": null,
  "notes": []
}
```

## `status`

- `active`: current work
- `paused`: intentionally suspended, resumable
- `cancelled`: stopped, retained, resumable only by explicit user request
- `completed`: finished

## `phase`

- `clarifying`
- `planning`
- `awaiting-plan-approval`
- `implementing`
- `testing`
- `reviewing`
- `adjudicating-review`
- `awaiting-fix-approval`
- `fixing`
- `awaiting-completion-approval`
- `blocked`
- `completed`
- `paused`
- `cancelled`

## Plan integrity

Each approved plan stores its SHA-256 in both the plan entry and the approval history. Approved plan files are immutable. The state manager verifies the current file against the approval hash before implementation/resumption.

## Writes and backup

State manager writes use a same-directory temporary file followed by replacement. Before replacement, the previous valid `state.json` becomes `state.json.bak`. Only one backup generation is retained.
