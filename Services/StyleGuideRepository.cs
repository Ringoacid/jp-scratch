using System.Globalization;

namespace JpScratch.Services;

/// <summary>1世代分のスタイルガイド（要件3.4.2、`style_guides`テーブル）。</summary>
internal sealed record StyleGuide(
    long Id,
    DateTimeOffset GeneratedAt,
    string Content,
    int SourceReactions,
    bool IsActive,
    bool IsUserEdited);

/// <summary>
/// 自動生成スタイルガイドの世代管理（要件3.4.2）。生成のたびに新しい世代を追加し、
/// 過去の世代は履歴として残したまま、現在有効な1件だけを <c>is_active</c> で示す。
/// リアクション件数のしきい値判定用カーソルも合わせて持つ（DBスキーマは<c>app_metadata</c>を再利用し、
/// v2で作成済みのテーブルのため<see cref="Database"/>の版は上げない）。
/// </summary>
internal sealed class StyleGuideRepository
{
    private const string ReviewCursorKey = "style_guide_review_cursor";

    private readonly Database _database;
    private readonly Func<DateTimeOffset> _now;

    internal StyleGuideRepository(Database database, Func<DateTimeOffset>? now = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>
    /// 現在プロンプトへ同梱すべきスタイルガイド。無効化されていれば null。
    /// 万一 <c>generated_at</c> が壊れている行があっても（手編集等）、例外にせず
    /// 「有効な世代なし」として扱う（他のリポジトリと同じ「壊れた行は黙って除外する」規約）。
    /// </summary>
    internal StyleGuide? GetActive()
        => _database.Read(
            """
            SELECT id, generated_at, content, source_reactions, is_active, is_user_edited
            FROM style_guides
            WHERE is_active = 1
            ORDER BY id DESC;
            """,
            reader =>
            {
                while (reader.Read())
                {
                    if (ReadRow(reader) is { } guide) return guide;
                }
                return null;
            });

    /// <summary>世代管理の一覧（設定画面の閲覧用）。新しい順。壊れた行は除外する。</summary>
    internal IReadOnlyList<StyleGuide> ListAll()
        => _database.Read(
            """
            SELECT id, generated_at, content, source_reactions, is_active, is_user_edited
            FROM style_guides
            ORDER BY id DESC;
            """,
            reader =>
            {
                List<StyleGuide> guides = [];
                while (reader.Read())
                {
                    if (ReadRow(reader) is { } guide) guides.Add(guide);
                }
                return (IReadOnlyList<StyleGuide>)guides;
            });

    /// <summary>
    /// LLMによる新しい世代を追加し、有効な世代として差し替える。
    /// 過去の世代（ユーザー編集済みも含む）は履歴として残る。
    /// </summary>
    internal StyleGuide Generate(string content, int sourceReactionCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceReactionCount);

        StyleGuide? created = null;
        _database.InTransaction(db =>
        {
            db.Execute("UPDATE style_guides SET is_active = 0;");
            created = db.Read(
                """
                INSERT INTO style_guides (generated_at, content, source_reactions, is_active, is_user_edited)
                VALUES ($generated_at, $content, $source_reactions, 1, 0)
                RETURNING id, generated_at, content, source_reactions, is_active, is_user_edited;
                """,
                reader => reader.Read() && ReadRow(reader) is { } inserted
                    ? inserted
                    : throw new InvalidOperationException("スタイルガイドの生成を記録できませんでした。"),
                ("$generated_at", _now().ToString("O", CultureInfo.InvariantCulture)),
                ("$content", content.Trim()),
                ("$source_reactions", sourceReactionCount));
        });

        return created!;
    }

    /// <summary>
    /// 設定画面からの手編集。新しい世代は作らず、指定した世代（有効・過去のどちらでも）の
    /// 内容だけを差し替える（要件3.4.2「設定画面で全文が読め、ユーザーが自由に編集・削除できる」）。
    /// </summary>
    internal bool UpdateContent(long id, string newContent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(newContent);
        return _database.Execute(
            """
            UPDATE style_guides
            SET content = $content, is_user_edited = 1
            WHERE id = $id;
            """,
            ("$content", newContent.Trim()),
            ("$id", id)) > 0;
    }

    /// <summary>過去の世代を現在有効なものとして復元する。</summary>
    internal bool SetActive(long id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        bool changed = false;
        _database.InTransaction(db =>
        {
            db.Execute("UPDATE style_guides SET is_active = 0;");
            changed = db.Execute(
                "UPDATE style_guides SET is_active = 1 WHERE id = $id;",
                ("$id", id)) > 0;
        });
        return changed;
    }

    /// <summary>スタイルガイドの無効化。世代の履歴は残したまま、プロンプトへの同梱を止める。</summary>
    internal void Deactivate()
        => _database.Execute("UPDATE style_guides SET is_active = 0;");

    /// <summary>世代を完全に削除する。</summary>
    internal bool Delete(long id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        return _database.Execute(
            "DELETE FROM style_guides WHERE id = $id;",
            ("$id", id)) > 0;
    }

    /// <summary>
    /// 前回しきい値を確認した時点のリアクション総数（要件3.4.2「50件たまるごとに」）。
    /// 未記録なら0（初回はリアクション総数がそのまま閾値との比較対象になる）。
    /// </summary>
    internal long GetReviewCursor()
    {
        string? value = _database.Read(
            "SELECT value FROM app_metadata WHERE key = $key;",
            reader => reader.Read() ? reader.GetString(0) : null,
            ("$key", ReviewCursorKey));
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long cursor)
            ? cursor
            : 0L;
    }

    /// <summary>
    /// カーソルを進める。生成を承諾・辞退のどちらでも呼ぶ想定
    /// （辞退時も次のしきい値は「今の総数 + 閾値」になり、以後1件ごとに再確認しない）。
    /// </summary>
    internal void AdvanceReviewCursor(long totalReactionsAtCheck)
        => _database.Execute(
            """
            INSERT INTO app_metadata (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            ("$key", ReviewCursorKey),
            ("$value", totalReactionsAtCheck.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// <c>generated_at</c> が壊れている行は null を返し、除外させる
    /// （<c>ApiCallRepository</c>/<c>FxRateService</c>と同じ「壊れた行は黙って除外する」規約。
    /// ここだけ<c>Parse</c>を使うと、壊れた1行のせいで<see cref="GetActive"/>や
    /// <see cref="ListAll"/>の呼び出し元（校正の実行・設定画面のコンストラクタ）が
    /// 例外で落ちてしまう）。
    /// </summary>
    private static StyleGuide? ReadRow(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        if (!DateTimeOffset.TryParse(
                reader.GetString(1),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset generatedAt))
        {
            return null;
        }

        return new StyleGuide(
            reader.GetInt64(0),
            generatedAt,
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4) != 0,
            reader.GetInt32(5) != 0);
    }
}
