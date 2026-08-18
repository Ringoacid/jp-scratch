#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import shutil
import sys
import tempfile
from pathlib import Path
from typing import Any

SCHEMA_VERSION = 1
STATUSES = {"active", "paused", "cancelled", "completed"}
PHASES = {
    "clarifying",
    "planning",
    "awaiting-plan-approval",
    "implementing",
    "testing",
    "reviewing",
    "adjudicating-review",
    "awaiting-fix-approval",
    "fixing",
    "awaiting-completion-approval",
    "blocked",
    "completed",
    "paused",
    "cancelled",
}
INVALID_FILENAME = re.compile(r'[\\/:*?"<>|\x00-\x1f]')
RESERVED_WINDOWS = {
    "CON", "PRN", "AUX", "NUL",
    *(f"COM{i}" for i in range(1, 10)),
    *(f"LPT{i}" for i in range(1, 10)),
}


def now_local() -> dt.datetime:
    return dt.datetime.now().astimezone()


def iso_now() -> str:
    return now_local().isoformat(timespec="seconds")


def stamp() -> str:
    return now_local().strftime("%Y%m%d-%H%M%S")


def sanitize_name(name: str) -> str:
    value = INVALID_FILENAME.sub("_", name).strip().rstrip(". ")
    value = re.sub(r"\s+", " ", value)
    if not value:
        value = "タスク"
    if value.upper() in RESERVED_WINDOWS:
        value = f"_{value}"
    return value[:80].rstrip(". ") or "タスク"


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as f:
        data = json.load(f)
    if not isinstance(data, dict):
        raise ValueError(f"JSON root must be an object: {path}")
    return data


def validate_state(data: dict[str, Any]) -> None:
    if data.get("schemaVersion") != SCHEMA_VERSION:
        raise ValueError(f"Unsupported schemaVersion: {data.get('schemaVersion')!r}")
    if data.get("status") not in STATUSES:
        raise ValueError(f"Invalid status: {data.get('status')!r}")
    phase = data.get("phase")
    if phase not in PHASES:
        raise ValueError(f"Invalid phase: {phase!r}")
    if not isinstance(data.get("approvals"), list):
        raise ValueError("approvals must be an array")
    if not isinstance(data.get("plans"), list):
        raise ValueError("plans must be an array")
    if not isinstance(data.get("reviews"), list):
        raise ValueError("reviews must be an array")


def atomic_write_state(task_dir: Path, data: dict[str, Any]) -> None:
    validate_state(data)
    task_dir.mkdir(parents=True, exist_ok=True)
    state_path = task_dir / "state.json"
    bak_path = task_dir / "state.json.bak"
    data["updatedAt"] = iso_now()
    payload = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
    fd, tmp_name = tempfile.mkstemp(prefix="state.", suffix=".tmp", dir=task_dir)
    tmp_path = Path(tmp_name)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
            f.write(payload)
            f.flush()
            os.fsync(f.fileno())
        if state_path.exists():
            shutil.copy2(state_path, bak_path)
        os.replace(tmp_path, state_path)
    finally:
        if tmp_path.exists():
            tmp_path.unlink(missing_ok=True)


def load_state(task_dir: Path, recover: bool = True) -> dict[str, Any]:
    state_path = task_dir / "state.json"
    try:
        data = read_json(state_path)
        validate_state(data)
        return data
    except Exception:
        if not recover:
            raise
        bak = task_dir / "state.json.bak"
        if not bak.exists():
            raise
        data = read_json(bak)
        validate_state(data)
        # Recover the last valid generation without rotating the broken file into .bak.
        payload = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
        tmp = task_dir / f"state.recover-{os.getpid()}.tmp"
        tmp.write_text(payload, encoding="utf-8", newline="\n")
        os.replace(tmp, state_path)
        return data


def rel_to_task(task_dir: Path, file_path: str) -> tuple[Path, str]:
    p = Path(file_path)
    full = p if p.is_absolute() else task_dir / p
    full = full.resolve()
    base = task_dir.resolve()
    try:
        rel = full.relative_to(base).as_posix()
    except ValueError as e:
        raise ValueError("Artifact file must be inside the task directory") from e
    return full, rel


def find_plan(state: dict[str, Any], rel: str) -> dict[str, Any] | None:
    for item in state["plans"]:
        if item.get("file") == rel:
            return item
    return None


def cmd_create(args: argparse.Namespace) -> dict[str, Any]:
    repo = Path(args.repo).resolve()
    root = repo / ".codex-loop" / "tasks"
    root.mkdir(parents=True, exist_ok=True)
    base = f"{sanitize_name(args.name)}-{stamp()}"
    task_dir = root / base
    n = 2
    while task_dir.exists():
        task_dir = root / f"{base}-{n}"
        n += 1
    for sub in ["plans", "reviews", "reports", "screenshots", "artifacts", "temp"]:
        (task_dir / sub).mkdir(parents=True, exist_ok=True)
    created = iso_now()
    state = {
        "schemaVersion": SCHEMA_VERSION,
        "taskId": task_dir.name,
        "taskName": args.name,
        "createdAt": created,
        "updatedAt": created,
        "status": "active",
        "phase": "clarifying",
        "nextAction": "要件を確認して実装プランを作成する",
        "executionMode": "mcp",
        "suspendedPhase": None,
        "luna": {
            "model": None,
            "threadId": None,
            "threadCreatedAt": None,
            "contextResetCount": 0
        },
        "plans": [],
        "reviews": [],
        "reports": [],
        "approvals": [],
        "latestPlan": None,
        "latestReview": None,
        "latestReport": None,
        "notes": []
    }
    atomic_write_state(task_dir, state)
    return {"taskDir": str(task_dir), "state": state}


def cmd_list(args: argparse.Namespace) -> Any:
    repo = Path(args.repo).resolve()
    root = repo / ".codex-loop" / "tasks"
    rows = []
    if not root.exists():
        return rows
    for task_dir in sorted((p for p in root.iterdir() if p.is_dir()), key=lambda p: p.name):
        try:
            state = load_state(task_dir)
        except Exception as e:
            rows.append({"taskDir": str(task_dir), "error": str(e)})
            continue
        if not args.include_completed and state["status"] == "completed":
            continue
        if not args.include_cancelled and state["status"] == "cancelled":
            continue
        rows.append({
            "taskDir": str(task_dir),
            "taskName": state["taskName"],
            "status": state["status"],
            "phase": state["phase"],
            "updatedAt": state["updatedAt"],
            "nextAction": state.get("nextAction")
        })
    rows.sort(key=lambda r: r.get("updatedAt", ""), reverse=True)
    return rows


def cmd_show(args: argparse.Namespace) -> Any:
    return load_state(Path(args.task).resolve())


def cmd_update(args: argparse.Namespace) -> Any:
    task_dir = Path(args.task).resolve()
    state = load_state(task_dir)
    if args.status:
        if args.status not in STATUSES:
            raise ValueError(f"Invalid status: {args.status}")
        if args.status in {"paused", "cancelled"} and state.get("phase") not in {"paused", "cancelled", "completed"}:
            state["suspendedPhase"] = state.get("phase")
        if args.status == "active" and not args.phase and state.get("phase") in {"paused", "cancelled"}:
            state["phase"] = state.get("suspendedPhase") or "clarifying"
            state["suspendedPhase"] = None
        state["status"] = args.status
    if args.phase:
        if args.phase not in PHASES:
            raise ValueError(f"Invalid phase: {args.phase}")
        state["phase"] = args.phase
    if args.next_action is not None:
        state["nextAction"] = args.next_action
    if args.execution_mode:
        state["executionMode"] = args.execution_mode
    if args.note:
        state.setdefault("notes", []).append({"at": iso_now(), "text": args.note})
    atomic_write_state(task_dir, state)
    return state


def cmd_add_plan(args: argparse.Namespace) -> Any:
    task_dir = Path(args.task).resolve()
    state = load_state(task_dir)
    full, rel = rel_to_task(task_dir, args.file)
    if not full.is_file():
        raise FileNotFoundError(full)
    if find_plan(state, rel):
        raise ValueError(f"Plan already registered: {rel}")
    entry = {
        "kind": args.kind,
        "file": rel,
        "createdAt": iso_now(),
        "approved": False,
        "approvedAt": None,
        "approvedSha256": None
    }
    state["plans"].append(entry)
    state["latestPlan"] = rel
    state["phase"] = "awaiting-plan-approval" if args.kind == "implementation" else "awaiting-fix-approval"
    state["nextAction"] = "ユーザーの実装プラン承認待ち" if args.kind == "implementation" else "ユーザーの修正プラン承認待ち"
    atomic_write_state(task_dir, state)
    return entry


def cmd_approve_plan(args: argparse.Namespace) -> Any:
    task_dir = Path(args.task).resolve()
    state = load_state(task_dir)
    full, rel = rel_to_task(task_dir, args.file)
    if not full.is_file():
        raise FileNotFoundError(full)
    plan = find_plan(state, rel)
    if not plan:
        raise ValueError(f"Plan is not registered: {rel}")
    digest = sha256_file(full)
    approved_at = iso_now()
    plan["approved"] = True
    plan["approvedAt"] = approved_at
    plan["approvedSha256"] = digest
    approval = {
        "type": args.approval_type,
        "target": rel,
        "sha256": digest,
        "approvedAt": approved_at,
        "source": "explicit-user-confirmation"
    }
    state["approvals"].append(approval)
    state["phase"] = args.next_phase
    state["nextAction"] = args.next_action
    atomic_write_state(task_dir, state)
    return approval


def cmd_verify_plan(args: argparse.Namespace) -> Any:
    task_dir = Path(args.task).resolve()
    state = load_state(task_dir)
    full, rel = rel_to_task(task_dir, args.file)
    plan = find_plan(state, rel)
    if not plan or not plan.get("approved") or not plan.get("approvedSha256"):
        raise ValueError(f"Plan is not approved: {rel}")
    actual = sha256_file(full)
    expected = plan["approvedSha256"]
    result = {"file": rel, "expectedSha256": expected, "actualSha256": actual, "valid": actual == expected}
    if actual != expected:
        print(json.dumps(result, ensure_ascii=False, indent=2))
        sys.exit(3)
    return result


def cmd_add_review(args: argparse.Namespace) -> Any:
    task_dir = Path(args.task).resolve()
    state = load_state(task_dir)
    full, rel = rel_to_task(task_dir, args.file)
    if not full.is_file():
        raise FileNotFoundError(full)
    entry = {
        "file": rel,
        "createdAt": iso_now(),
        "model": args.model,
        "threadId": args.thread_id
    }
    state["reviews"].append(entry)
    state["latestReview"] = rel
    state["phase"] = "adjudicating-review"
    state["nextAction"] = "Claude + Advisorでレビューの妥当性を検証する"
    atomic_write_state(task_dir, state)
    return entry


def cmd_add_report(args: argparse.Namespace) -> Any:
    task_dir = Path(args.task).resolve()
    state = load_state(task_dir)
    full, rel = rel_to_task(task_dir, args.file)
    if not full.is_file():
        raise FileNotFoundError(full)
    entry = {"file": rel, "createdAt": iso_now()}
    state["reports"].append(entry)
    state["latestReport"] = rel
    if args.complete:
        state["status"] = "completed"
        state["phase"] = "completed"
        state["nextAction"] = "完了"
    atomic_write_state(task_dir, state)
    return entry


def cmd_set_luna(args: argparse.Namespace) -> Any:
    task_dir = Path(args.task).resolve()
    state = load_state(task_dir)
    previous = state["luna"].get("threadId")
    if previous and previous != args.thread_id:
        state["luna"]["contextResetCount"] = int(state["luna"].get("contextResetCount") or 0) + 1
    state["luna"]["model"] = args.model
    state["luna"]["threadId"] = args.thread_id
    state["luna"]["threadCreatedAt"] = args.thread_created_at or iso_now()
    atomic_write_state(task_dir, state)
    return state["luna"]


def cmd_clear_luna(args: argparse.Namespace) -> Any:
    task_dir = Path(args.task).resolve()
    state = load_state(task_dir)
    if state["luna"].get("threadId"):
        state["luna"]["contextResetCount"] = int(state["luna"].get("contextResetCount") or 0) + 1
    state["luna"]["threadId"] = None
    state["luna"]["threadCreatedAt"] = None
    atomic_write_state(task_dir, state)
    return state["luna"]


def cmd_add_approval(args: argparse.Namespace) -> Any:
    task_dir = Path(args.task).resolve()
    state = load_state(task_dir)
    entry = {
        "type": args.approval_type,
        "target": args.target,
        "sha256": None,
        "approvedAt": iso_now(),
        "source": "explicit-user-confirmation"
    }
    state["approvals"].append(entry)
    if args.phase:
        state["phase"] = args.phase
    if args.next_action is not None:
        state["nextAction"] = args.next_action
    atomic_write_state(task_dir, state)
    return entry


def cmd_recover(args: argparse.Namespace) -> Any:
    task_dir = Path(args.task).resolve()
    return load_state(task_dir, recover=True)


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="codex-loop task state manager")
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("create")
    s.add_argument("--repo", required=True)
    s.add_argument("--name", required=True)
    s.set_defaults(func=cmd_create)

    s = sub.add_parser("list")
    s.add_argument("--repo", required=True)
    s.add_argument("--include-completed", action="store_true")
    s.add_argument("--include-cancelled", action="store_true")
    s.set_defaults(func=cmd_list)

    s = sub.add_parser("show")
    s.add_argument("--task", required=True)
    s.set_defaults(func=cmd_show)

    s = sub.add_parser("update")
    s.add_argument("--task", required=True)
    s.add_argument("--status")
    s.add_argument("--phase")
    s.add_argument("--next-action")
    s.add_argument("--execution-mode", choices=["mcp", "codex-exec"])
    s.add_argument("--note")
    s.set_defaults(func=cmd_update)

    s = sub.add_parser("add-plan")
    s.add_argument("--task", required=True)
    s.add_argument("--file", required=True)
    s.add_argument("--kind", required=True, choices=["implementation", "fix"])
    s.set_defaults(func=cmd_add_plan)

    s = sub.add_parser("approve-plan")
    s.add_argument("--task", required=True)
    s.add_argument("--file", required=True)
    s.add_argument("--approval-type", required=True, choices=["implementation_plan", "fix_plan"])
    s.add_argument("--next-phase", required=True, choices=sorted(PHASES))
    s.add_argument("--next-action", required=True)
    s.set_defaults(func=cmd_approve_plan)

    s = sub.add_parser("verify-plan")
    s.add_argument("--task", required=True)
    s.add_argument("--file", required=True)
    s.set_defaults(func=cmd_verify_plan)

    s = sub.add_parser("add-review")
    s.add_argument("--task", required=True)
    s.add_argument("--file", required=True)
    s.add_argument("--model", required=True)
    s.add_argument("--thread-id")
    s.set_defaults(func=cmd_add_review)

    s = sub.add_parser("add-report")
    s.add_argument("--task", required=True)
    s.add_argument("--file", required=True)
    s.add_argument("--complete", action="store_true")
    s.set_defaults(func=cmd_add_report)

    s = sub.add_parser("set-luna")
    s.add_argument("--task", required=True)
    s.add_argument("--thread-id", required=True)
    s.add_argument("--model", required=True)
    s.add_argument("--thread-created-at")
    s.set_defaults(func=cmd_set_luna)

    s = sub.add_parser("clear-luna")
    s.add_argument("--task", required=True)
    s.set_defaults(func=cmd_clear_luna)

    s = sub.add_parser("add-approval")
    s.add_argument("--task", required=True)
    s.add_argument("--approval-type", required=True)
    s.add_argument("--target")
    s.add_argument("--phase", choices=sorted(PHASES))
    s.add_argument("--next-action")
    s.set_defaults(func=cmd_add_approval)

    s = sub.add_parser("recover")
    s.add_argument("--task", required=True)
    s.set_defaults(func=cmd_recover)
    return p


def main() -> None:
    args = build_parser().parse_args()
    try:
        result = args.func(args)
        print(json.dumps(result, ensure_ascii=False, indent=2))
    except SystemExit:
        raise
    except Exception as e:
        print(json.dumps({"error": str(e)}, ensure_ascii=False, indent=2), file=sys.stderr)
        sys.exit(2)


if __name__ == "__main__":
    main()
