using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using JpScratch.Infrastructure;
using JpScratch.Models;

namespace JpScratch.Services;

/// <summary>
/// settings.json の読み書き。壊れたファイルで起動不能になるのは常駐アプリとして最悪なので、
/// パースに失敗したら黙って既定値へ倒し、壊れたファイルは .bad へ退避する。
/// </summary>
internal sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly DispatcherTimer _saveTimer;

    public AppSettings Current { get; private set; } = new();

    /// <summary>settings.json が無い状態から起動したか。インストール直後の初期化に使う。</summary>
    public bool IsFirstRun { get; private set; }

    /// <summary>
    /// 読み込みが失敗した理由。<see cref="FileReadFailure.Unreadable"/> は一時的な I/O 失敗
    /// （共有違反・権限エラー等）、<see cref="FileReadFailure.InvalidEncoding"/> は UTF-8 として
    /// 解釈できないバイト列。後者は再起動しても直らないため、起動時の案内文を書き分ける。
    /// </summary>
    public FileReadFailure ReadFailure { get; private set; }

    /// <summary>
    /// 読み込みに失敗したか。ファイル自体は無傷なので、既定値で上書きしてはいけない。
    /// <see cref="AtomicFile.TryReadAllText(string, out string)"/> や
    /// <see cref="TabRepository.LoadBody"/> と同じ「読めなかったものは上書きしない」規約。
    /// この間は <see cref="SaveNow"/> / <see cref="SaveDebounced"/> が書き込みを見送る。
    /// 設定画面から明示的に保存（<see cref="Replace"/>）した時点で解除される。
    /// </summary>
    public bool IsReadFailed => ReadFailure != FileReadFailure.None;

    /// <summary>
    /// 設定が「意図的に」変更されたときだけ発火する（＝設定画面の OK）。
    /// ウィンドウ位置やフォントサイズの記録では発火させない。
    /// あれで通知すると、ウィンドウを動かすたびにホットキーの再登録が走ってしまう。
    /// </summary>
    public event Action<AppSettings>? Changed;

    public SettingsService()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow(notify: false);
        };
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                IsFirstRun = true;
                Current = new AppSettings();
                return;
            }

            // 読み取りは strict UTF-8（AtomicFile と同じ）。File.ReadAllText の既定デコーダは
            // 不正バイトを U+FFFD へ黙って置換するため、外部エディタで CP932 保存された
            // settings.json でも JSON の構造部分は ASCII で通ってしまい、「正常に読めた」ことに
            // なる。その状態で保存すると、カスタム指示などに入っていた日本語が置換文字入りの
            // UTF-8 で上書きされ、復元できなくなる（本文ファイルと同じ規約で失敗させる）。
            if (!AtomicFile.TryReadAllText(AppPaths.SettingsFile, out var json, out var failure))
            {
                // ファイルは無傷なので .bad へ退避してはいけない。退避したうえで既定値化すると、
                // 直後の SaveNow / SaveDebounced が既定値を書き戻し、ホットキー・テーマ・上限額・
                // APIキー取得元がまとめて失われる。この起動の間は「書かない」ことで元の設定を守る。
                ReadFailure = failure;
                Current = new AppSettings();
                Normalize(Current);
                return;
            }

            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // 中身が壊れている（＝直す手段が無い）ときだけ退避して既定値へ倒す。
            QuarantineBrokenFile();
            Current = new AppSettings();
        }

        Normalize(Current);
    }

    /// <summary>頻繁に変わる値（ウィンドウ位置・フォントサイズ）はまとめてから書く。</summary>
    public void SaveDebounced()
    {
        // 読み取りに失敗した起動では、無傷のファイルを既定値で潰さないため何も書かない。
        if (IsReadFailed) return;

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>終了時などに確実に書き出す。通知はしない。</summary>
    public void SaveNow() => SaveNow(notify: false);

    private void SaveNow(bool notify)
    {
        _saveTimer.Stop();
        Normalize(Current);

        if (IsReadFailed)
        {
            // 元の settings.json は無傷のまま残っている。既定値で上書きしない
            // （通知だけは行う。設定画面を開いた場合の反映経路を止めないため）。
            if (notify) Changed?.Invoke(Current);
            return;
        }

        try
        {
            AtomicFile.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 設定が保存できなくても編集中の本文は守りたいので、ここでは落とさない。
        }

        if (notify) Changed?.Invoke(Current);
    }

    /// <summary>設定画面から差し替えるときに使う。ここだけが <see cref="Changed"/> を発火させる。</summary>
    public void Replace(AppSettings settings)
    {
        Current = settings;
        // 設定画面の OK は「この内容で上書きする」というユーザーの明示的な意思表示なので、
        // 読み取り失敗による書き込み抑止をここで解除する（起動時に読めなかった内容より、
        // 画面で確認したうえで押された OK のほうが新しい）。
        ReadFailure = FileReadFailure.None;
        SaveNow(notify: true);
    }

    /// <summary>
    /// v3 までの単一モデル設定（<see cref="AppSettings.ProofreadingModel"/>）を、
    /// 自動用・手動用の 2 枠へ移す（要件 3.5.1）。既存ユーザーが移行前と同じ挙動で起動できるよう、
    /// 旧設定の値を両方へコピーする。移行済みの印として旧プロパティは空にする。
    /// </summary>
    private static void MigrateProofreadingModel(AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.ProofreadingModel)) return;

        if (ProofreadingModelCatalog.IsSupported(s.ProofreadingModel))
        {
            s.AutoProofreadingModel = s.ProofreadingModel.Trim();
            s.ManualProofreadingModel = s.ProofreadingModel.Trim();
        }

        s.ProofreadingModel = "";
    }

    private static int ClampTimeoutSeconds(int seconds)
        => Math.Clamp(
            seconds,
            (int)ProofreadingModelCatalog.MinimumRequestTimeout.TotalSeconds,
            (int)ProofreadingModelCatalog.MaximumRequestTimeout.TotalSeconds);

    private static void Normalize(AppSettings s)
    {
        s.WindowWidth = Math.Clamp(s.WindowWidth, 320, 4000);
        s.WindowHeight = Math.Clamp(s.WindowHeight, 240, 4000);
        s.FontSize = Math.Clamp(s.FontSize, 8, 72);
        s.AutoSaveDebounceMs = Math.Clamp(s.AutoSaveDebounceMs, 200, 10_000);
        s.ProofreadingDebounceMs = Math.Clamp(
            s.ProofreadingDebounceMs,
            500,
            60_000);
        s.ProofreadingMinimumIntervalSeconds = Math.Clamp(
            s.ProofreadingMinimumIntervalSeconds,
            1,
            600);
        MigrateProofreadingModel(s);
        if (!ProofreadingModelCatalog.IsSupported(s.AutoProofreadingModel))
            s.AutoProofreadingModel = ProofreadingModelCatalog.DefaultAutomaticModel;
        if (!ProofreadingModelCatalog.IsSupported(s.ManualProofreadingModel))
            s.ManualProofreadingModel = ProofreadingModelCatalog.DefaultManualModel;
        s.AutoProofreadingTimeoutSeconds = ClampTimeoutSeconds(s.AutoProofreadingTimeoutSeconds);
        s.ManualProofreadingTimeoutSeconds = ClampTimeoutSeconds(s.ManualProofreadingTimeoutSeconds);
        s.TrashRetentionDays = Math.Clamp(s.TrashRetentionDays, 1, 365);
        s.AutoTitleMaxLength = Math.Clamp(s.AutoTitleMaxLength, 4, 60);

        // 0 は「無制限」という正当な値なので Math.Clamp の下限にする（負値だけを無制限へ倒す）。
        if (s.MonthlyLimitUsd < 0m) s.MonthlyLimitUsd = 0m;
        // 閾値は (0, 1] の割合。範囲外・壊れた値は既定の80%へ戻す。
        if (s.MonthlyLimitWarningRatio <= 0m || s.MonthlyLimitWarningRatio > 1m)
            s.MonthlyLimitWarningRatio = 0.80m;

        // 0 は「無期限（圧縮しない）」という正当な値。負値だけを 0 へ倒す。
        // 上限は 1200 か月（100年）。これ以上は事実上の無期限であり、
        // cutoff 計算の AddMonths が DateTime の範囲外へ出ないための歯止めでもある。
        if (s.ApiLogRetentionMonths < 0) s.ApiLogRetentionMonths = 0;
        if (s.ApiLogRetentionMonths > 1200) s.ApiLogRetentionMonths = 1200;

        s.CustomInstruction ??= "";
        // 0件では判定が常に真になり毎回生成を提案してしまうため、下限を1に固定する。
        s.StyleGuideGenerationThreshold = Math.Clamp(s.StyleGuideGenerationThreshold, 1, 10_000);

        // 未知の値（settings.json を手で壊した等）は円表示へ正規化する
        // （proofreading-ux-fixes-plan.md §8.5: 料金表示形式は文字列を直接比較せず enum で持つ）。
        if (!Enum.IsDefined(s.StatusBarCurrency))
            s.StatusBarCurrency = StatusBarCurrencyFormat.Jpy;
    }

    private static void QuarantineBrokenFile()
    {
        try
        {
            var bad = AppPaths.SettingsFile + ".bad";
            if (File.Exists(bad)) File.Delete(bad);
            File.Move(AppPaths.SettingsFile, bad);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
