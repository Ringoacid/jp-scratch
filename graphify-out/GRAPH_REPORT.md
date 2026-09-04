# Graph Report - jp-scratch  (2026-09-04)

## Corpus Check
- 206 files · ~201,269 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2877 nodes · 6298 edges · 164 communities (140 shown, 24 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 447 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `712e7873`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- .CreateAutomaticPlan
- PricingService
- FxRateService
- FindReplacePanel
- .RunSelfTestAsync
- Window
- NativeMethods
- Window
- ProofreadingInlineDiffGenerator
- capture-docs-screenshots.py
- GeminiProofreadingClientValidation
- BillingCsvExporterValidation
- MissedCorrectionDialog
- .BuildRows
- ProofreadingProposal
- ApiCallRepository
- MainWindow
- ApiProvider
- .Execute
- ResourceDictionary
- FxRateCompletionValidation
- 校正 UX・自動校正・課金表示 改修計画
- measure-performance.py
- .SetTransientStatus
- .RefreshUsageDisplay
- Editor
- RoutedEventArgs
- PricingHistoryChart
- ReactionRepository
- ProofreadingModelCatalog
- ProviderCompletionGuardValidation
- .GetCachedRate
- .RunBenchmarkAsync
- HotkeyService
- TrayIconService
- gui-regex-replace-test.py
- ProofreadingPrompt
- Window
- TrashWindow
- JpScratch.Services
- Window
- JpScratch.PromptValidation
- Database
- .StartOfMonth
- screenshot-main.py
- SingleInstance
- BackupRestoreService
- .Select
- App
- TabRepository
- .TriggerCheck_Changed
- CurrencyConversionValidation
- .FormatUsd
- .RunProofreadingAsync
- .TryReadAllText
- SettingsWindow
- Japanese Commit Message
- build-tray-icons.py
- plot-model-benchmark.py
- Q: model-benchmark-barsなども更新してください。
- Window
- .EnsureTodayAsync
- .Add
- GeminiUsage
- .ProofreadAsync
- ProofreadingSchedule
- StatusBarUsageFormatterValidation
- Window
- AppSettings
- ModelBenchmarkReport.cs
- .FormatInclusive
- Window
- AnthropicProofreadingClient
- PlamoProofreadingClient
- gui-settings-test.py
- .CheckLatestAsync
- BillingHistoryWindow
- .Create
- ComboBox
- GeminiProofreadingClient
- OpenAiProofreadingClient
- UsageAccumulator
- CLAUDE.md
- ClipboardHelper.cs
- AppPaths
- サードパーティー通知（THIRD-PARTY NOTICES）
- TabRoot
- ModelBenchmarkValidation
- PricingHistoryEditDialog
- graphify query
- .RunCompactionSelfTests
- Window
- Q: Gitの変更をレビューしてください。
- .LoadHistory
- ScratchTab
- 3.5.1 校正 API の共通契約
- 要件定義書 — 常駐型 日本語スクラッチパッド（仮称: JP Scratch）
- InverseBooleanToVisibilityConverter
- RelayCommand
- PromptValidation
- .CustomRange_TextChanged
- ApiUsageDisplayCost
- モデル仕様書: Gemini 3.5 Flash-Lite
- jp-scratch
- Prompt Validation App README
- ThemeService
- .Read
- JP Scratch
- JpScratch.Editor
- JP Scratch Settings
- smoke-test.ps1
- ProofreadingClientRouter
- ProofreadingClientBase
- 3.3 校正機能
- Gemini 3.7 Flash 追補ベンチマーク（2026-08-21）
- 5. マイルストーン
- CrossTabSearchWindow
- graphify skill
- graphify reference: add-watch
- Model Performance Metrics
- .HotkeyBox_LostFocus
- .RunOneAsync
- .PeriodCombo_SelectionChanged
- graphify reference: exports
- Cross-Tab Search UI
- Main Editor UI (Dark Mode)
- 3. 機能要件
- 3.2 エディタ
- Dark.xaml
- Light.xaml
- TrashListItem
- .ApplyFxRates
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
- .CollectHits
- graphify reference: GitHub clone and cross-repo merge
- graphify reference: transcribe video and audio
- .LoadStyleGuideControls
- VisualTreeHelpers
- StubHandler
- CrossTabSearchPreviewValidation
- Q: レビューで見つかった4件を修正し、PromptValidationの有料API実行ルールをAGENTS.mdへ追加する
- Q: 現在のGit変更に適したコミット範囲を判断する
- HotkeySpec
- Q: 次のコミットメッセージを作成してください。
- Q: graphify-out の生成物のコミットメッセージを作成してください。
- Q: 補助ファイルについて教えてください。削除しても大丈夫なものですか？
- FxRateCompletionItem
- .RunAsync
- .Create
- StubHandler
- SingleInstanceValidation
- BillingHistoryEmptyStateValidation.cs

## God Nodes (most connected - your core abstractions)
1. `MainWindow` - 178 edges
2. `Window` - 86 edges
3. `JpScratch.Services` - 83 edges
4. `SettingsWindow` - 82 edges
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

## Communities (164 total, 24 thin omitted)

### Community 0 - ".CreateAutomaticPlan"
Cohesion: 0.06
Nodes (31): CurrentPartStart, ParagraphProofreadingPlannerValidation, ProofreadingDispatchPlannerValidation, End, HashSet, int, IReadOnlyList, IReadOnlySet (+23 more)

### Community 1 - "PricingService"
Cohesion: 0.05
Nodes (36): HashSet, IEnumerable, IReadOnlyList, FontResolver, FontFamily, DateOnly, IReadOnlyList, CatalogPricingHistoryEntry (+28 more)

### Community 2 - "FxRateService"
Cohesion: 0.13
Nodes (15): SemaphoreSlim, bool, CancellationToken, DateOnly, DateTimeOffset, Func, HttpClient, JsonElement (+7 more)

### Community 3 - "FindReplacePanel"
Cohesion: 0.05
Nodes (42): CloseButton, CrossTabButton, InSelectionCheck, MatchCaseToggle, MatchCountText, NextButton, PrevButton, RegexToggle (+34 more)

### Community 4 - ".RunSelfTestAsync"
Cohesion: 0.05
Nodes (35): Options, IReadOnlyList, IReadOnlyList, List, Evaluator, CancellationToken, decimal, HttpClient (+27 more)

### Community 5 - "Window"
Cohesion: 0.06
Nodes (58): EffectiveFromText, InputDeltaText, InputText, OutputDeltaText, OutputText, SourceText, StatusText, ApiLogRetentionBox (+50 more)

### Community 6 - "NativeMethods"
Cohesion: 0.10
Nodes (20): APPBARDATA, DllImport, int, IntPtr, APPBARDATA, MONITORINFO, NativeMethods, POINT (+12 more)

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

### Community 11 - "BillingCsvExporterValidation"
Cohesion: 0.11
Nodes (10): Encoding, List, BillingCsvExporterValidation, DateTimeOffset, IEnumerable, IReadOnlyList, string, BillingCsvExporter (+2 more)

### Community 12 - "MissedCorrectionDialog"
Cohesion: 0.08
Nodes (20): MissedCorrectionActionValidation, int, MissedCorrectionAction, MissedCorrectionKind, MissedCorrectionPreview, TextDecorationCollection, CorrectedBox, ExecuteButton (+12 more)

### Community 13 - ".BuildRows"
Cohesion: 0.14
Nodes (12): Day1, Day2, MidDay, OldDay, DateOnly, DateTimeOffset, IReadOnlyList, BillingSeedCommand (+4 more)

### Community 14 - "ProofreadingProposal"
Cohesion: 0.06
Nodes (30): DiffKind, DiffOperation, DocumentChangeEventArgs, JsonSerializerOptions, DocumentDiffValidation, Dictionary, double, IEnumerable (+22 more)

### Community 15 - "ApiCallRepository"
Cohesion: 0.12
Nodes (23): DailyKey, DailyTotals, InClause, IReadOnlyCollection, Name, Parameters, SeedRow, DateOnly (+15 more)

### Community 16 - "MainWindow"
Cohesion: 0.07
Nodes (16): DataObjectSettingDataEventArgs, EventArgs, bool, Brush, CancelEventArgs, DateTime, decimal, DispatcherTimer (+8 more)

### Community 17 - "ApiProvider"
Cohesion: 0.17
Nodes (11): byte, ApiKeySource, ApiProvider, CredentialServiceValidation, Func, int, string, CredentialService (+3 more)

### Community 18 - ".Execute"
Cohesion: 0.17
Nodes (8): Action, DateTimeOffset, Func, IReadOnlyList, SqliteDataReader, string, StyleGuide, StyleGuideRepository

### Community 19 - "ResourceDictionary"
Cohesion: 0.07
Nodes (32): IsDropDownOpen, View.Columns, Arrow, Bd, Box, Check, Checked, CheckStates (+24 more)

### Community 20 - "FxRateCompletionValidation"
Cohesion: 0.26
Nodes (6): Clock, DateOnly, DateTimeOffset, Task, Clock, FxRateCompletionValidation

### Community 21 - "校正 UX・自動校正・課金表示 改修計画"
Cohesion: 0.04
Nodes (47): 10.1 項目, 10.2 動作, 10.3 主な変更候補, 10.4 必須テスト, 10. エディタの右クリックメニュー, 11.1 自動テスト, 11.2 ビルド, 11.3 手動 UI 確認 (+39 more)

### Community 22 - "measure-performance.py"
Cohesion: 0.17
Nodes (22): click(), close_window(), ensure_no_app_running(), FILETIME, filetime_value(), find_window(), find_window_containing(), focus() (+14 more)

### Community 23 - ".SetTransientStatus"
Cohesion: 0.12
Nodes (3): int, HideSuppressionCounter, CrossTabHit

### Community 24 - ".RefreshUsageDisplay"
Cohesion: 0.14
Nodes (6): string, StartupRegistration, StartupEventArgs, DateOnly, DateTimeOffset, IEnumerable

### Community 25 - "Editor"
Cohesion: 0.20
Nodes (6): ContextMenuEventArgs, MouseWheelEventArgs, Editor, TabScroller, ScrollViewer, TextEditor

### Community 26 - "RoutedEventArgs"
Cohesion: 0.10
Nodes (16): PricingHistoryRow, CancelButton, CheckForUpdatesButton, CopyDiagnosticsButton, CreateBackupButton, OpenFolderButton, PricingAddButton, PricingDeleteButton (+8 more)

### Community 27 - "PricingHistoryChart"
Cohesion: 0.14
Nodes (16): Brush, DateOnly, double, DrawingContext, Func, IEnumerable, IReadOnlyList, Point (+8 more)

### Community 28 - "ReactionRepository"
Cohesion: 0.25
Nodes (6): FewShotCandidate, ReactionRepositoryValidation, IReadOnlyList, ProofreadingReaction, ReactionRepository, RejectionRateBucket

### Community 29 - "ProofreadingModelCatalog"
Cohesion: 0.10
Nodes (10): Automatic, Manual, Dictionary, string, TimeSpan, ProofreadingModelCatalog, ProofreadingModelCatalogValidation, ApiKeyBox (+2 more)

### Community 30 - "ProviderCompletionGuardValidation"
Cohesion: 0.13
Nodes (14): Body, Case, HttpMessageHandler, Label, CancellationToken, Func, HttpClient, HttpRequestMessage (+6 more)

### Community 31 - ".GetCachedRate"
Cohesion: 0.12
Nodes (10): ApiUsageCost, FailedApiCallRecord, RecordedApiCall, Exception, IReadOnlyList, TimeSpan, ApiUsageCost, FailedApiCallRecord (+2 more)

### Community 32 - ".RunBenchmarkAsync"
Cohesion: 0.14
Nodes (11): BenchmarkOptions, ModelDescriptor, HttpClient, int, IReadOnlyDictionary, IReadOnlyList, JsonSerializerOptions, Task (+3 more)

### Community 33 - "HotkeyService"
Cohesion: 0.26
Nodes (6): HwndSource, int, IntPtr, IReadOnlyList, List, HotkeyService

### Community 34 - "TrayIconService"
Cohesion: 0.07
Nodes (18): Icon, IsPng, NotifyIcon, IEnumerable, Size, TrayIconStateValidation, DateTimeOffset, UsageLimitServiceValidation (+10 more)

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

### Community 39 - "JpScratch.Services"
Cohesion: 0.12
Nodes (7): JpScratch.Views, JpScratch.Models, JpScratch.Services, JpScratch, JpScratch.Infrastructure, string, ReleaseInfo

### Community 40 - "Window"
Cohesion: 0.10
Nodes (20): CalledAt, DiscardedCount, Duration, ErrorMessage, Jpy, Model, OutputTokens, PromptTokens (+12 more)

### Community 41 - "JpScratch.PromptValidation"
Cohesion: 0.09
Nodes (6): JpScratch.PromptValidation, JpScratch.Proofreading, ApiUsageDisplayFormatterValidation, BackupRestoreServiceValidation, HideSuppressionCounterValidation, StyleGuideRepositoryValidation

### Community 42 - "Database"
Cohesion: 0.16
Nodes (10): IDisposable, Lock, string, TestStore, string, TestStore, bool, Database (+2 more)

### Community 43 - ".StartOfMonth"
Cohesion: 0.27
Nodes (4): DateTimeOffset, UsagePeriodValidation, DateTimeOffset, UsagePeriod

### Community 44 - "screenshot-main.py"
Cohesion: 0.15
Nodes (19): BITMAPINFO, BITMAPINFOHEADER, capture_bitblt(), capture_print_window(), capture_window(), find_window(), is_blank(), main() (+11 more)

### Community 45 - "SingleInstance"
Cohesion: 0.12
Nodes (9): ActivateEventName, EventWaitHandle, ExitEventArgs, Action, string, SingleInstance, Mutex, MutexName (+1 more)

### Community 46 - "BackupRestoreService"
Cohesion: 0.14
Nodes (10): AppVersion, IncludesCredentials, Func, int, long, string, BackupRestoreService, PreparedBackup (+2 more)

### Community 47 - ".Select"
Cohesion: 0.17
Nodes (11): FewShotSelectorValidation, DateTimeOffset, HashSet, int, IReadOnlyList, FewShotCandidate, FewShotExample, FewShotSelection (+3 more)

### Community 48 - "App"
Cohesion: 0.11
Nodes (15): Application, Action, ApiKeySource, ApiProvider, bool, Exception, IEnumerable, IReadOnlyList (+7 more)

### Community 49 - "TabRepository"
Cohesion: 0.18
Nodes (5): TrashRepositoryValidation, DateTime, List, string, TabRepository

### Community 50 - ".TriggerCheck_Changed"
Cohesion: 0.53
Nodes (5): TriggerAutoCheck, TriggerManualCheck, TriggerRealternativeCheck, TriggerStyleGuideCheck, CheckBox

### Community 52 - ".FormatUsd"
Cohesion: 0.22
Nodes (6): StatusBarCurrencyFormat, ApiCallLog, ApiCallUsageSummary, StatusBarDisplayOptions, StatusBarUsageFormatter, UsageFormatting

### Community 54 - ".TryReadAllText"
Cohesion: 0.17
Nodes (5): AtomicFile, FileReadFailure, AtomicFileValidation, ReadOnlySpan, UTF8Encoding

### Community 55 - "SettingsWindow"
Cohesion: 0.09
Nodes (13): ComboBox, TextBlock, OkButton, bool, CancelEventArgs, Dictionary, FrameworkElement, Func (+5 more)

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

### Community 61 - ".EnsureTodayAsync"
Cohesion: 0.43
Nodes (4): DateOnly, DateTimeOffset, Task, FxRateServiceValidation

### Community 62 - ".Add"
Cohesion: 0.27
Nodes (4): Action, DateTimeOffset, ApiCallRepositoryValidation, StoredApiCall

### Community 63 - "GeminiUsage"
Cohesion: 0.19
Nodes (13): Exception, JsonElement, HttpStatusCode, TimeSpan, GeminiAlternativeResult, GeminiClientError, GeminiClientException, GeminiProofreadingResult (+5 more)

### Community 64 - ".ProofreadAsync"
Cohesion: 0.28
Nodes (8): CancellationToken, Func, HttpRequestMessage, HttpResponseMessage, string, Task, OpenAiProofreadingClientValidation, StubHandler

### Community 65 - "ProofreadingSchedule"
Cohesion: 0.27
Nodes (5): ProofreadingScheduleValidation, DateTimeOffset, Dictionary, TimeSpan, ProofreadingSchedule

### Community 67 - "Window"
Cohesion: 0.18
Nodes (14): DescriptionText, EffectiveDatePicker, InputPriceBox, InputPriceRow, InputUnitText, OutputPriceBox, OutputPriceRow, OutputUnitText (+6 more)

### Community 68 - "AppSettings"
Cohesion: 0.21
Nodes (7): decimal, IReadOnlyList, AppSettings, WindowPositionMode, DispatcherTimer, JsonSerializerOptions, SettingsService

### Community 69 - "ModelBenchmarkReport.cs"
Cohesion: 0.24
Nodes (13): double, int, IReadOnlyList, BenchmarkFxRate, BenchmarkModelInfo, BenchmarkModelSummary, BenchmarkProtectionSummary, BenchmarkReport (+5 more)

### Community 70 - ".FormatInclusive"
Cohesion: 0.21
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
Cohesion: 0.30
Nodes (6): bool, DateTimeOffset, From, To, BillingHistoryWindow, PeriodOption

### Community 77 - ".Create"
Cohesion: 0.23
Nodes (5): BackupServiceValidation, string, BackupResult, BackupService, ZipArchive

### Community 78 - "ComboBox"
Cohesion: 0.13
Nodes (13): AutoProofreadingModelCombo, CredentialSourceCombo, FontCombo, ManualProofreadingModelCombo, PositionCombo, PricingHistoryList, PricingModelCombo, StatusBarCurrencyCombo (+5 more)

### Community 79 - "GeminiProofreadingClient"
Cohesion: 0.16
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, GeminiProofreadingClient

### Community 80 - "OpenAiProofreadingClient"
Cohesion: 0.16
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, OpenAiProofreadingClient

### Community 81 - "UsageAccumulator"
Cohesion: 0.38
Nodes (6): bool, decimal, HashSet, long, DailyTotals, UsageAccumulator

### Community 82 - "CLAUDE.md"
Cohesion: 0.17
Nodes (4): App.xaml.cs, Graphify Integration, PromptValidation, ProofreadingModelCatalog

### Community 84 - "AppPaths"
Cohesion: 0.24
Nodes (3): string, AppPaths, AppPathsValidation

### Community 85 - "サードパーティー通知（THIRD-PARTY NOTICES）"
Cohesion: 0.20
Nodes (9): 1. AvalonEdit 6.3.1.120, 2. Microsoft.Data.Sqlite 10.0.10 / Microsoft.Data.Sqlite.Core 10.0.10, 3. SQLitePCLRaw 2.1.12, 4. SQLite, 5. .NET / .NET Desktop Runtime, 6. ビルド時のみ使用するもの（頒布物には含まれない）, 7. 外部 API サービスについて, サードパーティー通知（THIRD-PARTY NOTICES） (+1 more)

### Community 86 - "TabRoot"
Cohesion: 0.20
Nodes (7): MouseEventArgs, ActiveMarker, ProofreadingPanel, TabRoot, FrameworkElement, MouseButtonEventArgs, Border

### Community 87 - "ModelBenchmarkValidation"
Cohesion: 0.29
Nodes (3): IReadOnlyList, JsonSerializerOptions, ModelBenchmarkValidation

### Community 88 - "PricingHistoryEditDialog"
Cohesion: 0.38
Nodes (4): bool, DateOnly, RoutedEventArgs, PricingHistoryEditDialog

### Community 89 - "graphify query"
Cohesion: 0.22
Nodes (9): BFS Traversal Mode, DFS Traversal Mode, graphify explain, graphify path, graphify query, graphify reflect, NetworkX, Constrained Query Expansion (+1 more)

### Community 90 - ".RunCompactionSelfTests"
Cohesion: 0.23
Nodes (4): DateTimeOffset, ApiLogCompactionValidation, DateTimeOffset, ApiLogRetention

### Community 91 - "Window"
Cohesion: 0.15
Nodes (15): LineNumber, Preview, TabTitle, IncludeTrashCheck, JumpButton, MatchCaseCheck, RegexCheck, SearchButton (+7 more)

### Community 92 - "Q: Gitの変更をレビューしてください。"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: Gitの変更をレビューしてください。, Source Nodes

### Community 93 - ".LoadHistory"
Cohesion: 0.20
Nodes (6): CompleteFxButton, ExportCsvButton, RefreshButton, List, RoutedEventArgs, Button

### Community 94 - "ScratchTab"
Cohesion: 0.10
Nodes (14): INotifyPropertyChanged, bool, DateTime, string, TextDocument, ScratchTab, bool, DispatcherTimer (+6 more)

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

### Community 100 - ".CustomRange_TextChanged"
Cohesion: 0.50
Nodes (4): CustomFromBox, CustomToBox, TextChangedEventArgs, TextBox

### Community 101 - "ApiUsageDisplayCost"
Cohesion: 0.45
Nodes (3): IReadOnlyList, ApiUsageDisplayCost, ApiUsageDisplayFormatter

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
Cohesion: 0.42
Nodes (4): AppTheme, ResourceDictionary, string, ThemeService

### Community 106 - ".Read"
Cohesion: 0.27
Nodes (5): int, DatabaseMigrationValidation, Func, SqliteDataReader, SqliteCommand

### Community 108 - "JpScratch.Editor"
Cohesion: 0.11
Nodes (9): char, JpScratch.Editor, JpScratch.Controls, DocumentColorizingTransformer, DocumentLine, Brush, IdeographicSpaceColorizer, RegexReplacement (+1 more)

### Community 109 - "JP Scratch Settings"
Cohesion: 0.40
Nodes (5): Settings UI - API & Billing, Settings UI - Editor, Settings UI - General, Settings UI - Learning, JP Scratch Settings

### Community 111 - "ProofreadingClientRouter"
Cohesion: 0.22
Nodes (7): CancellationToken, Dictionary, IReadOnlyList, string, Task, TimeSpan, ProofreadingClientRouter

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

### Community 116 - "CrossTabSearchWindow"
Cohesion: 0.23
Nodes (6): ResultsList, KeyEventArgs, MouseButtonEventArgs, RoutedEventArgs, CrossTabSearchWindow, ListView

### Community 118 - "graphify reference: add-watch"
Cohesion: 0.67
Nodes (3): graphify reference: add-watch, graphify.ingest.ingest, graphify.watch

### Community 119 - "Model Performance Metrics"
Cohesion: 0.67
Nodes (3): Model Benchmark Bar Chart (Dark), Model Benchmark Scatter Plot, Model Performance Metrics

### Community 120 - ".HotkeyBox_LostFocus"
Cohesion: 0.38
Nodes (4): TextBox, CopyHideHotkeyBox, ToggleHotkeyBox, KeyEventArgs

### Community 121 - ".RunOneAsync"
Cohesion: 0.24
Nodes (7): CostUsd, Known, CancellationToken, CancellationToken, IReadOnlyList, Task, IProofreadingClient

### Community 122 - ".PeriodCombo_SelectionChanged"
Cohesion: 0.40
Nodes (3): PeriodCombo, SelectionChangedEventArgs, ComboBox

### Community 126 - "3. 機能要件"
Cohesion: 0.20
Nodes (10): 3.1.1 タスクトレイ, 3.1.2 表示位置とサイズ, 3.1.3 自動非表示と、隠すときの挙動, 3.1.4 グローバルホットキー, 3.1 常駐とウィンドウ制御, 3.6.1 表示箇所, 3.6.2 課金履歴画面（実装済み。実機確認済み）, 3.6.3 課金ガード（実装済み。実機確認済み） (+2 more)

### Community 127 - "3.2 エディタ"
Cohesion: 0.40
Nodes (5): 3.2.1 タブ, 3.2.2 編集機能, 3.2.3 検索・置換, 3.2.4 永続化（スクラッチパッド型）, 3.2 エディタ

### Community 132 - ".ApplyFxRates"
Cohesion: 0.25
Nodes (4): IReadOnlyDictionary, FxRateCompletionResult, decimal, UsdCostConversion

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

### Community 145 - ".CollectHits"
Cohesion: 0.20
Nodes (7): LineNumber, End, int, Start, CrossTabSearchPreview, List, Regex

### Community 148 - ".LoadStyleGuideControls"
Cohesion: 0.24
Nodes (4): StyleGuideActivateButton, StyleGuideDeactivateButton, StyleGuideDeleteButton, StyleGuideSaveButton

### Community 150 - "StubHandler"
Cohesion: 0.29
Nodes (7): CancellationToken, Func, HttpRequestMessage, HttpResponseMessage, List, Uri, StubHandler

### Community 152 - "Q: レビューで見つかった4件を修正し、PromptValidationの有料API実行ルールをAGENTS.mdへ追加する"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: レビューで見つかった4件を修正し、PromptValidationの有料API実行ルールをAGENTS.mdへ追加する, Source Nodes

### Community 153 - "Q: 現在のGit変更に適したコミット範囲を判断する"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: 現在のGit変更に適したコミット範囲を判断する, Source Nodes

### Community 154 - "HotkeySpec"
Cohesion: 0.47
Nodes (3): Key, HotkeySpec, ModifierKeys

### Community 155 - "Q: 次のコミットメッセージを作成してください。"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: 次のコミットメッセージを作成してください。, Source Nodes

### Community 156 - "Q: graphify-out の生成物のコミットメッセージを作成してください。"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: graphify-out の生成物のコミットメッセージを作成してください。, Source Nodes

### Community 157 - "Q: 補助ファイルについて教えてください。削除しても大丈夫なものですか？"
Cohesion: 0.40
Nodes (4): Answer, Outcome, Q: 補助ファイルについて教えてください。削除しても大丈夫なものですか？, Source Nodes

### Community 158 - "FxRateCompletionItem"
Cohesion: 0.33
Nodes (4): DateOnly, decimal, string, FxRateCompletionItem

### Community 159 - ".RunAsync"
Cohesion: 0.40
Nodes (4): IReadOnlyList, string, Task, OpenAiCacheProbeCommand

### Community 161 - "StubHandler"
Cohesion: 0.40
Nodes (5): CancellationToken, Func, HttpRequestMessage, HttpResponseMessage, StubHandler

### Community 162 - "SingleInstanceValidation"
Cohesion: 0.40
Nodes (3): int, string, SingleInstanceValidation

## Knowledge Gaps
- **266 isolated node(s):** `JpScratch`, `TextBlock`, `CheckBox`, `StoredApiCall`, `net10.0-windows` (+261 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **24 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainWindow` connect `MainWindow` to `PricingService`, `FxRateService`, `.ApplyFxRates`, `NativeMethods`, `Window`, `ProofreadingInlineDiffGenerator`, `ProofreadingProposal`, `ApiCallRepository`, `ApiProvider`, `.Execute`, `.SetTransientStatus`, `.RefreshUsageDisplay`, `Editor`, `ReactionRepository`, `.GetCachedRate`, `HotkeyService`, `TrayIconService`, `TrashWindow`, `JpScratch.Services`, `Database`, `App`, `TabRepository`, `.FormatUsd`, `.RunProofreadingAsync`, `Window`, `ProofreadingSchedule`, `AppSettings`, `BillingHistoryWindow`, `TabRoot`, `ScratchTab`, `ThemeService`, `JpScratch.Editor`, `CrossTabSearchWindow`, `.RunOneAsync`?**
  _High betweenness centrality (0.201) - this node is a cross-community bridge._
- **Why does `SettingsWindow` connect `SettingsWindow` to `PricingService`, `AppSettings`, `Window`, `JpScratch.Services`, `Database`, `ComboBox`, `BackupRestoreService`, `Window`, `ApiProvider`, `.Execute`, `.LoadStyleGuideControls`, `.HotkeyBox_LostFocus`, `RoutedEventArgs`, `ReactionRepository`, `ProofreadingModelCatalog`?**
  _High betweenness centrality (0.128) - this node is a cross-community bridge._
- **Why does `JpScratch.Services` connect `JpScratch.Services` to `PricingService`, `FxRateService`, `.ApplyFxRates`, `BillingCsvExporterValidation`, `MissedCorrectionDialog`, `.BuildRows`, `ApiCallRepository`, `.CollectHits`, `.Execute`, `CrossTabSearchPreviewValidation`, `.SetTransientStatus`, `.RefreshUsageDisplay`, `ReactionRepository`, `TrayIconService`, `BillingHistoryEmptyStateValidation.cs`, `JpScratch.PromptValidation`, `.StartOfMonth`, `BackupRestoreService`, `.Select`, `CurrencyConversionValidation`, `.FormatUsd`, `ModelBenchmarkReport.cs`, `.FormatInclusive`, `.CheckLatestAsync`, `.Create`, `CLAUDE.md`, `.RunCompactionSelfTests`, `ApiUsageDisplayCost`?**
  _High betweenness centrality (0.095) - this node is a cross-community bridge._
- **What connects `JpScratch`, `TextBlock`, `CheckBox` to the rest of the system?**
  _266 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `.CreateAutomaticPlan` be split into smaller, more focused modules?**
  _Cohesion score 0.05737234652897304 - nodes in this community are weakly interconnected._
- **Should `PricingService` be split into smaller, more focused modules?**
  _Cohesion score 0.05274725274725275 - nodes in this community are weakly interconnected._
- **Should `FxRateService` be split into smaller, more focused modules?**
  _Cohesion score 0.12962962962962962 - nodes in this community are weakly interconnected._