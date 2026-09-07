# Graph Report - jp-scratch  (2026-09-07)

## Corpus Check
- 213 files · ~215,129 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2953 nodes · 6462 edges · 151 communities (128 shown, 23 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 452 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `c8c3d1ce`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- .CreateAutomaticPlan
- PricingService
- FxRateService
- FindReplacePanel
- .Main
- Window
- NativeMethods
- Window
- ProofreadingInlineDiffGenerator
- capture-docs-screenshots.py
- GeminiProofreadingClientValidation
- StatusBarUsageFormatterValidation
- MissedCorrectionDialog
- .BuildRows
- ProofreadingProposal
- ApiCallRepository
- MainWindow
- ApiProvider
- .Execute
- ResourceDictionary
- JpScratch.Services
- 校正 UX・自動校正・課金表示 改修計画
- measure-performance.py
- HideSuppressionCounter
- FxRateCompletionValidation
- .LoadFrom
- ScratchTab
- PricingHistoryChart
- ReactionRepository
- ProofreadingModelCatalog
- ProviderCompletionGuardValidation
- .AlternativeWithReasonMenuItem_Click
- .RunBenchmarkAsync
- SettingsFieldFormattingValidation
- UsageLimitServiceValidation
- gui-regex-replace-test.py
- ProofreadingPrompt
- Window
- TrashWindow
- JpScratch.Views
- Window
- JpScratch.Models
- Database
- AppSettings
- screenshot-main.py
- SingleInstance
- BackupRestoreService
- .TotalsSurviveCompaction
- App
- TabRepository
- HotkeyCapture
- TrayIconService
- HotkeySpec
- .SelectProposal
- SettingsWindow
- Japanese Commit Message
- build-tray-icons.py
- plot-model-benchmark.py
- Q: model-benchmark-barsなども更新してください。
- Window
- .RecordFailedApiCall
- .RunSelfTestAsync
- GeminiUsage
- .ProofreadAsync
- .RunProofreadingAsync
- IdeographicSpaceColorizer
- Window
- .RunSelfTests
- ApiUsageCost
- .FormatInclusive
- Window
- AnthropicProofreadingClient
- PlamoProofreadingClient
- gui-settings-test.py
- .CheckLatestAsync
- BillingHistoryWindow
- .Create
- GPT 6 Astra 追補ベンチマーク（2026-09-07）
- GeminiProofreadingClient
- OpenAiProofreadingClient
- UsageAccumulator
- CLAUDE.md
- .BuildRejectionTrendRow
- AppPaths
- サードパーティー通知（THIRD-PARTY NOTICES）
- .RunAsync
- PricingHistoryEditDialog
- graphify query
- .Read
- CrossTabSearchWindow
- Q: Gitの変更をレビューしてください。
- TabManager
- 3.5.1 校正 API の共通契約
- 要件定義書 — 常駐型 日本語スクラッチパッド（仮称: JP Scratch）
- InverseBooleanToVisibilityConverter
- RelayCommand
- PromptValidation
- .OnStartup
- モデル仕様書: Gemini 3.5 Flash-Lite
- jp-scratch
- Prompt Validation App README
- ThemeService
- JP Scratch
- JpScratch.Editor
- JP Scratch Settings
- smoke-test.ps1
- TabRoot
- ProofreadingClientBase
- 3.3 校正機能
- Gemini 3.7 Flash 追補ベンチマーク（2026-08-21）
- 5. マイルストーン
- graphify skill
- graphify reference: add-watch
- Model Performance Metrics
- Q: 全タブ検索から、ごみ箱のタブを復元できるようにしてください。
- Q: モデルに更新が必要か調べてください。（新しいモデルや料金など）
- graphify reference: exports
- Cross-Tab Search UI
- Main Editor UI (Dark Mode)
- 3. 機能要件
- 3.2 エディタ
- Dark.xaml
- Light.xaml
- TrashListItem
- Graphify Skill
- Context Menu UI
- Model Benchmark Bar Chart (Light)
- Proofreading Suggestion UI
- PromptValidation
- 3.4 学習機能（文体の適応）
- 3.5 API 連携
- Window
- graphify reference: commit hook and native CLAUDE.md integration
- graphify reference: incremental update and cluster-only
- モデル仕様書: GPT-5.6 Luna
- graphify reference: GitHub clone and cross-repo merge
- graphify reference: transcribe video and audio
- TrayIconStateValidation
- JpScratch.Infrastructure
- Q: レビューで見つかった4件を修正し、PromptValidationの有料API実行ルールをAGENTS.mdへ追加する
- Q: 現在のGit変更に適したコミット範囲を判断する
- FontResolver
- Q: 次のコミットメッセージを作成してください。
- Q: graphify-out の生成物のコミットメッセージを作成してください。
- Q: 補助ファイルについて教えてください。削除しても大丈夫なものですか？
- ReleaseInfo.cs
- .OnExit

## God Nodes (most connected - your core abstractions)
1. `MainWindow` - 178 edges
2. `Window` - 98 edges
3. `SettingsWindow` - 96 edges
4. `JpScratch.Services` - 84 edges
5. `JpScratch.PromptValidation` - 51 edges
6. `PricingService` - 47 edges
7. `ScratchTab` - 43 edges
8. `Database` - 42 edges
9. `TabManager` - 39 edges
10. `Window` - 38 edges

## Surprising Connections (you probably didn't know these)
- `Billing History UI` --conceptually_related_to--> `Model Benchmark (2026-08-06)`  [INFERRED]
  docs/images/billing-history.png → PromptValidation/model-benchmark-2026-08-06.md
- `Proofreading Settings UI` --conceptually_related_to--> `Model Benchmark (2026-08-06)`  [INFERRED]
  docs/images/settings-proofreading.png → PromptValidation/model-benchmark-2026-08-06.md
- `Model Benchmark (2026-08-06)` --references--> `Model Benchmark Scatter Plot (Dark)`  [EXTRACTED]
  PromptValidation/model-benchmark-2026-08-06.md → docs/images/model-benchmark-scatter-dark.png
- `App` --inherits--> `Application`  [EXTRACTED]
  App.xaml.cs → App.xaml
- `App` --references--> `SingleInstance`  [EXTRACTED]
  App.xaml.cs → Infrastructure/SingleInstance.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Graphify Query & Feedback Loop** — claude_skills_graphify_references_query_md_graphify_query, claude_skills_graphify_references_query_md_query_expansion, claude_skills_graphify_references_query_md_save_result, claude_skills_graphify_references_query_md_graphify_reflect [EXTRACTED 0.90]
- **Graphify Skill Documentation** — claude_skills_graphify_references_add_watch, claude_skills_graphify_references_exports, claude_skills_graphify_references_extraction_spec [EXTRACTED 1.00]
- **Model Benchmarking Visualizations** — docs_images_model_benchmark_bars_dark, docs_images_model_benchmark_bars_light, docs_images_model_benchmark_scatter_light [EXTRACTED 1.00]
- **Model Evaluation & Benchmarking** — promptvalidation_model_benchmark_2026_08_06, docs_images_model_benchmark_scatter_dark, docs_images_billing_history [EXTRACTED 1.00]
- **Proofreading Validation Pipeline** — promptvalidation_readme, promptvalidation_validation_2026_07_29, promptvalidation_validation_2026_07_29_round2, promptvalidation_algorithm_validation_2026_07_29 [EXTRACTED 1.00]
- **Settings UI Tabs** — docs_images_settings_general, docs_images_settings_editor, docs_images_settings_learning, docs_images_settings_billing [EXTRACTED 1.00]
- **Proofreading Subsystem** — proofreading_proofreadingclientbase, proofreading_proofreadingmodelcatalog, proofreading_proofreadingprompt [INFERRED 0.85]

## Communities (151 total, 23 thin omitted)

### Community 0 - ".CreateAutomaticPlan"
Cohesion: 0.06
Nodes (31): CurrentPartStart, ParagraphProofreadingPlannerValidation, ProofreadingDispatchPlannerValidation, End, HashSet, int, IReadOnlyList, IReadOnlySet (+23 more)

### Community 1 - "PricingService"
Cohesion: 0.08
Nodes (28): AtomicFile, FileReadFailure, AtomicFileValidation, Action, Dictionary, PricingServiceValidation, ReadOnlySpan, DateOnly (+20 more)

### Community 2 - "FxRateService"
Cohesion: 0.08
Nodes (28): CancellationToken, DateOnly, DateTimeOffset, Func, HttpRequestMessage, HttpResponseMessage, Task, FxRateServiceValidation (+20 more)

### Community 3 - "FindReplacePanel"
Cohesion: 0.05
Nodes (42): CloseButton, CrossTabButton, InSelectionCheck, MatchCaseToggle, MatchCountText, NextButton, PrevButton, RegexToggle (+34 more)

### Community 4 - ".Main"
Cohesion: 0.06
Nodes (34): Options, IReadOnlyList, List, Evaluator, CancellationToken, decimal, HttpClient, HttpResponseMessage (+26 more)

### Community 5 - "Window"
Cohesion: 0.04
Nodes (87): EffectiveFromText, InputDeltaText, InputText, Name, OutputDeltaText, OutputText, SourceText, StatusText (+79 more)

### Community 6 - "NativeMethods"
Cohesion: 0.07
Nodes (27): APPBARDATA, HwndSource, DllImport, int, IntPtr, MarshalAs, APPBARDATA, MONITORINFO (+19 more)

### Community 7 - "Window"
Cohesion: 0.06
Nodes (34): BoolToCollapsed, BoolToVisible, IsActive, IsEditing, Title, AcceptAllProposalsButton, AcceptProposalButton, FindPanel (+26 more)

### Community 8 - "ProofreadingInlineDiffGenerator"
Cohesion: 0.06
Nodes (33): CultureSpecificCharacterBufferRange, bool, Brush, double, DrawingContext, IReadOnlyList, Point, TextAlignment (+25 more)

### Community 9 - "capture-docs-screenshots.py"
Cohesion: 0.08
Nodes (46): activate_first_tab(), app_is_running(), AppSession, BITMAPINFO, BITMAPINFOHEADER, build_tabs(), _capture(), capture_window() (+38 more)

### Community 10 - "GeminiProofreadingClientValidation"
Cohesion: 0.22
Nodes (10): CancellationToken, Func, HttpClient, HttpRequestMessage, HttpResponseMessage, string, Task, GeminiProofreadingClientValidation (+2 more)

### Community 11 - "StatusBarUsageFormatterValidation"
Cohesion: 0.05
Nodes (23): Encoding, StatusBarCurrencyFormat, ApiUsageDisplayFormatterValidation, List, BillingCsvExporterValidation, DateTimeOffset, BillingHistoryEmptyStateValidation, DateOnly (+15 more)

### Community 12 - "MissedCorrectionDialog"
Cohesion: 0.08
Nodes (20): MissedCorrectionActionValidation, int, MissedCorrectionAction, MissedCorrectionKind, MissedCorrectionPreview, TextDecorationCollection, CorrectedBox, ExecuteButton (+12 more)

### Community 13 - ".BuildRows"
Cohesion: 0.21
Nodes (9): Day1, Day2, MidDay, OldDay, DateOnly, DateTimeOffset, IReadOnlyList, BillingSeedCommand (+1 more)

### Community 14 - "ProofreadingProposal"
Cohesion: 0.06
Nodes (31): DiffKind, DiffOperation, DocumentChangeEventArgs, IReadOnlyList, JsonSerializerOptions, DocumentDiffValidation, Dictionary, double (+23 more)

### Community 15 - "ApiCallRepository"
Cohesion: 0.11
Nodes (25): DailyKey, DailyTotals, InClause, Name, Parameters, SeedRow, DateOnly, DateTimeOffset (+17 more)

### Community 16 - "MainWindow"
Cohesion: 0.09
Nodes (14): DataObjectSettingDataEventArgs, bool, Brush, DateOnly, DateTime, DateTimeOffset, decimal, DispatcherTimer (+6 more)

### Community 17 - "ApiProvider"
Cohesion: 0.13
Nodes (13): Action, byte, ApiKeySource, ApiProvider, CredentialServiceValidation, Func, int, string (+5 more)

### Community 18 - ".Execute"
Cohesion: 0.12
Nodes (10): StyleGuideRepositoryValidation, Action, DateTimeOffset, Func, IReadOnlyList, SqliteDataReader, string, StyleGuide (+2 more)

### Community 19 - "ResourceDictionary"
Cohesion: 0.07
Nodes (32): IsDropDownOpen, View.Columns, Arrow, Bd, Box, Check, Checked, CheckStates (+24 more)

### Community 21 - "校正 UX・自動校正・課金表示 改修計画"
Cohesion: 0.04
Nodes (47): 10.1 項目, 10.2 動作, 10.3 主な変更候補, 10.4 必須テスト, 10. エディタの右クリックメニュー, 11.1 自動テスト, 11.2 ビルド, 11.3 手動 UI 確認 (+39 more)

### Community 22 - "measure-performance.py"
Cohesion: 0.17
Nodes (22): click(), close_window(), ensure_no_app_running(), FILETIME, filetime_value(), find_window(), find_window_containing(), focus() (+14 more)

### Community 23 - "HideSuppressionCounter"
Cohesion: 0.33
Nodes (3): HideSuppressionCounterValidation, int, HideSuppressionCounter

### Community 24 - "FxRateCompletionValidation"
Cohesion: 0.16
Nodes (13): Clock, CancellationToken, DateOnly, DateTimeOffset, Func, HttpRequestMessage, HttpResponseMessage, List (+5 more)

### Community 25 - ".LoadFrom"
Cohesion: 0.09
Nodes (17): ComboBox, AutoModelFamilyCombo, AutoProofreadingModelCombo, CredentialProviderCombo, CredentialSourceCombo, FontCombo, ManualModelFamilyCombo, ManualProofreadingModelCombo (+9 more)

### Community 26 - "ScratchTab"
Cohesion: 0.16
Nodes (6): bool, DateTime, string, TextDocument, ScratchTab, IEnumerable

### Community 27 - "PricingHistoryChart"
Cohesion: 0.14
Nodes (16): Brush, DateOnly, double, DrawingContext, Func, IEnumerable, IReadOnlyList, Point (+8 more)

### Community 28 - "ReactionRepository"
Cohesion: 0.23
Nodes (6): FewShotCandidate, ReactionRepositoryValidation, IReadOnlyList, ProofreadingReaction, ReactionRepository, RejectionRateBucket

### Community 29 - "ProofreadingModelCatalog"
Cohesion: 0.14
Nodes (13): Automatic, Manual, DateOnly, Dictionary, IReadOnlyList, string, TimeSpan, CatalogPricingHistoryEntry (+5 more)

### Community 30 - "ProviderCompletionGuardValidation"
Cohesion: 0.05
Nodes (35): Body, Case, HttpMessageHandler, Label, FewShotSelectorValidation, CancellationToken, Func, HttpClient (+27 more)

### Community 31 - ".AlternativeWithReasonMenuItem_Click"
Cohesion: 0.13
Nodes (4): ProofreadingPurpose, RecordedApiCall, Exception, FailedApiCallRecord

### Community 32 - ".RunBenchmarkAsync"
Cohesion: 0.06
Nodes (33): BenchmarkOptions, CostUsd, Known, CancellationToken, HttpClient, int, IReadOnlyDictionary, IReadOnlyList (+25 more)

### Community 34 - "UsageLimitServiceValidation"
Cohesion: 0.16
Nodes (7): DateTimeOffset, UsageLimitServiceValidation, DateTimeOffset, string, UsageLimitNotificationTracker, UsageLimitService, UsageLimitState

### Community 35 - "gui-regex-replace-test.py"
Cohesion: 0.17
Nodes (22): Popen, assert_no_error_dialog(), class_name(), click(), dialog_details(), find_window(), focus(), main() (+14 more)

### Community 36 - "ProofreadingPrompt"
Cohesion: 0.20
Nodes (5): ProofreadingPromptV3Validation, IReadOnlyList, Regex, string, ProofreadingPrompt

### Community 37 - "Window"
Cohesion: 0.12
Nodes (18): CalledDateText, CompletableCountText, RateText, UncompletableCountText, ApplyButton, CancelButton, FetchButton, MessageText (+10 more)

### Community 38 - "TrashWindow"
Cohesion: 0.16
Nodes (8): ResultsList, KeyEventArgs, MouseButtonEventArgs, ObservableCollection, RoutedEventArgs, SelectionChangedEventArgs, TrashWindow, ListView

### Community 40 - "Window"
Cohesion: 0.07
Nodes (31): CalledAt, DiscardedCount, Duration, ErrorMessage, Jpy, Model, OutputTokens, PromptTokens (+23 more)

### Community 42 - "Database"
Cohesion: 0.11
Nodes (14): FileInfo, IDisposable, Lock, int, DatabaseMigrationValidation, string, TestStore, string (+6 more)

### Community 43 - "AppSettings"
Cohesion: 0.21
Nodes (7): decimal, IReadOnlyList, AppSettings, WindowPositionMode, DispatcherTimer, JsonSerializerOptions, SettingsService

### Community 44 - "screenshot-main.py"
Cohesion: 0.15
Nodes (19): BITMAPINFO, BITMAPINFOHEADER, capture_bitblt(), capture_print_window(), capture_window(), find_window(), is_blank(), main() (+11 more)

### Community 45 - "SingleInstance"
Cohesion: 0.12
Nodes (11): ActivateEventName, EventWaitHandle, Action, string, SingleInstance, Mutex, MutexName, int (+3 more)

### Community 46 - "BackupRestoreService"
Cohesion: 0.14
Nodes (10): AppVersion, IncludesCredentials, BackupRestoreServiceValidation, Func, int, long, string, BackupRestoreService (+2 more)

### Community 47 - ".TotalsSurviveCompaction"
Cohesion: 0.24
Nodes (5): DateTimeOffset, IReadOnlyList, BillingSeedCommandValidation, DateTimeOffset, ApiLogRetention

### Community 48 - "App"
Cohesion: 0.13
Nodes (12): Application, ApiKeySource, ApiProvider, bool, Exception, IEnumerable, IReadOnlyList, string (+4 more)

### Community 49 - "TabRepository"
Cohesion: 0.26
Nodes (5): TrashRepositoryValidation, DateTime, List, string, TabRepository

### Community 51 - "HotkeyCapture"
Cohesion: 0.23
Nodes (8): HookProc, bool, DllImport, HashSet, IntPtr, MarshalAs, ModifierKeys, HotkeyCapture

### Community 52 - "TrayIconService"
Cohesion: 0.23
Nodes (7): Icon, NotifyIcon, Dictionary, string, TrayIconService, TrayIconState, TrayIconStateResolver

### Community 53 - "HotkeySpec"
Cohesion: 0.14
Nodes (10): KeyboardFocusChangedEventArgs, Key, ModifierKeys, HotkeySpec, TextBox, CopyHideHotkeyBox, ToggleHotkeyBox, Key (+2 more)

### Community 54 - ".SelectProposal"
Cohesion: 0.17
Nodes (6): ContextMenuEventArgs, MouseWheelEventArgs, Editor, TabScroller, ScrollViewer, TextEditor

### Community 55 - "SettingsWindow"
Cohesion: 0.07
Nodes (18): PricingHistoryRow, ApiKeyBox, OkButton, bool, CancelEventArgs, DateOnly, Dictionary, Func (+10 more)

### Community 56 - "Japanese Commit Message"
Cohesion: 0.33
Nodes (5): Determine the scope, Japanese Commit Message, Propose and confirm, Safety boundaries, Stage, verify, and commit

### Community 57 - "build-tray-icons.py"
Cohesion: 0.17
Nodes (12): badge_triangle(), build(), draw_bar(), encode_dib(), main(), Image, 32bpp の DIB エントリ（BITMAPINFOHEADER + BGRA + AND マスク）を作る。 Pillow の ICO…, サイズ構成と「256px だけ PNG」を確認する。ここが崩れると NotifyIcon が絵を出せない。 (+4 more)

### Community 58 - "plot-model-benchmark.py"
Cohesion: 0.20
Nodes (19): chart_name(), default_reports(), draw_bars(), draw_scatter(), load(), main(), markdown_table(), pareto() (+11 more)

### Community 59 - "Q: model-benchmark-barsなども更新してください。"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: model-benchmark-barsなども更新してください。, Source Nodes

### Community 60 - "Window"
Cohesion: 0.15
Nodes (12): CostNoticeText, DescriptionText, ReasonBox, ReasonSuggestionBox, Window, RoutedEventArgs, SelectionChangedEventArgs, ProofreadingReasonDialog (+4 more)

### Community 61 - ".RecordFailedApiCall"
Cohesion: 0.39
Nodes (3): FailedApiCallRecord, TimeSpan, ProofreadingSendOutcome

### Community 62 - ".RunSelfTestAsync"
Cohesion: 0.18
Nodes (7): IReadOnlyCollection, Action, DateTimeOffset, ApiCallRepositoryValidation, StoredApiCall, decimal, CurrencyConversionValidation

### Community 63 - "GeminiUsage"
Cohesion: 0.19
Nodes (13): Exception, JsonElement, HttpStatusCode, TimeSpan, GeminiAlternativeResult, GeminiClientError, GeminiClientException, GeminiProofreadingResult (+5 more)

### Community 64 - ".ProofreadAsync"
Cohesion: 0.28
Nodes (8): CancellationToken, Func, HttpRequestMessage, HttpResponseMessage, string, Task, OpenAiProofreadingClientValidation, StubHandler

### Community 65 - ".RunProofreadingAsync"
Cohesion: 0.16
Nodes (7): ProofreadingScheduleValidation, DateTimeOffset, Dictionary, TimeSpan, ProofreadingSchedule, TextAnchor, TextDocument

### Community 66 - "IdeographicSpaceColorizer"
Cohesion: 0.29
Nodes (5): char, DocumentColorizingTransformer, DocumentLine, Brush, IdeographicSpaceColorizer

### Community 67 - "Window"
Cohesion: 0.18
Nodes (14): DescriptionText, EffectiveDatePicker, InputPriceBox, InputPriceRow, InputUnitText, OutputPriceBox, OutputPriceRow, OutputUnitText (+6 more)

### Community 68 - ".RunSelfTests"
Cohesion: 0.31
Nodes (4): DateTimeOffset, UsagePeriodValidation, DateTimeOffset, UsagePeriod

### Community 69 - "ApiUsageCost"
Cohesion: 0.32
Nodes (4): ApiUsageCost, IReadOnlyList, ApiUsageCost, RecordedApiCall

### Community 70 - ".FormatInclusive"
Cohesion: 0.23
Nodes (7): DateTimeOffset, CustomDateRangeParserValidation, DateTimeOffset, From, To, CustomDateRangeParser, Result

### Community 71 - "Window"
Cohesion: 0.24
Nodes (8): DescriptionText, StoredKeyStatusText, Window, WindowTitleText, KeyEventArgs, RoutedEventArgs, CredentialSourceDialog, TextBlock

### Community 72 - "AnthropicProofreadingClient"
Cohesion: 0.18
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, AnthropicProofreadingClient

### Community 73 - "PlamoProofreadingClient"
Cohesion: 0.16
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, PlamoProofreadingClient

### Community 74 - "gui-settings-test.py"
Cohesion: 0.27
Nodes (12): class_name(), click_settings_button(), click_trash_button(), close_window(), dialog_details(), error_dialogs(), find_window(), main() (+4 more)

### Community 75 - ".CheckLatestAsync"
Cohesion: 0.27
Nodes (7): CancellationToken, HttpClient, Task, ReleaseUpdateInfo, ReleaseUpdateService, UpdateCheckResult, Version

### Community 76 - "BillingHistoryWindow"
Cohesion: 0.13
Nodes (13): CompleteFxButton, ExportCsvButton, RefreshButton, bool, DateTimeOffset, From, List, RoutedEventArgs (+5 more)

### Community 77 - ".Create"
Cohesion: 0.25
Nodes (5): BackupServiceValidation, string, BackupResult, BackupService, ZipArchive

### Community 78 - "GPT 6 Astra 追補ベンチマーク（2026-09-07）"
Cohesion: 0.33
Nodes (5): GPT 6 Astra 追補ベンチマーク（2026-09-07）, 実行条件, 文章別, 既存計測との位置関係, 結果

### Community 79 - "GeminiProofreadingClient"
Cohesion: 0.16
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, GeminiProofreadingClient

### Community 80 - "OpenAiProofreadingClient"
Cohesion: 0.16
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, OpenAiProofreadingClient

### Community 81 - "UsageAccumulator"
Cohesion: 0.32
Nodes (6): bool, decimal, HashSet, long, DailyTotals, UsageAccumulator

### Community 82 - "CLAUDE.md"
Cohesion: 0.15
Nodes (4): App.xaml.cs, Graphify Integration, PromptValidation, ProofreadingModelCatalog

### Community 84 - "AppPaths"
Cohesion: 0.27
Nodes (3): string, AppPaths, AppPathsValidation

### Community 85 - "サードパーティー通知（THIRD-PARTY NOTICES）"
Cohesion: 0.20
Nodes (9): 1. AvalonEdit 6.3.1.120, 2. Microsoft.Data.Sqlite 10.0.10 / Microsoft.Data.Sqlite.Core 10.0.10, 3. SQLitePCLRaw 2.1.12, 4. SQLite, 5. .NET / .NET Desktop Runtime, 6. ビルド時のみ使用するもの（頒布物には含まれない）, 7. 外部 API サービスについて, サードパーティー通知（THIRD-PARTY NOTICES） (+1 more)

### Community 86 - ".RunAsync"
Cohesion: 0.40
Nodes (4): IReadOnlyList, string, Task, OpenAiCacheProbeCommand

### Community 88 - "PricingHistoryEditDialog"
Cohesion: 0.38
Nodes (4): bool, DateOnly, RoutedEventArgs, PricingHistoryEditDialog

### Community 89 - "graphify query"
Cohesion: 0.22
Nodes (9): BFS Traversal Mode, DFS Traversal Mode, graphify explain, graphify path, graphify query, graphify reflect, NetworkX, Constrained Query Expansion (+1 more)

### Community 90 - ".Read"
Cohesion: 0.26
Nodes (4): DateTimeOffset, ApiLogCompactionValidation, Func, SqliteDataReader

### Community 91 - "CrossTabSearchWindow"
Cohesion: 0.06
Nodes (31): LineNumber, Preview, TabTitle, LineNumber, CrossTabSearchPreviewValidation, End, int, Start (+23 more)

### Community 92 - "Q: Gitの変更をレビューしてください。"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: Gitの変更をレビューしてください。, Source Nodes

### Community 94 - "TabManager"
Cohesion: 0.11
Nodes (8): INotifyPropertyChanged, bool, DispatcherTimer, int, List, ObservableCollection, TimeSpan, TabManager

### Community 95 - "3.5.1 校正 API の共通契約"
Cohesion: 0.20
Nodes (10): 3.5.1 校正 API の共通契約, Anthropic Messages API, Gemini API, OpenAI Responses API, Preferred Networks PLaMo API（OpenAI 互換）, タイムアウトと再試行, 完了判定（データ保全上の必須要件）, 思考・推論の設定 (+2 more)

### Community 96 - "要件定義書 — 常駐型 日本語スクラッチパッド（仮称: JP Scratch）"
Cohesion: 0.17
Nodes (12): 1.1 解決したい問題, 1.2 目的, 1.3 設計原則, 1. 背景と目的, 2.1 非機能要件, 2. 技術スタック, 4.1 SQLite スキーマ, 4. データモデル (+4 more)

### Community 97 - "InverseBooleanToVisibilityConverter"
Cohesion: 0.38
Nodes (4): CultureInfo, InverseBooleanToVisibilityConverter, IValueConverter, Type

### Community 98 - "RelayCommand"
Cohesion: 0.29
Nodes (4): ICommand, Action, Func, RelayCommand

### Community 99 - "PromptValidation"
Cohesion: 0.33
Nodes (6): PromptValidation, net10.0-windows, AvalonEdit (6.3.1.120), Microsoft.Data.Sqlite (10.0.10), SQLitePCLRaw.bundle_e_sqlite3 (2.1.12), Microsoft.NET.Sdk

### Community 101 - ".OnStartup"
Cohesion: 0.09
Nodes (8): EventArgs, string, StartupRegistration, IReadOnlyList, TabSaveFailure, StartupEventArgs, CancelEventArgs, KeyEventArgs

### Community 102 - "モデル仕様書: Gemini 3.5 Flash-Lite"
Cohesion: 0.22
Nodes (8): 1. 概要 (Overview), 2. トークン上限 (Token Limits), 3. 入出力仕様 (I/O Capabilities), 4. サポート機能一覧 (Features), 5. 使用・推論オプション (Serving Options), 6. 生成パラメータ, 7. Developer API 標準単価（2026-07-29 確認）, モデル仕様書: Gemini 3.5 Flash-Lite

### Community 103 - "jp-scratch"
Cohesion: 0.33
Nodes (6): net10.0-windows, jp-scratch, AvalonEdit (6.3.1.120), Microsoft.Data.Sqlite (10.0.10), SQLitePCLRaw.bundle_e_sqlite3 (2.1.12), Microsoft.NET.Sdk

### Community 104 - "Prompt Validation App README"
Cohesion: 0.33
Nodes (6): Algorithm Validation (2026-07-29), DocumentDiff Algorithm, full-rewrite-safe Prompt, Prompt Validation App README, Initial Prompt Validation, Prompt Comparison Round 2

### Community 105 - "ThemeService"
Cohesion: 0.31
Nodes (5): AppTheme, ResourceDictionary, string, ThemeService, IntPtr

### Community 108 - "JpScratch.Editor"
Cohesion: 0.16
Nodes (4): JpScratch.Editor, JpScratch.Controls, RegexReplacement, RegexReplacementValidation

### Community 109 - "JP Scratch Settings"
Cohesion: 0.40
Nodes (5): Settings UI - API & Billing, Settings UI - Editor, Settings UI - General, Settings UI - Learning, JP Scratch Settings

### Community 111 - "TabRoot"
Cohesion: 0.20
Nodes (7): MouseEventArgs, ActiveMarker, ProofreadingPanel, TabRoot, FrameworkElement, MouseButtonEventArgs, Border

### Community 112 - "ProofreadingClientBase"
Cohesion: 0.17
Nodes (12): bool, CancellationToken, Func, HttpClient, HttpRequestMessage, HttpResponseMessage, HttpStatusCode, IReadOnlyList (+4 more)

### Community 113 - "3.3 校正機能"
Cohesion: 0.29
Nodes (7): 3.3.1 実行トリガー, 3.3.2 送信範囲, 3.3.3 校正対象, 3.3.4 提案の表示, 3.3.5 提案位置の解決（重要な設計上の論点）, 3.3.6 リアクション, 3.3 校正機能

### Community 114 - "Gemini 3.7 Flash 追補ベンチマーク（2026-08-21）"
Cohesion: 0.18
Nodes (9): Billing History UI, Model Benchmark Scatter Plot (Dark), Proofreading Settings UI, 2026-08-06計測との位置関係, Gemini 3.7 Flash 追補ベンチマーク（2026-08-21）, 実行条件, 文章別, 結果 (+1 more)

### Community 115 - "5. マイルストーン"
Cohesion: 0.29
Nodes (7): 5. マイルストーン, v1 で判明した仕様上の追記, v1 — 常駐エディタ（P-1 の解決）, v2 — 校正（P-2 の解決）, v3 — 学習（P-3 の解決）, v4 — プロバイダー拡張（自動用・手動用の 2 枠）, 実装時の実測値（2026-07-28, Release / framework-dependent）

### Community 118 - "graphify reference: add-watch"
Cohesion: 0.67
Nodes (3): graphify reference: add-watch, graphify.ingest.ingest, graphify.watch

### Community 119 - "Model Performance Metrics"
Cohesion: 0.67
Nodes (3): Model Benchmark Bar Chart (Dark), Model Benchmark Scatter Plot, Model Performance Metrics

### Community 120 - "Q: 全タブ検索から、ごみ箱のタブを復元できるようにしてください。"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: 全タブ検索から、ごみ箱のタブを復元できるようにしてください。, Source Nodes

### Community 121 - "Q: モデルに更新が必要か調べてください。（新しいモデルや料金など）"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: モデルに更新が必要か調べてください。（新しいモデルや料金など）, Source Nodes

### Community 126 - "3. 機能要件"
Cohesion: 0.20
Nodes (10): 3.1.1 タスクトレイ, 3.1.2 表示位置とサイズ, 3.1.3 自動非表示と、隠すときの挙動, 3.1.4 グローバルホットキー, 3.1 常駐とウィンドウ制御, 3.6.1 表示箇所, 3.6.2 課金履歴画面（実装済み。実機確認済み）, 3.6.3 課金ガード（実装済み。実機確認済み） (+2 more)

### Community 127 - "3.2 エディタ"
Cohesion: 0.40
Nodes (5): 3.2.1 タブ, 3.2.2 編集機能, 3.2.3 検索・置換, 3.2.4 永続化（スクラッチパッド型）, 3.2 エディタ

### Community 139 - "3.4 学習機能（文体の適応）"
Cohesion: 0.40
Nodes (5): 3.4.1 リアクション履歴の few-shot 同梱, 3.4.2 スタイルガイドの自動生成, 3.4.3 ユーザー手書きのカスタム指示欄, 3.4.4 プロンプト構成（送信順）, 3.4 学習機能（文体の適応）

### Community 140 - "3.5 API 連携"
Cohesion: 0.40
Nodes (5): 3.5.2 トークン数と料金, 3.5.3 為替レート（Frankfurter API）, 3.5.4 モデルの確認状況, 3.5.5 API キーの管理, 3.5 API 連携

### Community 141 - "Window"
Cohesion: 0.18
Nodes (12): DeletedAtText, LineCountDisplay, Tab.Title, DeleteButton, EmptyTrashButton, PreviewBox, RestoreButton, SummaryText (+4 more)

### Community 142 - "graphify reference: commit hook and native CLAUDE.md integration"
Cohesion: 0.50
Nodes (3): For git commit hook, For native CLAUDE.md integration, graphify reference: commit hook and native CLAUDE.md integration

### Community 143 - "graphify reference: incremental update and cluster-only"
Cohesion: 0.50
Nodes (3): For --cluster-only, For --update (incremental re-extraction), graphify reference: incremental update and cluster-only

### Community 144 - "モデル仕様書: GPT-5.6 Luna"
Cohesion: 0.33
Nodes (5): 1. 概要, 2. 入出力と上限, 3. JP Scratch での利用, 4. 標準単価, モデル仕様書: GPT-5.6 Luna

### Community 148 - "TrayIconStateValidation"
Cohesion: 0.29
Nodes (4): IsPng, IEnumerable, Size, TrayIconStateValidation

### Community 149 - "JpScratch.Infrastructure"
Cohesion: 0.11
Nodes (4): JpScratch.Infrastructure, DependencyObject, ClipboardHelper, VisualTreeHelpers

### Community 152 - "Q: レビューで見つかった4件を修正し、PromptValidationの有料API実行ルールをAGENTS.mdへ追加する"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: レビューで見つかった4件を修正し、PromptValidationの有料API実行ルールをAGENTS.mdへ追加する, Source Nodes

### Community 153 - "Q: 現在のGit変更に適したコミット範囲を判断する"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: 現在のGit変更に適したコミット範囲を判断する, Source Nodes

### Community 154 - "FontResolver"
Cohesion: 0.33
Nodes (5): HashSet, IEnumerable, IReadOnlyList, FontResolver, FontFamily

### Community 155 - "Q: 次のコミットメッセージを作成してください。"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: 次のコミットメッセージを作成してください。, Source Nodes

### Community 156 - "Q: graphify-out の生成物のコミットメッセージを作成してください。"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: graphify-out の生成物のコミットメッセージを作成してください。, Source Nodes

### Community 157 - "Q: 補助ファイルについて教えてください。削除しても大丈夫なものですか？"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: 補助ファイルについて教えてください。削除しても大丈夫なものですか？, Source Nodes

## Knowledge Gaps
- **278 isolated node(s):** `JpScratch`, `TextBlock`, `CheckBox`, `StoredApiCall`, `net10.0-windows` (+273 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **23 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Work-memory lessons

**Preferred sources** — corroborated by past sessions; start here.
- `BackupRestoreService` (3× useful, score=2.181917743)
- `SettingsWindow` (3× useful, score=2.181917743)
- `graphify reflect` (2× useful, score=1.457483374)
- `App` (2× useful, score=1.45357578)
- `BackupService` (2× useful, score=1.45357578)

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainWindow` connect `MainWindow` to `PricingService`, `FxRateService`, `NativeMethods`, `Window`, `ProofreadingInlineDiffGenerator`, `StatusBarUsageFormatterValidation`, `ProofreadingProposal`, `ApiCallRepository`, `ApiProvider`, `.Execute`, `HideSuppressionCounter`, `ScratchTab`, `ReactionRepository`, `.AlternativeWithReasonMenuItem_Click`, `.RunBenchmarkAsync`, `UsageLimitServiceValidation`, `TrashWindow`, `Database`, `AppSettings`, `App`, `TabRepository`, `.SetTransientStatus`, `TrayIconService`, `.SelectProposal`, `Window`, `.RecordFailedApiCall`, `.RunProofreadingAsync`, `IdeographicSpaceColorizer`, `ApiUsageCost`, `BillingHistoryWindow`, `CrossTabSearchWindow`, `TabManager`, `.OnStartup`, `ThemeService`, `JpScratch.Editor`, `TabRoot`?**
  _High betweenness centrality (0.199) - this node is a cross-community bridge._
- **Why does `SettingsWindow` connect `SettingsWindow` to `PricingService`, `Window`, `Database`, `AppSettings`, `JpScratch.Editor`, `BackupRestoreService`, `Window`, `ApiProvider`, `.Execute`, `HotkeyCapture`, `.BuildRejectionTrendRow`, `HotkeySpec`, `.LoadFrom`, `ReactionRepository`, `ProofreadingModelCatalog`?**
  _High betweenness centrality (0.120) - this node is a cross-community bridge._
- **Why does `JpScratch.Services` connect `JpScratch.Services` to `PricingService`, `FxRateService`, `StatusBarUsageFormatterValidation`, `MissedCorrectionDialog`, `ApiCallRepository`, `.Execute`, `JpScratch.Infrastructure`, `HideSuppressionCounter`, `ReactionRepository`, `ProviderCompletionGuardValidation`, `.RunBenchmarkAsync`, `SettingsFieldFormattingValidation`, `UsageLimitServiceValidation`, `ReleaseInfo.cs`, `JpScratch.Views`, `JpScratch.Models`, `BackupRestoreService`, `.TotalsSurviveCompaction`, `TrayIconService`, `.RunSelfTests`, `.FormatInclusive`, `.CheckLatestAsync`, `.Create`, `CLAUDE.md`, `CrossTabSearchWindow`, `.OnStartup`, `JpScratch.Editor`?**
  _High betweenness centrality (0.081) - this node is a cross-community bridge._
- **What connects `JpScratch`, `TextBlock`, `CheckBox` to the rest of the system?**
  _278 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `.CreateAutomaticPlan` be split into smaller, more focused modules?**
  _Cohesion score 0.05737234652897304 - nodes in this community are weakly interconnected._
- **Should `PricingService` be split into smaller, more focused modules?**
  _Cohesion score 0.07562479714378449 - nodes in this community are weakly interconnected._
- **Should `FxRateService` be split into smaller, more focused modules?**
  _Cohesion score 0.08299240210403273 - nodes in this community are weakly interconnected._