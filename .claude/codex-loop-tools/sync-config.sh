#!/usr/bin/env bash
set -euo pipefail
ROOT="${1:-$PWD}"
if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required to sync codex-loop configuration on Linux/macOS." >&2
  exit 1
fi
python3 - "$ROOT" <<'PY'
import json, pathlib, re, sys
root = pathlib.Path(sys.argv[1]).resolve()
config_path = root / '.claude' / 'codex-loop.json'
settings_path = root / '.claude' / 'settings.json'
skill_path = root / '.claude' / 'skills' / 'codex-loop' / 'SKILL.md'
config = json.loads(config_path.read_text(encoding='utf-8'))
models = config.get('models', {}) if isinstance(config.get('models', {}), dict) else {}
orch = models.get('orchestrator', 'opus') or 'opus'
advisor = models.get('advisor', 'fable') or 'fable'
settings = {}
if settings_path.exists() and settings_path.read_text(encoding='utf-8').strip():
    settings = json.loads(settings_path.read_text(encoding='utf-8'))
settings.setdefault('$schema', 'https://json.schemastore.org/claude-code-settings.json')
settings['model'] = orch
settings['advisorModel'] = advisor
perms = settings.get('permissions')
if not isinstance(perms, dict):
    perms = {}
    settings['permissions'] = perms
allow = perms.get('allow')
if not isinstance(allow, list):
    allow = []
    perms['allow'] = allow
if 'mcp__codex__*' not in allow:
    allow.append('mcp__codex__*')
settings_path.parent.mkdir(parents=True, exist_ok=True)
settings_path.write_text(json.dumps(settings, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
text = skill_path.read_text(encoding='utf-8')
if not text.startswith('---\n'):
    raise SystemExit('SKILL.md frontmatter not found')
parts = text.split('---\n', 2)
front = parts[1]
if re.search(r'(?m)^model:\s*.*$', front):
    front = re.sub(r'(?m)^model:\s*.*$', f'model: {orch}', front, count=1)
else:
    front += f'model: {orch}\n'
skill_path.write_text('---\n' + front + '---\n' + parts[2], encoding='utf-8')
print(f'Synced orchestrator={orch}, advisor={advisor}')
PY
