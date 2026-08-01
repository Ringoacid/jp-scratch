using System.IO;

namespace JpScratch.Infrastructure;

/// <summary>
/// %APPDATA%\JpScratch 配下のパスを解決する（要件 4）。
/// アプリが壊れてもメモ帳で本文をサルベージできること自体が要件なので、
/// 本文は必ずこの下のプレーンテキストとして置く。
/// </summary>
internal static class AppPaths
{
    /// <summary>
    /// 設定すると、データディレクトリを %APPDATA%\JpScratch の代わりにこのパスへ切り替える。
    /// 課金履歴画面などを実データに触れずに検証するための隔離用途で、通常運用では未設定のまま。
    /// </summary>
    internal const string DataDirEnvironmentVariable = "JPSCRATCH_DATA_DIR";

    public static string Root { get; }

    /// <summary>
    /// <see cref="DataDirEnvironmentVariable"/> が設定されたが、ディレクトリを作成できなかったときに
    /// その環境変数の値を保持する。この場合は黙って実データ（%APPDATA%\JpScratch）へ落とさない:
    /// <see cref="Infrastructure.SingleInstance"/> の名前を実際に採用された Root から導出しているため、
    /// フォールバックすると「実データを書きながら隔離用の名前」になり、実データの常駐インスタンスへ
    /// 呼び戻されず同じ app.db を2プロセスが書く。隔離は開発専用機能なので、作れなかったら
    /// 起動時に明示的に失敗させる（<c>App.OnStartup</c> がメッセージを出して終了する）。
    /// </summary>
    internal static string? IsolationFailure { get; private set; }

    /// <summary>
    /// 隔離実行中か（環境変数が設定され、そのディレクトリが実際に採用された）。
    /// false のときはデータディレクトリが既定（%APPDATA%\JpScratch）であり、
    /// <see cref="SingleInstance"/> の名前も従来の固定名のまま。
    /// </summary>
    internal static bool IsIsolated { get; private set; }

    static AppPaths()
    {
        string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string defaultRoot = Path.Combine(appDataFolder, "JpScratch");
        string? environmentValue = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);

        Root = ResolveRoot(environmentValue, appDataFolder);

        if (!TryCreateDirectories())
        {
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                // 指定パスが作れない（存在しないドライブ X:\ の指定など。GetFullPath は構文的に
                // 正しいパスを弾けないため ResolveRoot を通過する）。型初期化例外にして起動全体を
                // 道連れにせず、Root は指定パスのまま保持して起動時に明示的に失敗させる。
                IsolationFailure = environmentValue;
            }
            // 環境変数未設定で既定パスすら作れない場合は、Root は既定のまま残す。
            // ここで例外を漏らすと型初期化例外になり、ハンドラ登録より前に起動全体が死ぬ。
        }

        IsIsolated = !string.IsNullOrWhiteSpace(environmentValue) &&
                     IsolationFailure is null &&
                     !string.Equals(Root, defaultRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>ルートと必要なサブディレクトリを作る。失敗したら false（呼び出し側が処理する）。</summary>
    private static bool TryCreateDirectories()
    {
        try
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(TabsDir);
            Directory.CreateDirectory(TrashDir);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException
                or NotSupportedException or PathTooLongException or ArgumentException)
        {
            // ArgumentException（不正文字等）も含める。静的コンストラクタで絶対に例外を漏らさない
            // という目的のため、ResolveRoot の catch と同じ広さで拾う。
            return false;
        }
    }

    /// <summary>
    /// データディレクトリを決定する純粋関数。<paramref name="environmentValue"/> が
    /// 空・空白のみ・パスとして不正な場合は既定（<paramref name="appDataFolder"/>\JpScratch）へ
    /// 安全に落ちる。静的コンストラクタでの例外は型初期化例外になり起動全体を道連れにするため、
    /// 副作用（ディレクトリ作成）から切り離してここでテスト可能にしてある。
    /// </summary>
    internal static string ResolveRoot(string? environmentValue, string appDataFolder)
    {
        string defaultRoot = Path.Combine(appDataFolder, "JpScratch");

        if (string.IsNullOrWhiteSpace(environmentValue))
            return defaultRoot;

        try
        {
            string fullPath = Path.GetFullPath(environmentValue.Trim());
            return string.IsNullOrWhiteSpace(fullPath) ? defaultRoot : fullPath;
        }
        catch (Exception)
        {
            // ArgumentException（不正文字等）/ PathTooLongException / NotSupportedException（"C:\\a:b" 等）を
            // まとめて捕捉する。ここで何を投げても型初期化例外になり起動全体が死ぬため、必ず既定へ落とす。
            return defaultRoot;
        }
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string PricingFile => Path.Combine(Root, "pricing.json");
    public static string CredentialsFile => Path.Combine(Root, "credentials.dat");
    public static string DatabaseFile => Path.Combine(Root, "app.db");
    public static string TabsDir => Path.Combine(Root, "tabs");
    public static string TrashDir => Path.Combine(TabsDir, "trash");
    public static string CrashLogFile => Path.Combine(Root, "crash.log");

    public static string TabFile(string tabId) => TabDataFile(TabsDir, tabId);
    public static string TrashFile(string tabId) => TabDataFile(TrashDir, tabId);

    /// <summary>
    /// DB由来のタブIDを安全なファイル名へ変換する。タブIDは新規作成時と同じ32桁の
    /// GUID（"N"形式）だけを許可し、改ざん・破損したDBからのパス逸脱を防ぐ。
    /// </summary>
    internal static string TabDataFile(string directory, string tabId)
    {
        if (!Guid.TryParseExact(tabId, "N", out _))
            throw new InvalidDataException("タブIDの形式が不正です。");

        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(directory, tabId + ".txt"));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("タブの保存先がデータディレクトリ外です。");

        return path;
    }
}
