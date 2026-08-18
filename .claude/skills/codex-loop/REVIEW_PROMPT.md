# Sol review contract

This file is reference material for the `/codex-loop` skill. The skill should synthesize a task-specific prompt rather than blindly pasting this file.

- Review the current Git working tree as a whole: staged, unstaged, and relevant untracked files.
- Include changes that existed before the current codex-loop task and changes made by the user, Claude, or Luna.
- Exclude ignored files and obvious build output, caches, and temporary files.
- Do not modify source code or normal project configuration. You may write review artifacts only below the current task directory in `.codex-loop/`.
- Run relevant builds/tests/static checks when useful.
- Focus on defects with practical impact: correctness, regression, security, data integrity, error handling, concurrency, material performance issues, and insufficient tests.
- Avoid style-only, naming-only, formatting-only, and subjective architecture comments unless they lead to a concrete defect.
- Every finding gets a severity: Critical / High / Medium / Low. Treat the labels as guidance, not a rigid taxonomy.
- Write natural Markdown to the exact review path supplied by Claude.
- If there are no actionable findings, say `修正すべき指摘なし` explicitly.
