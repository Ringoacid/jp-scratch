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

            var json = File.ReadAllText(AppPaths.SettingsFile);
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            QuarantineBrokenFile();
            Current = new AppSettings();
        }

        Normalize(Current);
    }

    /// <summary>頻繁に変わる値（ウィンドウ位置・フォントサイズ）はまとめてから書く。</summary>
    public void SaveDebounced()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>終了時などに確実に書き出す。通知はしない。</summary>
    public void SaveNow() => SaveNow(notify: false);

    private void SaveNow(bool notify)
    {
        _saveTimer.Stop();
        Normalize(Current);

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
        SaveNow(notify: true);
    }

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
        if (!ProofreadingModelCatalog.IsSupported(s.ProofreadingModel))
            s.ProofreadingModel = ProofreadingModelCatalog.GeminiModel;
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
