using System.Globalization;
using System.Text;

namespace Poker.Voice
{
    /// <summary>手寫 JSON 的小工具（JsonUtility 不支援 null／巢狀字典，狀態格式要與中繼一致）。</summary>
    internal static class JsonText
    {
        public static void Str(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static string Num(float f) => f.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
