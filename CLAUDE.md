# CLAUDE.md

WSL や一部アプリで日本語が打てない問題を解消する、Windows 常駐型の日本語スクラッチパッド。
Gemini / GPT-5.6 Luna による誤字脱字校正と、文体を壊さない学習機能を備える。
- 仕様の正典: [requirements.md](requirements.md)（全 8 章）。判断に迷ったらまずこれを読む。
- 使い方とビルド: [README.md](README.md)
- 使用モデル: [gemini-3.5-flash-lite.md](gemini-3.5-flash-lite.md) / [gpt-5.6-luna.md](gpt-5.6-luna.md)

## 現在地
| 段階 | 内容 | 状態 |
|---|---|---|
| v1 | 常駐エディタ（コールド 0.63s / 常駐 21.9MB / MSI 1.8MB） | **完了** |
| v2 | Gemini / GPT-5.6 Luna による校正 | **完了**（実機確認済み） |
| v3 | 文体の学習（few-shot・スタイルガイド・拒否率推移） | **完了**（実機確認済み。キャッシュは意図的に見送り） |

## コマンド
```powershell
dotnet build / dotnet run                            # ビルド / 実行
powershell -File tools\smoke-test.ps1 publish\fdd\JpScratch.exe   # 煙テスト（%APPDATA%\JpScratch を消してから）
powershell -File installer\build.ps1                  # MSI（要 WiX v5）
dotnet run --project PromptValidation -- --seed-billing <隔離dir> [--bulk] [--force]  # 課金画面の目視確認用データ投入
```

## 構成
```
App.xaml.cs / Infrastructure / Models / Services / Editor / Controls /
Views / Themes / Proofreading / PromptValidation / installer
```

## 壊しやすい不変条件（触る前に理由まで理解する）
- **本文は必ずプレーンテキスト。** 書き込みは一時ファイル → `File.Replace`（`AtomicFile.cs`）。
- **ゴミ箱移動/復元は「ファイル操作を先、DB更新を後」。**
- **本文が読めないとき `LoadBody` は例外**（空文字で開いて上書きしない）。
- **DB行→モデルは `Parse` でなく `TryParse`**（壊れた1行で全体を落とさない）。
- **`Migrate()` の新DDL は `IF NOT EXISTS`。** 版を上げたら `PromptValidation` の `CurrentVersion` も更新。
- **テーマ辞書は `ThemeService` だけが差し込む。** 既定テンプレートは色指定を無視 → `Styles.xaml` で差し替え。
- **ウィンドウ位置は物理ピクセル。IME 変換中は自動非表示を止める。**
- **許可による本文置換は `CarryForwardAppliedEdit` と `_applyingProposal` の対。** 一括許可の `RunUpdate` は `try` の内側に。
- **校正実行中の破棄は `TextAnchor` でリクエスト単位に判断。** 本文編集で未送信分は中止。
- **送信済みの記録はパート（1リクエスト）単位。** 段落単位だと、2,000字超の段落で前半成功・後半失敗のとき課金済みの前半まで再送される。
- **文書分割は常に空行区切り。自動校正の既定デバウンス 5000ms。**
- **数値入力欄は表示→パースの往復不変性を確認。** 未編集欄は元の値をそのまま書き戻す。
- **描画層へ渡すのは不変スナップショット。** 取り消し線は `DrawingContext` で手描き（`TextDecoration` は空白で描けない）。
- **進行中フラグを `true` にする行と `try`/`finally` の間には何も置かない。** モーダル表示・`BeginUpdate()` も「何か」に含む。課金確認ダイアログはフラグを立てる**前**に出してはいけない（WPF のモーダルは入れ子のメッセージループを回すため、タイマーが発火して二重送信・二重課金になる）。
- **`AtomicFile` の失敗は上位でタブ/ファイル単位に隔離し、必ずユーザーへ通知する。** 1件の共有違反で他タブの保存を巻き込まない。読めなかったものは既定値・空文字で上書きしない。
- **`SettingsService.IsReadFailed` の起動では、設定値に依存する破壊的処理を一切走らせない**（ゴミ箱の期限削除・課金明細の圧縮・スタートアップ登録）。一時的な読み取り失敗が不可逆な削除・圧縮・設定変更に化ける。
- **「保存できない」を知らせるだけで終わらせない。** 上限付きバックオフで自動再試行し、終了時は「再試行/このまま終了/取りやめ」を選ばせる。隠れている間の通知はステータスバーでなくトレイへ。
- **ユーザーが編集しうるテキストファイルの読み取りは strict UTF-8（`AtomicFile.TryReadAllText`）。** 既定のデコーダは不正バイトを U+FFFD へ黙って置換するため、CP932 保存されたファイルが「読めた」ことになり、次の保存で原本を壊す。
- **`IsDirty` を落とすのは本文とメタ情報の両方を書き切ってから。** 片方で落とすと、失敗したタブが未保存扱いから外れて通知にも再試行にも乗らない。

## 環境の癖
- **PowerShell スクリプトは UTF-8 BOM 付きで保存**（無いと日本語が CP932 化して壊れる）。
- **Gemini へ送るシステム指示の改行は LF へ正規化。**
- **`New-Object` より `[Type]::new()` を使う。**
- **画面キャプチャ不可** → 見た目・実キー入力・IME は自動検証できないので実機確認を依頼する。
- **`JPSCRATCH_DATA_DIR` で開発用データディレクトリを隔離**（未設定なら現行と完全に同一）。

## 開発時の課金 API 利用ルール
- **実 API を呼ぶ前に必ずユーザーへ確認。** 実行後はトークン数と推定料金を提示する。
- ビルド・自己テスト・ドライラン（`PromptValidation --self-test`）は確認不要。
- ユーザー所有の未追跡 `.claude/` は変更・コミット対象にしない。
