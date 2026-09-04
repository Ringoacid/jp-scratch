using System.Text;

namespace JpScratch.Editor;

/// <summary>
/// 正規表現モードの置換欄で使うエスケープを、.NET の置換パターンへ渡す前に展開する。
/// <see cref="System.Text.RegularExpressions.Match.Result(string)"/> 自体は
/// <c>\n</c> などを制御文字として扱わないため、検索欄と同じ表記で入力できるよう補う。
/// </summary>
internal static class RegexReplacement
{
    internal static string ExpandEscapes(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains('\\')) return value;

        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }

            var escaped = value[i + 1];
            switch (escaped)
            {
                case 'r':
                    result.Append('\r');
                    i++;
                    break;
                case 'n':
                    result.Append('\n');
                    i++;
                    break;
                case 't':
                    result.Append('\t');
                    i++;
                    break;
                case '\\':
                    result.Append('\\');
                    i++;
                    break;
                default:
                    // 未知のエスケープは文字どおり残す。たとえば \$ は .NET の
                    // 置換構文を変えないため、ドルを文字として出す場合は $$ を使う。
                    result.Append('\\');
                    break;
            }
        }

        return result.ToString();
    }
}
