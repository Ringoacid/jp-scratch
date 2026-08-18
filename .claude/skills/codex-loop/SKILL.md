---
name: codex-loop
description: Stateful Claude Code + Codex implementation/review workflow with explicit user plan approvals, resumable tasks, Luna implementation, Sol review, and Fable advisor support.
argument-hint: "[新規タスク内容 / 再開したいタスク / pause / cancel / resume]"
disable-model-invocation: true
model: opus
---

# Codex Loop

`/codex-loop` は、Claude Codeをオーケストレーターとして、Codexの実装・レビューをユーザー承認付きで反復するための状態管理ワークフローである。

プロジェクトルートは `${CLAUDE_PROJECT_DIR}`。設定の正本は `${CLAUDE_PROJECT_DIR}/.claude/codex-loop.json`、実行状態の正本は各タスクの `state.json` とする。

## 0. 最重要原則

1. **1タスク1テーマ**。独立した複数テーマが混在する場合は分割を提案する。
2. **Lunaを主実装者**とする。ただしClaude自身も必要と判断すれば自由にコードを編集してよい。
3. **Solはレビュー担当**。通常のソースコード・設定ファイルは変更せず、`.codex-loop/` 配下へのレビュー・スクリーンショット・補助資料の書き込みだけを許可する。技術的な強制ではなくプロンプト規約とする。
4. **Solのレビュー対象は、その時点のGit変更全体**。`/codex-loop` 開始前の変更、Claude/Luna/ユーザーの変更を区別しない。
5. staged / unstaged / untracked を原則すべてレビューする。ただし `.gitignore` 対象と明らかな生成物・キャッシュ・一時ファイルは除外してよい。
6. **初回実装プランと各修正プランは、ユーザーの明示承認があるまで実装しない。** 「OK」「進めて」「承認」「その方針で」等、明確な実装開始意思が必要。曖昧な相槌は承認扱いにしない。
7. 承認済みプランMarkdownは不変。変更が必要なら新しいプランファイルを作成し、再承認する。
8. 承認済みプランはSHA-256を保存し、実装開始前と再開時に検証する。不一致なら停止してユーザーに確認する。
9. `.codex-loop/` はGit管理外。コミット対象に含めない。
10. `git add` は必要に応じて実行してよい。`git commit` はユーザーが明示的に頼んだ場合のみ実行してよい。
11. `git reset` / `git restore` / `git checkout --` / `git clean` / `git stash` 等、既存変更を破棄・退避し得る操作は、必要になった時点で必ずユーザーに一度確認する。
12. ビルド・テスト・lint・formatter・型チェック等はプロジェクトに合わせて積極的に実行し、失敗時は原因を調査する。
13. Fable Advisorの使用タイミングは固定しない。計画・設計・レビュー査定・修正方針など、必要な場面で随時相談してよい。
14. 仕様・UX・既存挙動・優先順位など、勝手に決めると完成形が変わる不明点は積極的にユーザーへ質問する。相互依存する重要事項は逐次、独立した軽い確認はまとめてよい。

## 1. 設定を読む

毎回最初に `.claude/codex-loop.json` を読む。モデル名・リトライ数・ポリシーはこの設定を優先する。

デフォルト構成:

- Orchestrator: `opus`
- Advisor: `fable`
- Implementation: `gpt-5.6-luna`
- Review: `gpt-5.6-sol`

Skillの `model:` は呼び出したターンだけ有効なので、`.claude/codex-loop.json` の `models.orchestrator` / `models.advisor` は付属の同期スクリプトでSkill frontmatterと `.claude/settings.json` のプロジェクト既定 `model` / `advisorModel` の両方へ同期する。ユーザーが `/model` で明示的に切り替えたセッションでは、そのセッション指定が優先される。

## 2. state.json の操作

状態変更は可能な限り付属state managerを使う。これにより `state.json` は一時ファイルから原子的に置換され、直前の正常状態を `state.json.bak` に1世代残す。

### Linux / macOS / WSL

```bash
python3 "${CLAUDE_PROJECT_DIR}/.claude/skills/codex-loop/scripts/state_manager.py" <command> ...
```

### Native Windows

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".claude\skills\codex-loop\scripts\state_manager.ps1" <command> ...
```

利用コマンド: `create`, `list`, `show`, `update`, `add-plan`, `approve-plan`, `verify-plan`, `add-review`, `add-report`, `set-luna`, `clear-luna`, `add-approval`, `recover`。

state managerの一時的な実行失敗は設定回数（デフォルト2回）まで再試行してよい。継続失敗時は可能な限り現在状態を保存し、`blocked` 相当としてユーザーへ報告する。ビルド/テスト失敗は単純リトライではなく原因調査対象とする。

## 3. `/codex-loop` 起動時のタスク選択

### 引数なし

1. `state_manager list` で未完了タスクを取得する。
2. `active` / `paused` タスクがあれば、以下を簡潔に一覧表示する:
   - タスク名
   - 状態
   - 現在フェーズ
   - 最終更新日時
   - 次アクション
3. 再開するタスクか、新規タスク作成かをユーザーに選ばせる。
4. `cancelled` は通常一覧から分ける。ただしユーザーが復活を希望すれば `active` に戻して再開可能。

### 引数あり

`$ARGUMENTS` と既存未完了タスクのタスク名・目的を照合する。明らかに同一なら再開候補として扱う。曖昧なら「既存タスク再開 / 新規作成」を質問する。明らかに別テーマなら新規タスクにする。

### 新規タスク

ユーザー依頼から短い**日本語タスク名**を作る。state manager `create` を使う。タスクフォルダ名は `日本語タスク名-YYYYMMDD-HHMMSS`。Windows禁止文字はstate managerが安全に置換する。

タスク構成:

```text
.codex-loop/tasks/<task>/
├─ state.json
├─ state.json.bak
├─ plans/
├─ reviews/
├─ reports/
├─ screenshots/
├─ artifacts/
└─ temp/
```

## 4. 再開時の復旧

1. `state.json` を読む。破損時はstate managerが `state.json.bak` から復旧を試みる。
2. 承認済みプランが次工程の根拠なら `verify-plan` を実行する。
3. SHA-256不一致なら承認済み扱いで進めない。ユーザーに変更を知らせ、再承認または新プラン作成を求める。
4. `nextAction` と `phase` に従って再開する。
5. Luna thread IDが残っていても、文脈が長すぎる・前提が混乱している・再利用が危険と判断したら新しいLuna threadを作ってよい。その場合は承認済みプランと現在のコードを読み直させる。

## 5. 要件確認と実装プラン

実装前にリポジトリと依頼を調査する。重要な不明点があればユーザーへ質問する。細かな内部実装判断まで質問する必要はない。

プランの粒度は**中程度**。次を中心に書く:

- 何を変えるか
- なぜ変えるか
- どの方針で変えるか
- 主な影響範囲
- 検証方法
- ユーザーが明示した重要制約

ファイル名・クラス名・具体的コード例の羅列は、理解に必要な場合を除き避ける。「やらないこと」は毎回機械的に列挙せず、ユーザー指定や誤解すると危険な境界だけ書く。

Advisorは計画中に必要なだけ使ってよい。

プランを以下へ保存する:

- 初回: `plans/YYYYMMDD-HHMMSS-実装プラン.md`
- 改訂/レビュー対応: `plans/YYYYMMDD-HHMMSS-修正プラン.md`

保存後 `add-plan` で登録し、会話でもプランを提示して**明示承認待ち**にする。この段階では実装しない。

## 6. 実装プラン承認後

ユーザーの明示承認を確認したら:

1. `approve-plan` で承認時SHA-256を記録する。
2. 直前に `verify-plan` を実行する。
3. 承認済みMarkdownを正本としてLunaへ読ませ、MCPプロンプトにも目的・重要制約・検証条件を短く添える。
4. Codex MCP `codex` を新規実装threadとして呼ぶ。

実装Codex設定は `.claude/codex-loop.json` を使う。デフォルト:

- `model`: `gpt-5.6-luna`
- `cwd`: project root
- `sandbox`: `workspace-write`
- `approval-policy`: `never`
- `config`: workspace-writeのnetwork accessを有効化

Lunaのthread IDを `set-luna` で保存する。

### Lunaへの必須指示

- 承認済みプランを正本として読む。
- リポジトリを調査してから実装する。
- Lunaが主実装者だが、Claudeやユーザーが同じworking treeを編集する可能性がある。現在の状態を尊重する。
- 開始前からの変更も含め、既存変更を勝手に消さない。
- `git add` は可。`git commit` は、その時点でユーザーから明示依頼がある場合のみ。
- 破棄/退避系Git操作が必要なら実行せずClaudeへ返す。
- プロジェクトに適したビルド・テスト・lint・formatter等を積極的に実行する。
- UI変更ではスクリーンショット確認を推奨するが必須ではない。可能ならタスクの `screenshots/` を使う。
- `.env`、APIキー、トークン、接続文字列、秘密鍵等の**値を意図的に読んだり表示したりしない**。プログラムが環境変数を内部利用することは可。秘密がログへ出ない設定を優先する。
- ネットワークの読み取り、パッケージ取得、公式ドキュメント確認は可。
- 外部書き込みも「追加料金なし・公開サービスの状態を変えない・破壊的でない・第三者へ影響しない」を満たす場合のみ自律実行可。それ以外はClaudeへ確認を返す。
- プランに承認済みの依存追加は自動実行してよい。実装途中で新規の外部依存が必要になった場合は実装を止め、Claudeへ返す。

## 7. 実装中のプラン逸脱

局所的な実装調整・命名・小規模内部リファクタ等はそのまま進めてよい。

以下が新たに必要になった場合は**実装を止め、プランを改訂してユーザー再承認**を取る:

- ユーザーから見える挙動・仕様変更
- 当初予定していない機能追加/削除
- 公開API / CLI / DBスキーマ / 永続化形式の変更
- 新しい外部依存パッケージ/サービスの導入
- アーキテクチャや主要責務の変更
- 大規模リファクタ
- セキュリティ方針・権限モデルの変更
- 当初予定外の範囲へ広く変更が波及

ユーザーが途中で追加要望を出した場合も同じ基準で扱う。軽微なら現在タスクへ取り込み、上記に該当すれば新しいプランと再承認を要求する。

## 8. Claudeによる実装介入

Lunaが主実装者だが、Claudeが「直接直す方が速い・確実・適切」と判断した場合は制限なく編集してよい。Claudeが編集した変更もSolレビュー対象になる。

ユーザーがIDE等から途中編集した変更も同様に現在のworking treeの一部として扱う。

## 9. Solレビュー

実装・検証後、毎回**新しいSol thread**を `codex` で開始する。前回threadを継続しない。前回指摘の修正確認が重要なら、必要な過去レビューMarkdownだけ参照させる。

レビューCodex設定のデフォルト:

- `model`: `gpt-5.6-sol`
- `cwd`: project root
- `sandbox`: `workspace-write`
- `approval-policy`: `never`
- network access: enabled

レビュー前にstateを `reviewing` へ更新する。

### Solへの必須指示

- ソースコードや通常設定ファイルを**変更しない**。
- `.codex-loop/` 配下だけは、レビューMarkdown・スクリーンショット・補助資料の保存に使ってよい。
- staged / unstaged / untracked を含む**現在のGit変更全体**をレビューする。開始前変更や作者を区別しない。
- `.gitignore` 対象と明らかな生成物/キャッシュ/一時ファイルは除外してよい。
- 必要に応じてビルド・テスト・lint・静的解析を自分で実行して根拠を確認する。
- UI変更ではスクリーンショット確認を推奨するが必須ではない。
- レビューは自然なMarkdown。各指摘に `Critical / High / Medium / Low` の重要度を付ける。基準は目安で、内容と根拠を優先する。
- 実害のある問題を優先: correctness、回帰、security、data integrity、error handling、concurrency、明確なperformance問題、テスト不足。
- 命名、formatting、style、主観的な設計好みだけの指摘は控える。
- 各指摘は、問題点・根拠・影響・必要なら修正方向を簡潔に書く。
- 問題がなければ明示的に「修正すべき指摘なし」と書く。
- レビュー結果を指定された `reviews/YYYYMMDD-HHMMSS-レビュー.md` に保存する。
- 秘密情報を意図的に読まない/表示しない。外部副作用ルールも実装担当と同じ。

レビュー作成後、`add-review` で `state.json` に登録する。レビュー本文のハッシュ検証は不要。

## 10. Claude + Fableによるレビュー査定

Solの生レビュー全文を通常の会話へそのまま貼らない。原文は `.codex-loop/.../reviews/` に残す。

Claudeは実コード・テスト結果・レビューを読み、必要に応じてAdvisorへ相談し、各指摘の妥当性と修正必要性を検証する。

### 修正が必要な場合

1. ユーザー向けには、Sol全文ではなく**整理した判断結果と修正プラン**を提示する。
2. `plans/YYYYMMDD-HHMMSS-修正プラン.md` を新規作成する。
3. `add-plan --kind fix` で登録する。
4. **明示承認があるまで修正しない。**

### 修正不要と判断したがSol指摘が残る場合

Claude + Advisorが「誤検知、既存仕様上問題なし、スコープ外、費用対効果が悪い、修正でリスク増」等で対応不要と判断しても、**勝手に終了しない**。判断理由を要約して「このまま終了してよいか」ユーザーへ明示確認する。

ユーザー承認後 `add-approval` で completion approval を記録し、最終報告へ進む。

### Solが「修正すべき指摘なし」の場合

Claude + Advisorで最終確認する。問題がなければ追加のユーザー確認なしで最終報告・完了へ進んでよい。

## 11. 修正プラン承認後

1. `approve-plan` でSHA-256と承認履歴を保存。
2. `verify-plan`。
3. 原則として既存Luna threadへ `codex-reply` し、修正プランの正本ファイルを読ませる。
4. 文脈が長すぎる・混乱している・新規threadが安全とClaudeが判断した場合は新しいLuna threadを開始し、`set-luna` で差し替える。
5. 修正後はビルド/テストを行い、新しいSol threadで再レビューする。
6. 固定回数で打ち切らない。Claude + Advisorが「残りは対応不要」と判断できるまで反復する。ただし残指摘ありで終了する場合は必ずユーザー確認を取る。

## 12. MCP障害と `codex exec` フォールバック

Codex MCPの一時障害は設定回数まで再試行する。復旧しない場合、**自動で `codex exec` へ切り替えない**。

ユーザーへ以下を選ばせる:

1. MCP復旧まで停止
2. `codex exec` で継続

`codex exec` を選んだ場合は `state.json.executionMode` を `codex-exec` に記録し、可能なら `codex exec resume <SESSION_ID>` で実装文脈を継続する。利用できない/安全に対応できない場合は新規セッションで承認済みプランと現状コードを読み直す。レビューは引き続き毎回新規セッションを原則とする。

## 13. 秘密情報

AI自身は秘密値を読み取らないことを原則とする。

禁止/回避例:

- `.env` を丸ごと読む
- `printenv` / `env` / `set` 等で全環境変数を表示
- APIキー、トークン、接続文字列、秘密鍵をecho/ログ出力
- `.codex-loop/` へ秘密値を書き出す

プログラムが環境変数を内部参照してテストやAPI呼び出しを行うのは可。秘密値がAIのプロンプト/出力へ露出しないよう、quiet/maskedログや限定的なコマンドを優先する。秘密漏洩の可能性が高いコマンドだけ事前にユーザーへ確認する。

## 14. Gitコミット

通常の `/codex-loop` はコミットしない。ユーザーが明示的に「コミットして」等と頼んだ場合のみClaudeまたはLunaが実行してよい。

`.codex-loop/` は `.gitignore` 対象であり、コミットに含めない。

## 15. pause / cancel / resume

- `pause`: `status=paused`, `phase=paused` とし、次アクションを記録。
- `cancel`: `status=cancelled`, `phase=cancelled`。記録は削除しない。
- `resume`: `paused` または明示指定された `cancelled` を `active` に戻し、直前の実作業フェーズへ復元する。復元先が曖昧ならstate内のlatest plan/reviewと `nextAction` を読んで判断し、必要ならユーザーへ質問する。
- `completed` は履歴として残す。

`.codex-loop/` 内のファイルは自動削除しない。

## 16. 最終報告

完了時はまず会話で**簡潔な結果**を伝える:

- 実装結果
- ビルド/テスト結果
- 残存課題または「特になし」

その後 `reports/YYYYMMDD-HHMMSS-最終報告.md` に詳細を保存する。詳細報告には必要に応じて以下を含める:

- 実装内容
- 主な変更領域
- 実行した検証と結果
- Solレビュー概要
- 採用した指摘と対応
- 採用しなかった指摘と理由
- 残存リスク/既知課題
- 重要なユーザー承認・判断
- 関連するplan/reviewファイルへの参照

保存後 `add-report --complete` で `status=completed`, `phase=completed` にする。

## 17. レビューMarkdownの推奨形式

Solには概ね次の形式を使わせる。完全一致は不要。

```markdown
# コードレビュー

## Summary
- レビュー範囲
- 実行したテスト/ビルド
- 総評

## Findings

### High — <短いタイトル>
- 対象: `path/to/file`（必要なら行付近）
- 問題: ...
- 根拠: ...
- 影響: ...
- 修正方向: ...

## Verification
- `dotnet test ...`: PASS

## Conclusion
- 修正すべき指摘なし
```

指摘ゼロの場合に空のFindingsを無理に作らない。
