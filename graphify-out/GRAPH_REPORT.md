# Graph Report - jp-scratch  (2026-08-21)

## Corpus Check
- 198 files · ~193,099 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2688 nodes · 6091 edges · 148 communities (121 shown, 27 thin omitted)
- Extraction: 92% EXTRACTED · 8% INFERRED · 0% AMBIGUOUS · INFERRED: 457 edges (avg confidence: 0.81)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `48c4718c`
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
- CrossTabSearchWindow
- MissedCorrectionDialog
- .Add
- DocumentDiff
- ApiCallRepository
- MainWindow
- ApiProvider
- StyleGuideRepository
- ResourceDictionary
- ProofreadingClientBase
- state_manager.py
- TabRepository
- ProofreadingClientRouter
- RoutedEventArgs
- .OnStartup
- SettingsWindow
- PricingHistoryChart
- ProofreadingProposal
- ProofreadingModelCatalog
- .StartOfMonth
- .GetCachedRate
- .RunBenchmarkAsync
- .Select
- UsageLimitServiceValidation
- BillingCsvExporterValidation
- ProviderCompletionGuardValidation
- Window
- TrashWindow
- FxRateCompletionValidation
- Window
- JpScratch.PromptValidation
- Database
- ScratchTab
- screenshot-main.py
- .FormatUsd
- .RunProofreadingAsync
- SettingsFieldFormattingValidation
- App
- .TestDeleteAllTrash
- ComboBox
- .EnsureTodayAsync
- SingleInstance
- AppSettings
- TrayIconService
- .Create
- .CollectHits
- build-tray-icons.py
- plot-model-benchmark.py
- .SetTransientStatus
- Window
- JpScratch.Editor
- .RunSelfTests
- .RunCompactionSelfTests
- .ProofreadAsync
- ProofreadingSchedule
- StatusBarUsageFormatterValidation
- Window
- ClipboardHelper.cs
- ApiUsageDisplayCost
- .FormatInclusive
- ReactionRepository
- AnthropicProofreadingClient
- PlamoProofreadingClient
- gui-settings-test.py
- ThemeService
- BillingHistoryWindow
- Window
- state_manager.ps1
- GeminiProofreadingClient
- OpenAiProofreadingClient
- Window
- CLAUDE.md
- TrayIconStateValidation
- AppPaths
- JpScratch.Services
- Editor
- HotkeyService
- settings.json
- graphify query
- FontResolver
- Window
- TabRoot
- GeminiUsage
- UsageAccumulator
- 3.5.1 校正 API の共通契約
- 要件定義書 — 常駐型 日本語スクラッチパッド（仮称: JP Scratch）
- InverseBooleanToVisibilityConverter
- RelayCommand
- PromptValidation
- HotkeySpec
- HideSuppressionCounter
- PricingHistoryEditDialog
- jp-scratch
- Prompt Validation App README
- .OnExit
- sync-config.ps1
- JP Scratch
- VisualTreeHelpers
- JP Scratch Settings
- smoke-test.ps1
- StubHandler
- CrossTabSearchPreviewValidation
- 3.3 校正機能
- Model Benchmark (2026-08-06)
- 5. マイルストーン
- codex
- graphify skill
- graphify reference: add-watch
- Model Performance Metrics
- CurrencyConversionValidation
- SingleInstanceValidation
- sync-config.sh script
- graphify reference: exports
- Cross-Tab Search UI
- Main Editor UI (Dark Mode)
- 3.1 常駐とウィンドウ制御
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
- 3. 機能要件
- graphify reference: commit hook and native CLAUDE.md integration
- graphify reference: incremental update and cluster-only
- BillingHistoryEmptyStateValidation.cs
- 1. 背景と目的
- graphify reference: GitHub clone and cross-repo merge
- graphify reference: transcribe video and audio

## God Nodes (most connected - your core abstractions)
1. `MainWindow` - 175 edges
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

## Communities (148 total, 27 thin omitted)

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

### Community 4 - ".RunSelfTestAsync"
Cohesion: 0.05
Nodes (35): Options, IReadOnlyList, IReadOnlyList, List, Evaluator, CancellationToken, decimal, HttpClient (+27 more)

### Community 5 - "Window"
Cohesion: 0.06
Nodes (56): EffectiveFromText, InputDeltaText, InputText, OutputDeltaText, OutputText, SourceText, StatusText, ApiLogRetentionBox (+48 more)

### Community 6 - "NativeMethods"
Cohesion: 0.11
Nodes (20): APPBARDATA, DllImport, int, IntPtr, APPBARDATA, MONITORINFO, NativeMethods, POINT (+12 more)

### Community 7 - "Window"
Cohesion: 0.06
Nodes (36): BoolToCollapsed, BoolToVisible, IsActive, IsEditing, Title, AcceptAllProposalsButton, AcceptProposalButton, ActiveMarker (+28 more)

### Community 8 - "ProofreadingInlineDiffGenerator"
Cohesion: 0.06
Nodes (33): CultureSpecificCharacterBufferRange, bool, Brush, double, DrawingContext, IReadOnlyList, Point, TextAlignment (+25 more)

### Community 9 - "capture-docs-screenshots.py"
Cohesion: 0.09
Nodes (43): activate_first_tab(), AppSession, BITMAPINFO, BITMAPINFOHEADER, build_tabs(), _capture(), capture_window(), click() (+35 more)

### Community 10 - "GeminiProofreadingClientValidation"
Cohesion: 0.22
Nodes (10): CancellationToken, Func, HttpClient, HttpRequestMessage, HttpResponseMessage, string, Task, GeminiProofreadingClientValidation (+2 more)

### Community 11 - "CrossTabSearchWindow"
Cohesion: 0.16
Nodes (11): JumpButton, ResultsList, SearchButton, TermBox, KeyEventArgs, MouseButtonEventArgs, RoutedEventArgs, CrossTabSearchWindow (+3 more)

### Community 12 - "MissedCorrectionDialog"
Cohesion: 0.08
Nodes (20): MissedCorrectionActionValidation, int, MissedCorrectionAction, MissedCorrectionKind, MissedCorrectionPreview, TextDecorationCollection, CorrectedBox, ExecuteButton (+12 more)

### Community 13 - ".Add"
Cohesion: 0.14
Nodes (12): Day1, Day2, MidDay, OldDay, DateOnly, DateTimeOffset, IReadOnlyList, BillingSeedCommand (+4 more)

### Community 14 - "DocumentDiff"
Cohesion: 0.14
Nodes (16): DiffKind, DiffOperation, Dictionary, double, IEnumerable, int, IReadOnlyDictionary, IReadOnlyList (+8 more)

### Community 15 - "ApiCallRepository"
Cohesion: 0.10
Nodes (26): DailyKey, DailyTotals, InClause, IReadOnlyCollection, Name, Parameters, SeedRow, DateOnly (+18 more)

### Community 16 - "MainWindow"
Cohesion: 0.08
Nodes (14): DataObjectSettingDataEventArgs, bool, Brush, DateOnly, DateTime, DateTimeOffset, decimal, DispatcherTimer (+6 more)

### Community 17 - "ApiProvider"
Cohesion: 0.14
Nodes (12): Action, byte, ApiKeySource, ApiProvider, CredentialServiceValidation, Func, int, string (+4 more)

### Community 18 - "StyleGuideRepository"
Cohesion: 0.10
Nodes (13): DateTimeOffset, Func, IReadOnlyList, SqliteDataReader, string, StyleGuide, StyleGuideRepository, StyleGuideActivateButton (+5 more)

### Community 19 - "ResourceDictionary"
Cohesion: 0.07
Nodes (32): IsDropDownOpen, View.Columns, Arrow, Bd, Box, Check, Checked, CheckStates (+24 more)

### Community 20 - "ProofreadingClientBase"
Cohesion: 0.13
Nodes (15): bool, CancellationToken, Func, HttpClient, HttpRequestMessage, HttpResponseMessage, HttpStatusCode, IReadOnlyList (+7 more)

### Community 21 - "state_manager.py"
Cohesion: 0.28
Nodes (31): Any, ArgumentParser, atomic_write_state(), build_parser(), cmd_add_approval(), cmd_add_plan(), cmd_add_report(), cmd_add_review() (+23 more)

### Community 22 - "TabRepository"
Cohesion: 0.28
Nodes (3): IEnumerable, string, TabRepository

### Community 23 - "ProofreadingClientRouter"
Cohesion: 0.16
Nodes (10): CancellationToken, IReadOnlyList, Task, CancellationToken, Dictionary, IReadOnlyList, string, Task (+2 more)

### Community 24 - "RoutedEventArgs"
Cohesion: 0.13
Nodes (13): PricingHistoryRow, CancelButton, DeleteStoredKeyButton, OpenFolderButton, PricingAddButton, PricingDeleteButton, PricingEditButton, PricingRestoreButton (+5 more)

### Community 25 - ".OnStartup"
Cohesion: 0.10
Nodes (6): EventArgs, string, StartupRegistration, StartupEventArgs, CancelEventArgs, KeyEventArgs

### Community 26 - "SettingsWindow"
Cohesion: 0.08
Nodes (16): ComboBox, TextBlock, TextBox, CopyHideHotkeyBox, OkButton, PricingModelCombo, ToggleHotkeyBox, bool (+8 more)

### Community 27 - "PricingHistoryChart"
Cohesion: 0.12
Nodes (17): Brush, DateOnly, double, DrawingContext, Func, IEnumerable, IReadOnlyList, Point (+9 more)

### Community 28 - "ProofreadingProposal"
Cohesion: 0.13
Nodes (10): DocumentChangeEventArgs, TextAnchor, ProofreadingProposal, ProposalState, bool, IReadOnlyList, List, TextDocument (+2 more)

### Community 29 - "ProofreadingModelCatalog"
Cohesion: 0.13
Nodes (8): Automatic, Manual, Dictionary, string, TimeSpan, ProofreadingModelCatalog, ApiKeyBox, PasswordBox

### Community 30 - ".StartOfMonth"
Cohesion: 0.27
Nodes (4): DateTimeOffset, UsagePeriodValidation, DateTimeOffset, UsagePeriod

### Community 31 - ".GetCachedRate"
Cohesion: 0.12
Nodes (10): ApiUsageCost, FailedApiCallRecord, RecordedApiCall, Exception, IReadOnlyList, TimeSpan, ApiUsageCost, FailedApiCallRecord (+2 more)

### Community 32 - ".RunBenchmarkAsync"
Cohesion: 0.05
Nodes (39): BenchmarkOptions, CostUsd, Known, DateOnly, IReadOnlyList, CatalogPricingHistoryEntry, EffectiveModelPricing, ModelDescriptor (+31 more)

### Community 33 - ".Select"
Cohesion: 0.08
Nodes (20): FewShotSelectorValidation, IReadOnlyList, string, Task, OpenAiCacheProbeCommand, ProofreadingPromptV3Validation, DateTimeOffset, HashSet (+12 more)

### Community 34 - "UsageLimitServiceValidation"
Cohesion: 0.15
Nodes (7): DateTimeOffset, UsageLimitServiceValidation, DateTimeOffset, string, UsageLimitNotificationTracker, UsageLimitService, UsageLimitState

### Community 35 - "BillingCsvExporterValidation"
Cohesion: 0.14
Nodes (8): Encoding, List, BillingCsvExporterValidation, DateTimeOffset, IEnumerable, IReadOnlyList, string, BillingCsvExporter

### Community 36 - "ProviderCompletionGuardValidation"
Cohesion: 0.13
Nodes (14): Body, Case, HttpMessageHandler, Label, CancellationToken, Func, HttpClient, HttpRequestMessage (+6 more)

### Community 37 - "Window"
Cohesion: 0.12
Nodes (18): CalledDateText, CompletableCountText, RateText, UncompletableCountText, ApplyButton, CancelButton, FetchButton, MessageText (+10 more)

### Community 38 - "TrashWindow"
Cohesion: 0.16
Nodes (8): ResultsList, KeyEventArgs, MouseButtonEventArgs, ObservableCollection, RoutedEventArgs, SelectionChangedEventArgs, TrashWindow, ListView

### Community 39 - "FxRateCompletionValidation"
Cohesion: 0.24
Nodes (6): Clock, DateOnly, DateTimeOffset, Task, Clock, FxRateCompletionValidation

### Community 40 - "Window"
Cohesion: 0.07
Nodes (35): CalledAt, DiscardedCount, Duration, ErrorMessage, Jpy, Model, OutputTokens, PromptTokens (+27 more)

### Community 41 - "JpScratch.PromptValidation"
Cohesion: 0.09
Nodes (6): JpScratch.PromptValidation, JpScratch.Proofreading, ApiUsageDisplayFormatterValidation, HideSuppressionCounterValidation, ProofreadingModelCatalogValidation, StyleGuideRepositoryValidation

### Community 42 - "Database"
Cohesion: 0.10
Nodes (15): IDisposable, Lock, int, DatabaseMigrationValidation, string, TestStore, string, TestStore (+7 more)

### Community 43 - "ScratchTab"
Cohesion: 0.10
Nodes (13): INotifyPropertyChanged, bool, DateTime, string, TextDocument, ScratchTab, bool, DispatcherTimer (+5 more)

### Community 44 - "screenshot-main.py"
Cohesion: 0.15
Nodes (19): BITMAPINFO, BITMAPINFOHEADER, capture_bitblt(), capture_print_window(), capture_window(), find_window(), is_blank(), main() (+11 more)

### Community 45 - ".FormatUsd"
Cohesion: 0.16
Nodes (8): StatusBarCurrencyFormat, ApiCallLog, ApiCallUsageSummary, StatusBarDisplayOptions, StatusBarUsageFormatter, DateOnly, UsageFormatting, BillingHistoryDisplayRow

### Community 48 - "App"
Cohesion: 0.14
Nodes (11): Application, ApiKeySource, ApiProvider, bool, IEnumerable, IReadOnlyList, App, DispatcherUnhandledExceptionEventArgs (+3 more)

### Community 49 - ".TestDeleteAllTrash"
Cohesion: 0.22
Nodes (5): TrashRepositoryValidation, DateTime, List, DateTime, TrashListItem

### Community 50 - "ComboBox"
Cohesion: 0.15
Nodes (12): AutoProofreadingModelCombo, CredentialSourceCombo, FontCombo, ManualProofreadingModelCombo, PositionCombo, PricingHistoryList, StatusBarCurrencyCombo, StyleGuideHistoryCombo (+4 more)

### Community 51 - ".EnsureTodayAsync"
Cohesion: 0.27
Nodes (9): CancellationToken, DateOnly, DateTimeOffset, Func, HttpRequestMessage, HttpResponseMessage, Task, FxRateServiceValidation (+1 more)

### Community 52 - "SingleInstance"
Cohesion: 0.17
Nodes (8): ActivateEventName, EventWaitHandle, Action, string, SingleInstance, Mutex, MutexName, RegisteredWaitHandle

### Community 53 - "AppSettings"
Cohesion: 0.24
Nodes (6): decimal, IReadOnlyList, AppSettings, DispatcherTimer, JsonSerializerOptions, SettingsService

### Community 54 - "TrayIconService"
Cohesion: 0.23
Nodes (7): Icon, NotifyIcon, Dictionary, string, TrayIconService, TrayIconState, TrayIconStateResolver

### Community 55 - ".Create"
Cohesion: 0.25
Nodes (4): JsonSerializerOptions, DocumentDiffValidation, DocumentChange, DocumentDiffResult

### Community 56 - ".CollectHits"
Cohesion: 0.18
Nodes (8): LineNumber, End, int, Start, CrossTabSearchPreview, List, Regex, CrossTabHit

### Community 57 - "build-tray-icons.py"
Cohesion: 0.17
Nodes (12): badge_triangle(), build(), draw_bar(), encode_dib(), main(), Image, 32bpp の DIB エントリ（BITMAPINFOHEADER + BGRA + AND マスク）を作る。 Pillow の ICO…, サイズ構成と「256px だけ PNG」を確認する。ここが崩れると NotifyIcon が絵を出せない。 (+4 more)

### Community 58 - "plot-model-benchmark.py"
Cohesion: 0.25
Nodes (15): draw_bars(), draw_scatter(), latest_report(), load(), main(), markdown_table(), pareto(), Path (+7 more)

### Community 60 - "Window"
Cohesion: 0.15
Nodes (12): CostNoticeText, DescriptionText, ReasonBox, ReasonSuggestionBox, Window, RoutedEventArgs, SelectionChangedEventArgs, ProofreadingReasonDialog (+4 more)

### Community 61 - "JpScratch.Editor"
Cohesion: 0.18
Nodes (6): char, JpScratch.Editor, DocumentColorizingTransformer, DocumentLine, Brush, IdeographicSpaceColorizer

### Community 62 - ".RunSelfTests"
Cohesion: 0.24
Nodes (4): Action, DateTimeOffset, ApiCallRepositoryValidation, StoredApiCall

### Community 63 - ".RunCompactionSelfTests"
Cohesion: 0.23
Nodes (4): DateTimeOffset, ApiLogCompactionValidation, DateTimeOffset, ApiLogRetention

### Community 64 - ".ProofreadAsync"
Cohesion: 0.28
Nodes (8): CancellationToken, Func, HttpRequestMessage, HttpResponseMessage, string, Task, OpenAiProofreadingClientValidation, StubHandler

### Community 65 - "ProofreadingSchedule"
Cohesion: 0.24
Nodes (5): ProofreadingScheduleValidation, DateTimeOffset, Dictionary, TimeSpan, ProofreadingSchedule

### Community 67 - "Window"
Cohesion: 0.18
Nodes (14): DescriptionText, EffectiveDatePicker, InputPriceBox, InputPriceRow, InputUnitText, OutputPriceBox, OutputPriceRow, OutputUnitText (+6 more)

### Community 69 - "ApiUsageDisplayCost"
Cohesion: 0.45
Nodes (3): IReadOnlyList, ApiUsageDisplayCost, ApiUsageDisplayFormatter

### Community 70 - ".FormatInclusive"
Cohesion: 0.21
Nodes (7): DateTimeOffset, CustomDateRangeParserValidation, DateTimeOffset, From, To, CustomDateRangeParser, Result

### Community 71 - "ReactionRepository"
Cohesion: 0.23
Nodes (6): FewShotCandidate, ReactionRepositoryValidation, IReadOnlyList, ProofreadingReaction, ReactionRepository, RejectionRateBucket

### Community 72 - "AnthropicProofreadingClient"
Cohesion: 0.18
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, AnthropicProofreadingClient

### Community 73 - "PlamoProofreadingClient"
Cohesion: 0.16
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, PlamoProofreadingClient

### Community 74 - "gui-settings-test.py"
Cohesion: 0.27
Nodes (12): class_name(), click_settings_button(), click_trash_button(), close_window(), dialog_details(), error_dialogs(), find_window(), main() (+4 more)

### Community 75 - "ThemeService"
Cohesion: 0.24
Nodes (6): AppTheme, WindowPositionMode, ResourceDictionary, string, ThemeService, IntPtr

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
Cohesion: 0.18
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, GeminiProofreadingClient

### Community 80 - "OpenAiProofreadingClient"
Cohesion: 0.15
Nodes (8): Func, HttpClient, HttpRequestMessage, int, JsonElement, string, Uri, OpenAiProofreadingClient

### Community 81 - "Window"
Cohesion: 0.24
Nodes (8): DescriptionText, StoredKeyStatusText, Window, WindowTitleText, KeyEventArgs, RoutedEventArgs, CredentialSourceDialog, TextBlock

### Community 82 - "CLAUDE.md"
Cohesion: 0.17
Nodes (4): App.xaml.cs, Graphify Integration, PromptValidation, ProofreadingModelCatalog

### Community 83 - "TrayIconStateValidation"
Cohesion: 0.26
Nodes (4): IsPng, IEnumerable, Size, TrayIconStateValidation

### Community 84 - "AppPaths"
Cohesion: 0.24
Nodes (3): string, AppPaths, AppPathsValidation

### Community 85 - "JpScratch.Services"
Cohesion: 0.14
Nodes (5): JpScratch.Views, JpScratch.Models, JpScratch.Services, JpScratch, JpScratch.Infrastructure

### Community 86 - "Editor"
Cohesion: 0.20
Nodes (6): ContextMenuEventArgs, MouseWheelEventArgs, Editor, TabScroller, ScrollViewer, TextEditor

### Community 87 - "HotkeyService"
Cohesion: 0.26
Nodes (6): HwndSource, int, IntPtr, IReadOnlyList, List, HotkeyService

### Community 88 - "settings.json"
Cohesion: 0.22
Nodes (8): advisorModel, hooks, PreToolUse, model, permissions, allow, $schema, mcp__codex__*

### Community 89 - "graphify query"
Cohesion: 0.22
Nodes (9): BFS Traversal Mode, DFS Traversal Mode, graphify explain, graphify path, graphify query, graphify reflect, NetworkX, Constrained Query Expansion (+1 more)

### Community 90 - "FontResolver"
Cohesion: 0.33
Nodes (5): HashSet, IEnumerable, IReadOnlyList, FontResolver, FontFamily

### Community 91 - "Window"
Cohesion: 0.22
Nodes (10): LineNumber, Preview, TabTitle, IncludeTrashCheck, MatchCaseCheck, RegexCheck, SummaryText, Window (+2 more)

### Community 92 - "TabRoot"
Cohesion: 0.24
Nodes (4): MouseEventArgs, TabRoot, FrameworkElement, MouseButtonEventArgs

### Community 93 - "GeminiUsage"
Cohesion: 0.36
Nodes (10): Exception, HttpStatusCode, TimeSpan, GeminiAlternativeResult, GeminiClientError, GeminiClientException, GeminiProofreadingResult, GeminiRawTextResult (+2 more)

### Community 94 - "UsageAccumulator"
Cohesion: 0.38
Nodes (6): long, bool, decimal, HashSet, DailyTotals, UsageAccumulator

### Community 95 - "3.5.1 校正 API の共通契約"
Cohesion: 0.20
Nodes (10): 3.5.1 校正 API の共通契約, Anthropic Messages API, Gemini API, OpenAI Responses API, Preferred Networks PLaMo API（OpenAI 互換）, タイムアウトと再試行, 完了判定（データ保全上の必須要件）, 思考・推論の設定 (+2 more)

### Community 96 - "要件定義書 — 常駐型 日本語スクラッチパッド（仮称: JP Scratch）"
Cohesion: 0.22
Nodes (8): 2.1 非機能要件, 2. 技術スタック, 4.1 SQLite スキーマ, 4. データモデル, 6. 想定外・非対象（v3 まで）, 7. リスクと対策, 8. 決定事項の一覧（本書の根拠）, 要件定義書 — 常駐型 日本語スクラッチパッド（仮称: JP Scratch）

### Community 97 - "InverseBooleanToVisibilityConverter"
Cohesion: 0.38
Nodes (4): CultureInfo, InverseBooleanToVisibilityConverter, IValueConverter, Type

### Community 98 - "RelayCommand"
Cohesion: 0.29
Nodes (4): ICommand, Action, Func, RelayCommand

### Community 99 - "PromptValidation"
Cohesion: 0.33
Nodes (6): PromptValidation, net10.0-windows, AvalonEdit (6.3.1.120), Microsoft.Data.Sqlite (10.0.10), SQLitePCLRaw.bundle_e_sqlite3 (2.1.12), Microsoft.NET.Sdk

### Community 100 - "HotkeySpec"
Cohesion: 0.47
Nodes (3): Key, HotkeySpec, ModifierKeys

### Community 102 - "PricingHistoryEditDialog"
Cohesion: 0.38
Nodes (4): bool, DateOnly, RoutedEventArgs, PricingHistoryEditDialog

### Community 103 - "jp-scratch"
Cohesion: 0.33
Nodes (6): net10.0-windows, jp-scratch, AvalonEdit (6.3.1.120), Microsoft.Data.Sqlite (10.0.10), SQLitePCLRaw.bundle_e_sqlite3 (2.1.12), Microsoft.NET.Sdk

### Community 104 - "Prompt Validation App README"
Cohesion: 0.33
Nodes (6): Algorithm Validation (2026-07-29), DocumentDiff Algorithm, full-rewrite-safe Prompt, Prompt Validation App README, Initial Prompt Validation, Prompt Comparison Round 2

### Community 105 - ".OnExit"
Cohesion: 0.25
Nodes (3): Exception, ExitEventArgs, UnhandledExceptionEventArgs

### Community 106 - "sync-config.ps1"
Cohesion: 0.60
Nodes (3): Ensure-ObjectProp(), Get-Prop(), Set-Prop()

### Community 107 - "JP Scratch"
Cohesion: 0.40
Nodes (5): Codex Loop, Gemini 3.5 Flash-Lite, GPT-5.6 Luna, JP Scratch, Proofreading UX Fixes Plan

### Community 109 - "JP Scratch Settings"
Cohesion: 0.40
Nodes (5): Settings UI - API & Billing, Settings UI - Editor, Settings UI - General, Settings UI - Learning, JP Scratch Settings

### Community 111 - "StubHandler"
Cohesion: 0.29
Nodes (7): CancellationToken, Func, HttpRequestMessage, HttpResponseMessage, List, Uri, StubHandler

### Community 113 - "3.3 校正機能"
Cohesion: 0.29
Nodes (7): 3.3.1 実行トリガー, 3.3.2 送信範囲, 3.3.3 校正対象, 3.3.4 提案の表示, 3.3.5 提案位置の解決（重要な設計上の論点）, 3.3.6 リアクション, 3.3 校正機能

### Community 114 - "Model Benchmark (2026-08-06)"
Cohesion: 0.50
Nodes (4): Billing History UI, Model Benchmark Scatter Plot (Dark), Proofreading Settings UI, Model Benchmark (2026-08-06)

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

### Community 121 - "SingleInstanceValidation"
Cohesion: 0.40
Nodes (3): int, string, SingleInstanceValidation

### Community 126 - "3.1 常駐とウィンドウ制御"
Cohesion: 0.40
Nodes (5): 3.1.1 タスクトレイ, 3.1.2 表示位置とサイズ, 3.1.3 自動非表示と、隠すときの挙動, 3.1.4 グローバルホットキー, 3.1 常駐とウィンドウ制御

### Community 127 - "3.2 エディタ"
Cohesion: 0.40
Nodes (5): 3.2.1 タブ, 3.2.2 編集機能, 3.2.3 検索・置換, 3.2.4 永続化（スクラッチパッド型）, 3.2 エディタ

### Community 139 - "3.4 学習機能（文体の適応）"
Cohesion: 0.40
Nodes (5): 3.4.1 リアクション履歴の few-shot 同梱, 3.4.2 スタイルガイドの自動生成, 3.4.3 ユーザー手書きのカスタム指示欄, 3.4.4 プロンプト構成（送信順）, 3.4 学習機能（文体の適応）

### Community 140 - "3.5 API 連携"
Cohesion: 0.40
Nodes (5): 3.5.2 トークン数と料金, 3.5.3 為替レート（Frankfurter API）, 3.5.4 モデルの確認状況, 3.5.5 API キーの管理, 3.5 API 連携

### Community 141 - "3. 機能要件"
Cohesion: 0.40
Nodes (5): 3.6.1 表示箇所, 3.6.2 課金履歴画面（実装済み。実機確認済み）, 3.6.3 課金ガード（実装済み。実機確認済み）, 3.6 コスト表示, 3. 機能要件

### Community 142 - "graphify reference: commit hook and native CLAUDE.md integration"
Cohesion: 0.50
Nodes (3): For git commit hook, For native CLAUDE.md integration, graphify reference: commit hook and native CLAUDE.md integration

### Community 143 - "graphify reference: incremental update and cluster-only"
Cohesion: 0.50
Nodes (3): For --cluster-only, For --update (incremental re-extraction), graphify reference: incremental update and cluster-only

### Community 145 - "1. 背景と目的"
Cohesion: 0.50
Nodes (4): 1.1 解決したい問題, 1.2 目的, 1.3 設計原則, 1. 背景と目的

## Knowledge Gaps
- **193 isolated node(s):** `sync-config.sh script`, `$schema`, `model`, `advisorModel`, `mcp__codex__*` (+188 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **27 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `MainWindow` connect `MainWindow` to `PricingService`, `FxRateService`, `NativeMethods`, `Window`, `ProofreadingInlineDiffGenerator`, `CrossTabSearchWindow`, `ApiCallRepository`, `ApiProvider`, `StyleGuideRepository`, `TabRepository`, `.OnStartup`, `ProofreadingProposal`, `.GetCachedRate`, `.RunBenchmarkAsync`, `UsageLimitServiceValidation`, `TrashWindow`, `FxRateCompletionValidation`, `ScratchTab`, `.FormatUsd`, `.RunProofreadingAsync`, `App`, `AppSettings`, `TrayIconService`, `.SetTransientStatus`, `Window`, `JpScratch.Editor`, `ProofreadingSchedule`, `ReactionRepository`, `ThemeService`, `BillingHistoryWindow`, `JpScratch.Services`, `Editor`, `HotkeyService`, `TabRoot`, `HideSuppressionCounter`?**
  _High betweenness centrality (0.249) - this node is a cross-community bridge._
- **Why does `SettingsWindow` connect `SettingsWindow` to `PricingService`, `Window`, `ReactionRepository`, `ApiProvider`, `StyleGuideRepository`, `ComboBox`, `JpScratch.Services`, `AppSettings`, `RoutedEventArgs`, `Window`, `ProofreadingModelCatalog`?**
  _High betweenness centrality (0.111) - this node is a cross-community bridge._
- **Why does `JpScratch.Services` connect `JpScratch.Services` to `PricingService`, `FxRateService`, `MissedCorrectionDialog`, `.Add`, `ApiCallRepository`, `BillingHistoryEmptyStateValidation.cs`, `StyleGuideRepository`, `.OnStartup`, `.StartOfMonth`, `.RunBenchmarkAsync`, `.Select`, `UsageLimitServiceValidation`, `BillingCsvExporterValidation`, `JpScratch.PromptValidation`, `Database`, `.FormatUsd`, `SettingsFieldFormattingValidation`, `TrayIconService`, `.CollectHits`, `.RunSelfTests`, `.RunCompactionSelfTests`, `ApiUsageDisplayCost`, `.FormatInclusive`, `ReactionRepository`, `CLAUDE.md`, `TrayIconStateValidation`, `HideSuppressionCounter`, `CrossTabSearchPreviewValidation`, `CurrencyConversionValidation`?**
  _High betweenness centrality (0.083) - this node is a cross-community bridge._
- **What connects `sync-config.sh script`, `$schema`, `model` to the rest of the system?**
  _193 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `.CreateAutomaticPlan` be split into smaller, more focused modules?**
  _Cohesion score 0.05737234652897304 - nodes in this community are weakly interconnected._
- **Should `PricingService` be split into smaller, more focused modules?**
  _Cohesion score 0.07106293285155074 - nodes in this community are weakly interconnected._
- **Should `FxRateService` be split into smaller, more focused modules?**
  _Cohesion score 0.09659090909090909 - nodes in this community are weakly interconnected._