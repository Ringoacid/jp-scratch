# プロンプト検証アプリ

Gemini の校正精度と文体保護を比較し、JP Scratch 本体と共有する校正実装を回帰検査する
コンソールアプリ。
既定のテストセットは、誤り検出5本と文体保護25本を `cases.json` に収録している。
既定のプロンプト案は、文脈を含む全文を渡して修正版全文を受け取る `full-rewrite-safe`。

## 実行

PowerShell で API キーを現在のプロセスだけに設定して実行する。

```powershell
$env:GEMINI_API_KEY = "..."
dotnet run --project PromptValidation
```

API キーは環境変数からのみ読み、引数・ログ・レポートには出力しない。精度比較のライブ実行は
現在 Gemini (`GEMINI_API_KEY`) を対象とし、OpenAI (`OPENAI_API_KEY`) の Responses API クライアントは
`--self-test` のローカルHTTPスタブで回帰検査する。

主な実行例:

```powershell
# 1ケースだけ検証
dotnet run --project PromptValidation -- --case typo-01

# 任意の文章を試す
dotnet run --project PromptValidation -- --input "この文章を校正して"

# APIを呼ばず、実際のプロンプトとJSON Schemaを確認
dotnet run --project PromptValidation -- --dry-run --case style-01

# 提案位置・全文差分・資格情報・料金/ログ/為替・DB移行をオフライン検査
dotnet run --project PromptValidation -- --self-test

# 保存済みの全文応答から個別提案を抽出（APIは呼ばない）
dotnet run --project PromptValidation -- --analyze-results PromptValidation/results

# 結果をJSONでも保存
dotnet run --project PromptValidation -- --output prompt-results.json

# プロンプト案を比較（各ケース10回）
dotnet run --project PromptValidation -- --suite error --repeat 10 --variant phrase-span

# 1回の実行に費用上限を設定
dotnet run --project PromptValidation -- --max-cost 0.25
```

終了コードは、全件合格なら `0`、検証失敗なら `1`、設定・通信エラーなら `2`。
各API呼び出しと集計には、`usageMetadata` と現行単価に基づく推定USD料金を表示する。

プロンプト案:

- `full-rewrite-safe`（既定）: 全文の修正版を返す。文書境界と文中命令の無視を明示。
- `full-rewrite`: ユーザー提示の簡潔な全文修正版方式。
- `phrase-span`: 修正箇所ごとに語句・文節全体の置換JSONを返す。
- `minimal-diff`: 修正箇所ごとに最小差分JSONを返す。
- `current`: 初期の置換JSONプロンプト。

## 合否判定

- `error`: モデルの提案を原文へ適用した結果、`expectedChanges` の修正後候補（`to` 配列）の
  いずれかが存在し、修正前文字列が残っていなければ合格。
- `style`: 有効な提案が1件もなければ合格。口語などへの提案は文体保護違反として表示。
- モデルの `original` と前後文脈から原文位置を解決できない提案は破棄し、失敗として集計。
- 全文方式では、期待修正に加えて前後文脈の完全な保持、異常な長さ変化、文体例との完全一致を検査。

`cases.json` は編集可能。ラフな文章は実際のユーザー文体に置き換えたり追加したりして評価する。

## 全文から個別提案への変換

本体の `Proofreading/DocumentDiff.cs` を検証プロジェクトからリンクして使用する。
`DocumentDiff` は原文とモデルの修正版全文を Unicode の書記素単位で比較し、各変更を
AvalonEdit と同じ UTF-16 オフセットの局所置換へ変換する。絵文字・結合文字を途中で分割せず、
同じ語の中で最大2書記素だけ離れた変更は1提案へまとめる。脱字のような純粋な挿入は、
隣接1書記素を含む置換にして編集追従可能な範囲を持たせる。

全提案を原文に再適用して修正版を再現できない場合、変更範囲が重なる場合、または変更量が
安全上限を超える場合は応答全体を破棄する。詳細と検証結果は
`algorithm-validation-2026-07-29.md` を参照。

`ProofreadingSession` も同じ方法でリンクし、`--self-test` で TextAnchor の前方編集追従、
範囲編集時の失効、境界挿入、複数提案の順次適用を検査する。
複数提案については、本文オフセットからの個別選択と前後への循環移動も検査する。
本体の `CredentialService` もリンクし、Gemini / OpenAI のキーの別保存、DPAPI暗号化、再読込、削除、
破損ファイルの検出を一時ディレクトリだけで検査する。実際のAPIキーや外部APIは使用しない。
`PricingService` もリンクし、`pricing.json` の初回生成、Gemini / GPT-5.6 Luna のモデル別 `decimal` 料金計算、既定モデルの補完、
破損ファイルの隔離を一時ディレクトリだけで検査する。
`ApiCallRepository` もリンクし、`api_calls` の全列、Invariant CultureによるUSD/JPY文字列、既存JPY列のNULL、
成功・error・timeout・破棄済みを含む精密decimal集計、全期間・セッション相当・当日・当月の日時境界、
DSTで重複するローカル時刻を含む`DateTimeOffset`比較、最後に挿入した有効ログの取得を一時SQLiteだけで検査する。出力トークンは候補出力と推論トークンの合計を
課金対象として本体側で記録する。
`FxRateService` はローカルHTTPスタブだけで、当日・週末のキャッシュ再利用、期限切れ時のupsert、
HTTP/timeout/不正JSONの古いキャッシュへのfallback、空キャッシュ時のnull、同時取得の一本化を検査する。
失敗後の同日再呼出・再起動相当の別serviceでもHTTPが増えないこと、翌日の再試行も検査する。
`DatabaseMigrationValidation` はDB v3の`app_metadata`を含む移行を一時SQLiteだけで検査する。したがって
`--self-test` は、料金・ログ・為替を含めて外部APIを一切呼ばない全オフライン検証である。
採用したシステム指示は本体の `ProofreadingPrompt` を共有し、検証版とのずれを防ぐ。
本体の `GeminiProofreadingClient` と `OpenAiProofreadingClient` もリンクし、ローカルHTTPスタブでリクエスト形式、
前後文脈タグ、`usageMetadata`、差分生成、1回だけの再試行、恒久エラー、タイムアウト、
キー未設定を検査する。`ParagraphProofreadingPlanner` の段落境界、変更検出、選択範囲、
書記素を壊さない2,000文字分割もAPIを呼ばずに検査する。
理由つき別案の専用プロンプトもローカルHTTPスタブで検査する。リアクションDBは一時SQLiteを使い、
スキーマ移行、保存、理由候補の順位、本文をUndoしても記録が残ることを検査する。
`ProofreadingSchedule` は時刻を固定してデバウンス、最小送信間隔、タブごとの待機状態を検査する。
複数の段落・分割リクエストから返った局所差分を送信時点の全文へ戻せることも検査する。

全文修正版には提案ごとのカテゴリ・理由が含まれない。固定表示を置く意味もないため、
本体の提案モデルには保持せず、UIにも表示しない。分類・理由生成だけを目的とする
追加 API 呼び出しは行わない。
