---
name: commit-message
description: Analyze local Git changes, propose two Japanese commit messages, and create a commit after explicit user selection. Use when the user asks for commit-message suggestions or asks Codex to commit without supplying final wording. Do not use for push, amend, rebase, or other history rewrites.
---

# Japanese Commit Message

Create one well-scoped Git commit with a Japanese message chosen by the user.

## Determine the scope

- Follow the applicable `AGENTS.md` instructions.
- Respect paths or files named by the user. If none are named, inspect the whole worktree but do not assume unrelated changes belong in the commit.
- Inspect both staged and unstaged changes with `git status --short`, `git diff --stat`, `git diff --cached --stat`, and the relevant full diffs.
- Check recent commit subjects when needed to learn the repository's message convention.
- Stop and ask which changes belong together when the scope is ambiguous, when unrelated changes are mixed in, or when existing staged changes do not match the intended scope.
- Treat generated artifacts, credentials, and possible secrets as scope hazards that require confirmation before staging.

## Propose and confirm

When the user has not supplied final wording, show exactly two Japanese proposals:

1. `案1（詳細版）`: a concise subject followed, when useful, by a blank line and bullet points covering the important changes.
2. `案2（簡潔版）`: a one-line summary of the same change.

Base both proposals only on the intended diff. Match an established repository convention; otherwise use an appropriate prefix such as `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, or `chore:`. Keep the subject near 50 Japanese characters when practical. Do not claim effects or verification absent from the evidence.

Do not stage or commit yet. Ask the user to choose `案1`, `案2`, or provide revised wording, using the host's user-confirmation facility when available. Wait for an explicit answer. Invoking this skill is not approval of either proposal.

If the user already supplied exact final wording, preserve it and skip the two-proposal choice unless it conflicts with the actual diff; resolve any conflict before committing.

## Stage, verify, and commit

After the message and scope are explicit:

1. Stage only the intended paths with `git add -- <paths>`. Do not use `git add .` or `git add -A`, and do not unstage existing work without permission.
2. Run `git diff --cached --check`, then inspect `git diff --cached --stat` and `git diff --cached` to confirm the staged patch matches the approved scope and message.
3. If the staged patch is empty or includes unintended changes, stop without committing and explain the mismatch.
4. Commit non-interactively with the approved message. Do not bypass hooks with `--no-verify`.
5. Verify the result with `git status --short` and the new commit metadata. Report the commit hash, final message, and any remaining uncommitted changes.

## Safety boundaries

- Do not run `git reset`, `git restore`, `git checkout`, `git stash`, `git commit --amend`, or `git push` unless the user explicitly requests that separate action.
- A request to draft or choose a message does not authorize a commit. Commit only when the user's request includes committing and the final wording has been explicitly selected or supplied.
- If commit hooks modify files or the commit fails, inspect and report the result. Do not blindly repeat the command.
