# Graph Report - jp-scratch  (2026-08-22)

## Corpus Check
- 208 files · ~200,931 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2714 nodes · 6125 edges · 151 communities (123 shown, 28 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 458 edges (avg confidence: 0.81)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `df66c1bc`
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
- BillingCsvExporterValidation
- MissedCorrectionDialog
- .BuildRows
- DocumentDiff
- ApiCallRepository
- MainWindow
- ApiProvider
- StyleGuideRepository
- ResourceDictionary
- ProofreadingClientBase
- state_manager.py
- StatusBarUsageFormatterValidation
- ProofreadingClientRouter
- ApiUsageDisplayCost
- .OnStartup
- SettingsWindow
- PricingHistoryChart
- FxRateCompletionValidation
- ProofreadingModelCatalog
- ProofreadingProposal
- .GetCachedRate
- .RunBenchmarkAsync
- .HideWindow
- UsageLimitServiceValidation
- .FormatUsd
- ProviderCompletionGuardValidation
- Window
- TrashWindow
- .SetTransientStatus
- Window
- JpScratch.Services
- Database
- ScratchTab
- screenshot-main.py
- TabManager.cs
- .AlternativeWithReasonMenuItem_Click
- SettingsFieldFormattingValidation
- App
- .TestDeleteAllTrash
- SelectionChangedEventArgs
- .RunSelfTests
- SingleInstance
- AppSettings
- TrayIconService
- RoutedEventArgs
- Japanese Commit Message
- build-tray-icons.py
- plot-model-benchmark.py
- Q: model-benchmark-barsなども更新してください。
- Window
- JpScratch.Editor
- .RunSelfTestAsync
- .TotalsSurviveCompaction
- .ProofreadAsync
- .RunProofreadingAsync
- ProofreadingPrompt
- .EnsureTodayAsync
- JpScratch.Models
- TabRepository
- .FormatInclusive
- ModelBenchmarkReport.cs
- AnthropicProofreadingClient
- PlamoProofreadingClient
- gui-settings-test.py
- .HotkeyBox_LostFocus
- BillingHistoryWindow
- Window
- state_manager.ps1
- GeminiProofreadingClient
- OpenAiProofreadingClient
- ModelBenchmarkValidation
- CLAUDE.md
- TrayIconStateValidation
- AppPaths
- ProofreadingInlineDiffElement
- TabRoot
- TrashListItem
- settings.json
- graphify query
- FontResolver
- CrossTabSearchWindow
- GeminiUsage
- ProofreadingInlineDiff
- UsageAccumulator
- 3.5.1 校正 API の共通契約
- 要件定義書 — 常駐型 日本語スクラッチパッド（仮称: JP Scratch）
- InverseBooleanToVisibilityConverter
- RelayCommand
- PromptValidation
- HotkeyService
- HideSuppressionCounter
- .ToUsdCost
- jp-scratch
- Prompt Validation App README
- .OnExit
- sync-config.ps1
- JP Scratch
- VisualTreeHelpers
- JP Scratch Settings
- smoke-test.ps1
- InlineDiffParagraphProperties
- .ParseRaw
- 3.3 校正機能
- Gemini 3.7 Flash 追補ベンチマーク（2026-08-21）
- 5. マイルストーン
- codex
- graphify skill
- graphify reference: add-watch
- Model Performance Metrics
- HotkeySpec
- StubHandler
- sync-config.sh script
- graphify reference: exports
- Cross-Tab Search UI
- Main Editor UI (Dark Mode)
- 3. 機能要件
- 3.2 エディタ
- Dark.xaml
- Light.xaml
- Sol review contract
- state.json Schema
- Graphify Skill
- Context Menu UI
- Model Benchmark Bar Chart (Light)
- Proofreading Suggestion UI
- PromptValidation
- 3.4 学習機能（文体の適応）
- 3.5 API 連携
- ProofreadingInlineDiffLayoutValidation
- graphify reference: commit hook and native CLAUDE.md integration
- graphify reference: incremental update and cluster-only
- SingleInstanceValidation
- ThemeService
- graphify reference: GitHub clone and cross-repo merge
- graphify reference: transcribe video and audio
- .CreateTextRun
- .ApiKeyBox_PasswordChanged
- .RunAsync

## God Nodes (most connected - your core abstractions)
1. `MainWindow` - 176 edges
2. `Window` - 79 edges
3. `JpScratch.Services` - 76 edges
4. `SettingsWindow` - 73 edges
5. `JpScratch.PromptValidation` - 48 edges
6. `PricingService` - 47 edges
7. `ScratchTab` - 43 edges
8. `TabManager` - 39 edges
9. `Window` - 38 edges
10. `Database` - 37 edges

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

## Communities (151 total, 28 thin omitted)

### Community 0 - ".CreateAutomaticPlan"
Cohesion: 0.06
Nodes (31): CurrentPartStart, ParagraphProofreadingPlannerValidation, ProofreadingDispatchPlannerValidation, End, HashSet, int, IReadOnlyList, IReadOnlySet (+23 more)

### Community 1 - "PricingService"
Cohesion: 0.07
Nodes (28): AtomicFile, FileReadFailure, AtomicFileValidation, Action, Dictionary, PricingServiceValidation, ReadOnlySpan, DateOnly (+20 more)

### Community 2 - "FxRateService"
Cohesion: 0.10
Nodes (19): SemaphoreSlim, bool, CancellationToken, DateOnly, DateTimeOffset, Func, HttpClient, JsonElement (+11 more)

### Community 3 - "FindReplacePanel"
Cohesion: 0.05
Nodes (42): CloseButton, CrossTabButton, InSelectionCheck, MatchCaseToggle, MatchCountText, NextButton, PrevButton, RegexToggle (+34 more)

### Community 4 - ".Main"
Cohesion: 0.06
Nodes (34): Options, IReadOnlyList, List, Evaluator, CancellationToken, decimal, HttpClient, HttpResponseMessage (+26 more)

### Community 5 - "Window"
Cohesion: 0.05
Nodes (63): EffectiveFromText, InputDeltaText, InputText, OutputDeltaText, OutputText, SourceText, StatusText, ApiLogRetentionBox (+55 more)

### Community 6 - "NativeMethods"
Cohesion: 0.11
Nodes (20): APPBARDATA, DllImport, int, IntPtr, APPBARDATA, MONITORINFO, NativeMethods, POINT (+12 more)

### Community 7 - "Window"
Cohesion: 0.06
Nodes (33): BoolToCollapsed, BoolToVisible, IsActive, IsEditing, Title, AcceptAllProposalsButton, AcceptProposalButton, FindPanel (+25 more)

### Community 8 - "ProofreadingInlineDiffGenerator"
Cohesion: 0.18
Nodes (8): CultureSpecificCharacterBufferRange, IReadOnlyList, InlineDiffTextSource, ProofreadingInlineDiffGenerator, TextFormatter, TextSource, TextSpan, VisualLineElementGenerator

### Community 9 - "capture-docs-screenshots.py"
Cohesion: 0.08
Nodes (46): activate_first_tab(), app_is_running(), AppSession, BITMAPINFO, BITMAPINFOHEADER, build_tabs(), _capture(), capture_window() (+38 more)

### Community 10 - "GeminiProofreadingClientValidation"
Cohesion: 0.22
Nodes (10): CancellationToken, Func, HttpClient, HttpRequestMessage, HttpResponseMessage, string, Task, GeminiProofreadingClientValidation (+2 more)

### Community 11 - "BillingCsvExporterValidation"
Cohesion: 0.15
Nodes (8): Encoding, List, BillingCsvExporterValidation, DateTimeOffset, IEnumerable, IReadOnlyList, string, BillingCsvExporter

### Community 12 - "MissedCorrectionDialog"
Cohesion: 0.08
Nodes (20): MissedCorrectionActionValidation, int, MissedCorrectionAction, MissedCorrectionKind, MissedCorrectionPreview, TextDecorationCollection, CorrectedBox, ExecuteButton (+12 more)

### Community 13 - ".BuildRows"
Cohesion: 0.20
Nodes (10): Day1, Day2, MidDay, OldDay, DateOnly, DateTimeOffset, IReadOnlyList, BillingSeedCommand (+2 more)

### Community 14 - "DocumentDiff"
Cohesion: 0.10
Nodes (21): DiffKind, DiffOperation, IReadOnlyList, JsonSerializerOptions, DocumentDiffValidation, Dictionary, double, IEnumerable (+13 more)

### Community 15 - "ApiCallRepository"
Cohesion: 0.11
Nodes (24): DailyKey, DailyTotals, InClause, IReadOnlyCollection, Name, Parameters, DateOnly, DateTimeOffset (+16 more)

### Community 16 - "MainWindow"
Cohesion: 0.10
Nodes (15): DataObjectSettingDataEventArgs, UsageLimitState, bool, Brush, DateOnly, DateTime, DateTimeOffset, decimal (+7 more)

### Community 17 - "ApiProvider"
Cohesion: 0.13
Nodes (12): Action, byte, ApiKeySource, ApiProvider, CredentialServiceValidation, Func, int, string (+4 more)

### Community 18 - "StyleGuideRepository"
Cohesion: 0.09
Nodes (15): StyleGuideRepositoryValidation, DateTimeOffset, Func, IReadOnlyList, SqliteDataReader, string, StyleGuide, StyleGuideRepository (+7 more)

### Community 19 - "ResourceDictionary"
Cohesion: 0.07
Nodes (32): IsDropDownOpen, View.Columns, Arrow, Bd, Box, Check, Checked, CheckStates (+24 more)

### Community 20 - "ProofreadingClientBase"
Cohesion: 0.17
Nodes (12): bool, CancellationToken, Func, HttpClient, HttpRequestMessage, HttpResponseMessage, HttpStatusCode, IReadOnlyList (+4 more)

### Community 21 - "state_manager.py"
Cohesion: 0.28
Nodes (31): Any, ArgumentParser, atomic_write_state(), build_parser(), cmd_add_approval(), cmd_add_plan(), cmd_add_report(), cmd_add_review() (+23 more)

### Community 22 - "StatusBarUsageFormatterValidation"
Cohesion: 0.17
Nodes (6): StatusBarCurrencyFormat, DateOnly, StatusBarUsageFormatterValidation, ApiCallLog, StatusBarDisplayOptions, StatusBarUsageFormatter

### Community 23 - "ProofreadingClientRouter"
Cohesion: 0.15
Nodes (11): CancellationToken, IReadOnlyList, Task, IProofreadingClient, CancellationToken, Dictionary, IReadOnlyList, string (+3 more)

### Community 24 - "ApiUsageDisplayCost"
Cohesion: 0.40
Nodes (4): ApiUsageDisplayFormatterValidation, IReadOnlyList, ApiUsageDisplayCost, ApiUsageDisplayFormatter

### Community 25 - ".OnStartup"
Cohesion: 0.12
Nodes (4): IReadOnlyList, string, StartupRegistration, StartupEventArgs

### Community 26 - "SettingsWindow"
Cohesion: 0.10
Nodes (12): ComboBox, TextBlock, OkButton, PricingModelCombo, bool, CancelEventArgs, Dictionary, FrameworkElement (+4 more)

### Community 27 - "PricingHistoryChart"
Cohesion: 0.12
Nodes (17): Brush, DateOnly, double, DrawingContext, Func, IEnumerable, IReadOnlyList, Point (+9 more)

### Community 28 - "FxRateCompletionValidation"
Cohesion: 0.18
Nodes (13): Clock, CancellationToken, DateOnly, DateTimeOffset, Func, HttpRequestMessage, HttpResponseMessage, List (+5 more)

### Community 29 - "ProofreadingModelCatalog"
Cohesion: 0.13
Nodes (11): Automatic, Manual, DateOnly, Dictionary, IReadOnlyList, string, TimeSpan, CatalogPricingHistoryEntry (+3 more)

### Community 30 - "ProofreadingProposal"
Cohesion: 0.06
Nodes (27): DocumentChangeEventArgs, FewShotCandidate, FewShotSelectorValidation, ReactionRepositoryValidation, DateTimeOffset, HashSet, int, IReadOnlyList (+19 more)

### Community 31 - ".GetCachedRate"
Cohesion: 0.12
Nodes (10): ApiUsageCost, FailedApiCallRecord, RecordedApiCall, Exception, IReadOnlyList, TimeSpan, ApiUsageCost, FailedApiCallRecord (+2 more)

### Community 32 - ".RunBenchmarkAsync"
Cohesion: 0.14
Nodes (13): BenchmarkOptions, ModelDescriptor, CancellationToken, HttpClient, int, IReadOnlyDictionary, IReadOnlyList, JsonSerializerOptions (+5 more)

### Community 33 - ".HideWindow"
Cohesion: 0.11
Nodes (6): EventArgs, MouseWheelEventArgs, TabScroller, CancelEventArgs, KeyEventArgs, ScrollViewer

### Community 34 - "UsageLimitServiceValidation"
Cohesion: 0.18
Nodes (6): DateTimeOffset, UsageLimitServiceValidation, DateTimeOffset, string, UsageLimitNotificationTracker, UsageLimitService

### Community 35 - ".FormatUsd"
Cohesion: 0.22
Nodes (5): DateTimeOffset, BillingHistoryEmptyStateValidation, ApiCallUsageSummary, DateOnly, UsageFormatting

### Community 36 - "ProviderCompletionGuardValidation"
Cohesion: 0.12
Nodes (14): Body, Case, HttpMessageHandler, Label, CancellationToken, Func, HttpClient, HttpRequestMessage (+6 more)

### Community 37 - "Window"
Cohesion: 0.12
Nodes (18): CalledDateText, CompletableCountText, RateText, UncompletableCountText, ApplyButton, CancelButton, FetchButton, MessageText (+10 more)

### Community 38 - "TrashWindow"
Cohesion: 0.17
Nodes (8): ResultsList, KeyEventArgs, MouseButtonEventArgs, ObservableCollection, RoutedEventArgs, SelectionChangedEventArgs, TrashWindow, ListView

### Community 40 - "Window"
Cohesion: 0.07
Nodes (35): CalledAt, DiscardedCount, Duration, ErrorMessage, Jpy, Model, OutputTokens, PromptTokens (+27 more)

### Community 41 - "JpScratch.Services"
Cohesion: 0.11
Nodes (4): JpScratch.PromptValidation, JpScratch.Proofreading, JpScratch.Services, ProofreadingScheduleValidation

### Community 42 - "Database"
Cohesion: 0.09
Nodes (18): IDisposable, Lock, DateTimeOffset, ApiLogCompactionValidation, int, DatabaseMigrationValidation, string, TestStore (+10 more)

### Community 43 - "ScratchTab"
Cohesion: 0.09
Nodes (14): INotifyPropertyChanged, bool, DateTime, string, TextDocument, ScratchTab, bool, DispatcherTimer (+6 more)

### Community 44 - "screenshot-main.py"
Cohesion: 0.15
Nodes (19): BITMAPINFO, BITMAPINFOHEADER, capture_bitblt(), capture_print_window(), capture_window(), find_window(), is_blank(), main() (+11 more)

### Community 48 - "App"
Cohesion: 0.14
Nodes (11): Application, ApiKeySource, ApiProvider, bool, Exception, IEnumerable, IReadOnlyList, App (+3 more)

### Community 49 - ".TestDeleteAllTrash"
Cohesion: 0.29
Nodes (3): TrashRepositoryValidation, DateTime, List

### Community 50 - "SelectionChangedEventArgs"
Cohesion: 0.33
Nodes (4): CredentialSourceCombo, PricingHistoryList, SelectionChangedEventArgs, ListView

### Community 51 - ".RunSelfTests"
Cohesion: 0.31
Nodes (4): DateTimeOffset, UsagePeriodValidation, DateTimeOffset, UsagePeriod

### Community 52 - "SingleInstance"
Cohesion: 0.17
Nodes (8): ActivateEventName, EventWaitHandle, Action, string, SingleInstance, Mutex, MutexName, RegisteredWaitHandle

### Community 53 - "AppSettings"
Cohesion: 0.20
Nodes (8): decimal, IReadOnlyList, AppSettings, AppTheme, WindowPositionMode, DispatcherTimer, JsonSerializerOptions, SettingsService

### Community 54 - "TrayIconService"
Cohesion: 0.23
Nodes (7): Icon, NotifyIcon, Dictionary, string, TrayIconService, TrayIconState, TrayIconStateResolver

### Community 55 - "RoutedEventArgs"
Cohesion: 0.13
Nodes (13): PricingHistoryRow, CancelButton, DeleteStoredKeyButton, OpenFolderButton, PricingAddButton, PricingDeleteButton, PricingEditButton, PricingRestoreButton (+5 more)

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
Cohesion: 0.05
Nodes (38): DescriptionText, StoredKeyStatusText, Window, WindowTitleText, KeyEventArgs, RoutedEventArgs, CredentialSourceDialog, TextBlock (+30 more)

### Community 61 - "JpScratch.Editor"
Cohesion: 0.18
Nodes (6): char, JpScratch.Editor, DocumentColorizingTransformer, DocumentLine, Brush, IdeographicSpaceColorizer

### Community 62 - ".RunSelfTestAsync"
Cohesion: 0.18
Nodes (6): Action, DateTimeOffset, ApiCallRepositoryValidation, StoredApiCall, decimal, CurrencyConversionValidation

### Community 63 - ".TotalsSurviveCompaction"
Cohesion: 0.24
Nodes (5): DateTimeOffset, IReadOnlyList, BillingSeedCommandValidation, DateTimeOffset, ApiLogRetention

### Community 64 - ".ProofreadAsync"
Cohesion: 0.28
Nodes (8): CancellationToken, Func, HttpRequestMessage, HttpResponseMessage, string, Task, OpenAiProofreadingClientValidation, StubHandler

### Community 65 - ".RunProofreadingAsync"
Cohesion: 0.17
Nodes (5): DateTimeOffset, Dictionary, TimeSpan, ProofreadingSchedule, TextAnchor

### Community 66 - "ProofreadingPrompt"
Cohesion: 0.18
Nodes (5): ProofreadingPromptV3Validation, IReadOnlyList, Regex, string, ProofreadingPrompt

### Community 67 - ".EnsureTodayAsync"
Cohesion: 0.43
Nodes (4): DateOnly, DateTimeOffset, Task, FxRateServiceValidation

### Community 68 - "JpScratch.Models"
Cohesion: 0.13
Nodes (6): JpScratch.Views, JpScratch.Models, JpScratch, JpScratch.Infrastructure, ProofreadingModelCatalogValidation, BillingHistoryDisplayRow

### Community 70 - ".FormatInclusive"
Cohesion: 0.23
Nodes (7): DateTimeOffset, CustomDateRangeParserValidation, DateTimeOffset, From, To, CustomDateRangeParser, Result

### Community 71 - "ModelBenchmarkReport.cs"
Cohesion: 0.28
Nodes (12): double, int, IReadOnlyList, BenchmarkFxRate, BenchmarkModelInfo, BenchmarkModelSummary, BenchmarkProtectionSummary, BenchmarkReport (+4 more)

### Community 72 - "AnthropicProofreadingClient"
Cohesion: 0.16
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, AnthropicProofreadingClient

### Community 73 - "PlamoProofreadingClient"
Cohesion: 0.15
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, PlamoProofreadingClient

### Community 74 - "gui-settings-test.py"
Cohesion: 0.27
Nodes (12): class_name(), click_settings_button(), click_trash_button(), close_window(), dialog_details(), error_dialogs(), find_window(), main() (+4 more)

### Community 75 - ".HotkeyBox_LostFocus"
Cohesion: 0.38
Nodes (4): TextBox, CopyHideHotkeyBox, ToggleHotkeyBox, KeyEventArgs

### Community 76 - "BillingHistoryWindow"
Cohesion: 0.16
Nodes (9): bool, DateTimeOffset, From, List, RoutedEventArgs, SelectionChangedEventArgs, To, BillingHistoryWindow (+1 more)

### Community 77 - "Window"
Cohesion: 0.18
Nodes (12): DeletedAtText, LineCountDisplay, Tab.Title, DeleteButton, EmptyTrashButton, PreviewBox, RestoreButton, SummaryText (+4 more)

### Community 78 - "state_manager.ps1"
Cohesion: 0.22
Nodes (6): Add-OrSetProperty(), Assert-State(), Ensure-ArrayProperty(), Get-IsoNow(), Read-State(), Write-State()

### Community 79 - "GeminiProofreadingClient"
Cohesion: 0.15
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, GeminiProofreadingClient

### Community 80 - "OpenAiProofreadingClient"
Cohesion: 0.15
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, OpenAiProofreadingClient

### Community 81 - "ModelBenchmarkValidation"
Cohesion: 0.29
Nodes (3): IReadOnlyList, JsonSerializerOptions, ModelBenchmarkValidation

### Community 82 - "CLAUDE.md"
Cohesion: 0.18
Nodes (4): App.xaml.cs, Graphify Integration, PromptValidation, ProofreadingModelCatalog

### Community 83 - "TrayIconStateValidation"
Cohesion: 0.29
Nodes (4): IsPng, IEnumerable, Size, TrayIconStateValidation

### Community 84 - "AppPaths"
Cohesion: 0.24
Nodes (3): string, AppPaths, AppPathsValidation

### Community 85 - "ProofreadingInlineDiffElement"
Cohesion: 0.21
Nodes (10): bool, Brush, double, DrawingContext, Point, TextLine, ProofreadingInlineDiffElement, ProofreadingInlineDiffRun (+2 more)

### Community 86 - "TabRoot"
Cohesion: 0.12
Nodes (10): ContextMenuEventArgs, MouseEventArgs, ActiveMarker, Editor, ProofreadingPanel, TabRoot, FrameworkElement, MouseButtonEventArgs (+2 more)

### Community 88 - "settings.json"
Cohesion: 0.22
Nodes (8): advisorModel, hooks, PreToolUse, model, permissions, allow, $schema, mcp__codex__*

### Community 89 - "graphify query"
Cohesion: 0.22
Nodes (9): BFS Traversal Mode, DFS Traversal Mode, graphify explain, graphify path, graphify query, graphify reflect, NetworkX, Constrained Query Expansion (+1 more)

### Community 90 - "FontResolver"
Cohesion: 0.33
Nodes (5): HashSet, IEnumerable, IReadOnlyList, FontResolver, FontFamily

### Community 91 - "CrossTabSearchWindow"
Cohesion: 0.06
Nodes (30): LineNumber, Preview, TabTitle, LineNumber, CrossTabSearchPreviewValidation, End, int, Start (+22 more)

### Community 92 - "GeminiUsage"
Cohesion: 0.36
Nodes (10): Exception, HttpStatusCode, TimeSpan, GeminiAlternativeResult, GeminiClientError, GeminiClientException, GeminiProofreadingResult, GeminiRawTextResult (+2 more)

### Community 93 - "ProofreadingInlineDiff"
Cohesion: 0.38
Nodes (4): IReadOnlyList, ProofreadingInlineDiff, ProofreadingInlineDiffLayout, VisualLineElement

### Community 94 - "UsageAccumulator"
Cohesion: 0.38
Nodes (6): long, bool, decimal, HashSet, DailyTotals, UsageAccumulator

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
Cohesion: 0.18
Nodes (5): ICommand, ClipboardHelper, Action, Func, RelayCommand

### Community 99 - "PromptValidation"
Cohesion: 0.33
Nodes (6): PromptValidation, net10.0-windows, AvalonEdit (6.3.1.120), Microsoft.Data.Sqlite (10.0.10), SQLitePCLRaw.bundle_e_sqlite3 (2.1.12), Microsoft.NET.Sdk

### Community 100 - "HotkeyService"
Cohesion: 0.29
Nodes (5): HwndSource, int, IntPtr, List, HotkeyService

### Community 101 - "HideSuppressionCounter"
Cohesion: 0.29
Nodes (3): HideSuppressionCounterValidation, int, HideSuppressionCounter

### Community 102 - ".ToUsdCost"
Cohesion: 0.25
Nodes (5): CostUsd, Known, PricingQuote, decimal, UsdCostConversion

### Community 103 - "jp-scratch"
Cohesion: 0.33
Nodes (6): net10.0-windows, jp-scratch, AvalonEdit (6.3.1.120), Microsoft.Data.Sqlite (10.0.10), SQLitePCLRaw.bundle_e_sqlite3 (2.1.12), Microsoft.NET.Sdk

### Community 104 - "Prompt Validation App README"
Cohesion: 0.33
Nodes (6): Algorithm Validation (2026-07-29), DocumentDiff Algorithm, full-rewrite-safe Prompt, Prompt Validation App README, Initial Prompt Validation, Prompt Comparison Round 2

### Community 106 - "sync-config.ps1"
Cohesion: 0.60
Nodes (3): Ensure-ObjectProp(), Get-Prop(), Set-Prop()

### Community 107 - "JP Scratch"
Cohesion: 0.40
Nodes (5): Codex Loop, Gemini 3.5 Flash-Lite, GPT-5.6 Luna, JP Scratch, Proofreading UX Fixes Plan

### Community 109 - "JP Scratch Settings"
Cohesion: 0.40
Nodes (5): Settings UI - API & Billing, Settings UI - Editor, Settings UI - General, Settings UI - Learning, JP Scratch Settings

### Community 111 - "InlineDiffParagraphProperties"
Cohesion: 0.22
Nodes (8): TextAlignment, InlineDiffParagraphProperties, FlowDirection, TextMarkerProperties, TextParagraphProperties, TextRunProperties, TextWrapping, Visual

### Community 112 - ".ParseRaw"
Cohesion: 0.32
Nodes (3): JsonElement, Text, Usage

### Community 113 - "3.3 校正機能"
Cohesion: 0.29
Nodes (7): 3.3.1 実行トリガー, 3.3.2 送信範囲, 3.3.3 校正対象, 3.3.4 提案の表示, 3.3.5 提案位置の解決（重要な設計上の論点）, 3.3.6 リアクション, 3.3 校正機能

### Community 114 - "Gemini 3.7 Flash 追補ベンチマーク（2026-08-21）"
Cohesion: 0.18
Nodes (9): Billing History UI, Model Benchmark Scatter Plot (Dark), Proofreading Settings UI, 2026-08-06計測との位置関係, Gemini 3.7 Flash 追補ベンチマーク（2026-08-21）, 実行条件, 文章別, 結果 (+1 more)

### Community 115 - "5. マイルストーン"
Cohesion: 0.29
Nodes (7): 5. マイルストーン, v1 で判明した仕様上の追記, v1 — 常駐エディタ（P-1 の解決）, v2 — 校正（P-2 の解決）, v3 — 学習（P-3 の解決）, v4 — プロバイダー拡張（自動用・手動用の 2 枠）, 実装時の実測値（2026-07-28, Release / framework-dependent）

### Community 116 - "codex"
Cohesion: 0.50
Nodes (3): codex, codex, mcp-server

### Community 118 - "graphify reference: add-watch"
Cohesion: 0.67
Nodes (3): graphify reference: add-watch, graphify.ingest.ingest, graphify.watch

### Community 119 - "Model Performance Metrics"
Cohesion: 0.67
Nodes (3): Model Benchmark Bar Chart (Dark), Model Benchmark Scatter Plot, Model Performance Metrics

### Community 120 - "HotkeySpec"
Cohesion: 0.47
Nodes (3): Key, HotkeySpec, ModifierKeys

### Community 121 - "StubHandler"
Cohesion: 0.40
Nodes (5): CancellationToken, Func, HttpRequestMessage, HttpResponseMessage, StubHandler

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

### Community 142 - "graphify reference: commit hook and native CLAUDE.md integration"
Cohesion: 0.50
Nodes (3): For git commit hook, For native CLAUDE.md integration, graphify reference: commit hook and native CLAUDE.md integration

### Community 143 - "graphify reference: incremental update and cluster-only"
Cohesion: 0.50
Nodes (3): For --cluster-only, For --update (incremental re-extraction), graphify reference: incremental update and cluster-only

### Community 144 - "SingleInstanceValidation"
Cohesion: 0.40
Nodes (3): int, string, SingleInstanceValidation

### Community 145 - "ThemeService"
Cohesion: 0.27
Nodes (4): ResourceDictionary, string, ThemeService, IntPtr

### Community 151 - ".RunAsync"
Cohesion: 0.40
Nodes (4): IReadOnlyList, string, Task, OpenAiCacheProbeCommand

## Knowledge Gaps
- **203 isolated node(s):** `sync-config.sh script`, `$schema`, `model`, `advisorModel`, `mcp__codex__*` (+198 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **28 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainWindow` connect `MainWindow` to `PricingService`, `FxRateService`, `Window`, `ProofreadingInlineDiffGenerator`, `ApiCallRepository`, `ApiProvider`, `StyleGuideRepository`, `ThemeService`, `ProofreadingClientRouter`, `.OnStartup`, `ProofreadingProposal`, `.GetCachedRate`, `.HideWindow`, `UsageLimitServiceValidation`, `.FormatUsd`, `TrashWindow`, `.SetTransientStatus`, `ScratchTab`, `.AlternativeWithReasonMenuItem_Click`, `App`, `AppSettings`, `TrayIconService`, `Window`, `JpScratch.Editor`, `.RunProofreadingAsync`, `JpScratch.Models`, `TabRepository`, `BillingHistoryWindow`, `TabRoot`, `CrossTabSearchWindow`, `HotkeyService`, `HideSuppressionCounter`?**
  _High betweenness centrality (0.249) - this node is a cross-community bridge._
- **Why does `SettingsWindow` connect `SettingsWindow` to `PricingService`, `JpScratch.Models`, `Window`, `.HotkeyBox_LostFocus`, `ApiProvider`, `StyleGuideRepository`, `SelectionChangedEventArgs`, `AppSettings`, `.ApiKeyBox_PasswordChanged`, `RoutedEventArgs`, `Window`, `ProofreadingProposal`?**
  _High betweenness centrality (0.110) - this node is a cross-community bridge._
- **Why does `JpScratch.Services` connect `JpScratch.Services` to `PricingService`, `FxRateService`, `BillingCsvExporterValidation`, `MissedCorrectionDialog`, `ApiCallRepository`, `ThemeService`, `StyleGuideRepository`, `StatusBarUsageFormatterValidation`, `ProofreadingClientRouter`, `ApiUsageDisplayCost`, `.OnStartup`, `ProofreadingProposal`, `UsageLimitServiceValidation`, `.FormatUsd`, `Window`, `TabManager.cs`, `SettingsFieldFormattingValidation`, `.RunSelfTests`, `TrayIconService`, `Window`, `.TotalsSurviveCompaction`, `JpScratch.Models`, `.FormatInclusive`, `ModelBenchmarkReport.cs`, `AnthropicProofreadingClient`, `PlamoProofreadingClient`, `GeminiProofreadingClient`, `OpenAiProofreadingClient`, `CLAUDE.md`, `TrashListItem`, `CrossTabSearchWindow`, `HideSuppressionCounter`, `.ToUsdCost`?**
  _High betweenness centrality (0.083) - this node is a cross-community bridge._
- **What connects `sync-config.sh script`, `$schema`, `model` to the rest of the system?**
  _203 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `.CreateAutomaticPlan` be split into smaller, more focused modules?**
  _Cohesion score 0.05526675786593707 - nodes in this community are weakly interconnected._
- **Should `PricingService` be split into smaller, more focused modules?**
  _Cohesion score 0.07253086419753087 - nodes in this community are weakly interconnected._
- **Should `FxRateService` be split into smaller, more focused modules?**
  _Cohesion score 0.09915966386554621 - nodes in this community are weakly interconnected._