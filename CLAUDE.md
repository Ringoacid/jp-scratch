# CLAUDE.md

このファイルは Claude Code がこのリポジトリを操作する際のガイドです。

## プロジェクト概要

- Windows 11 向け常駐型日本語スクラッチパッド「JP Scratch」。ホットキー（既定 `Alt+Space`）でどこからでも呼び出せるタスクトレイ常駐メモ帳。
- 本文は `%APPDATA%\JpScratch\tabs\{id}.txt` に UTF-8 BOM なしのプレーンテキストで自動保存（メモ帳でサルベージ可能であることが要件）。
- v1（常駐エディタ）・v2（校正＋課金管理）・v3（文体の学習＝few-shot・スタイルガイド自動生成・カスタム指示）・v4（プロバイダー拡張）はすべて実装済み。残るコンテキストキャッシュ適用は Gemini の単価が未確認のため意図的に見送り中。

## 技術スタック

- C# / .NET 10 (LTS)、WPF、AvalonEdit 6.x、SQLite (Microsoft.Data.Sqlite)。
- WinForms はトレイアイコンのためだけに参照（暗黙 using は外してあり `TrayIconService` のみが明示的に using）。
- 校正モデルは 4 プロバイダー 12 モデル（Google / OpenAI / Anthropic / Preferred Networks）。**自動用と手動用の 2 枠**を持ち、既定は自動 `gpt-5.6-luna` / 手動 `claude-sonnet-5`。API キーはプロバイダーごとに DPAPI 暗号化で `credentials.dat` に保存。

## ビルド・テスト

```powershell
dotnet build    # デバッグビルド / dotnet run で実行
powershell -File tools\smoke-test.ps1 publish\fdd\JpScratch.exe   # 煙テスト（%APPDATA%\JpScratch を消すので注意）
dotnet run --project PromptValidation -- --self-test   # オフライン回帰テスト（外部 API 不使用）
dotnet run --project PromptValidation -- --model-benchmark   # 全モデル比較（実課金。--self-test には入れない）
python tools/plot-model-benchmark.py   # 上の結果から README 用の比較図を生成
```

- `PromptValidation/` は本体とソースを共有する独立コンソールアプリ。校正ロジック変更時は必ず `--self-test` を通す。
- 状態アイコン: `tools\build-tray-icons.py`（`--check` で差分確認のみ）。MSI: `installer\build.ps1`（WiX v5 固定）。

## ディレクトリ構成

- `App.xaml.cs` 起動・常駐・単一インスタンス・クラッシュ時保存
- `Controls/` 検索・置換パネル / `Editor/` AvalonEdit の拡張（検索・不可視文字・校正提案の描画）
- `Infrastructure/` Win32 相互運用・原子的ファイル書き込み・パス解決
- `Models/` 設定・タブ・ホットキー / `Proofreading/` 校正クライアント・プロンプト・段落計画・差分・提案セッション
- `Services/` 設定・SQLite・資格情報・リアクション・タブ・ホットキー・配置・テーマ・トレイ
- `Themes/` ライト / ダーク / 共通スタイル / `Views/` メインウィンドウ・設定・全タブ検索・ダイアログ
- `installer/` WiX による MSI / `tools/` 煙テスト・アイコン生成スクリプト / `PromptValidation/` 検証アプリ

## 重要な実装上の注意

- **ウィンドウ位置は物理ピクセルで扱う**（`Services/WindowPlacer.cs`）。混在 DPI では WPF の `Window.Left/Top` を使わず `SetWindowPos` を直接呼ぶ。
- **WinForms の暗黙 using を追加しない**（`Brush` / `Point` / `KeyEventArgs` が WPF 側と全面衝突する）。
- **IME 変換中は自動非表示を止める**（`NativeMethods.HasImeComposition`）。
- **テーマ辞書は `ThemeService` だけが差し込む**。App.xaml で読むと二重マージになり切り替えが効かない。
- **二重起動の呼び戻しは名前付きイベント**（`Infrastructure/SingleInstance.cs`）。`PostMessage(HWND_BROADCAST)` は不可。
- **`Assets/app.ico` は手作りの正典**。小サイズは DIB、256px のみ PNG 圧縮（`NotifyIcon` が PNG を展開できないため）。
- **PowerShell スクリプトは UTF-8 BOM 付きで保存**（BOM なしだと CP932 として読まれコメントが壊れる）。
- **打ち切り・拒否の検出は `ProofreadingClientBase.EnsureCompleted` が正典**。本文抽出より前に必ず走る。切れた応答を採用すると「本文末尾を削除する提案」に化け、安全検査を通過して一括許可で本文が消える。プロバイダーを足したら `ProviderCompletionGuardValidation` の表にも行を足す。
- **タイムアウトでは再試行しない**（二重課金と待ち時間の倍化を避ける）。再試行は 429 / 5xx のみ。
- **モデルごとの差異は `ProofreadingModelCatalog` の表に持たせる**（単価・推奨タイムアウト・用途別の思考量・プロバイダー）。if 分岐を増やさない。
- **`pricing.json` の `currency` は編集で落とさない**（省略時 USD。円建ての PLaMo が $ 扱いになると桁が狂う）。
- **コピー時の HTML 形式は捨てる**（`MainWindow` が `DataObject.SettingData` で `DataFormats.Html` をキャンセル）。AvalonEdit の HTML フラグメントは行間が `<br>` ＋生の改行のため、HTML を優先する貼り付け先（Google Chat など）で行間が倍になり前後にも空行が入る。
- 書き込みは一時ファイル→`File.Replace`（`Infrastructure/AtomicFile.cs`）。SQLitePCLRaw は脆弱性対応で 2.1.12 に固定。

## 作業の進め方

- **ブランチは切らない**。指示がない限り `main` に直接コミットする（このプロジェクトは単独開発でレビュー工程がないため、ブランチを作ってもマージ待ちが増えるだけ）。
- ブランチを切る必要があると判断した場合は、**作る前に必ず確認を取る**。

## コーディング規約

- UI 文言はすべて日本語ハードコード。サテライトリソースは使わない。
- 校正プロンプトは `Proofreading/ProofreadingPrompt.cs` が正典。本体と検証アプリで共有する。
- 変更後は `dotnet build` と `PromptValidation --self-test` で確認する。

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
