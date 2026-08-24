## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

## PromptValidation

- 無課金・外部APIなしの自己検証は `dotnet run --project .\PromptValidation\PromptValidation.csproj -- --self-test` で実行する。
- `--dry-run`、`--analyze-results`、`--help` も外部APIを呼ばない。必要な範囲で確認なしに実行してよい。
- 引数なし、`--case`、`--suite`、`--probe-openai-cache`、`--model-benchmark` など、料金が発生するAPIを呼び出す可能性がある実行は、必ず事前にユーザーへ内容と料金発生の可能性を説明し、明示的な確認を得てから実行する。
- 判断できないオプションや新しい検証コマンドは、料金が発生するものとして扱い、ユーザーの確認なしに実行しない。
