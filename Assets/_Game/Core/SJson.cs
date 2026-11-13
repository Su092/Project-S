using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace S.Core
{
    /// <summary>
    /// 极简 JSON 解析器。为什么不用 JsonUtility：
    /// Core/ 必须零 Unity 依赖（可在 dotnet 下跑断言），而 System.Text.Json 不在 Unity 的
    /// .NET Standard 2.1 默认引用里。这个解析器只服务本项目的 sdata.json，够用且两处都能编译。
    /// </summary>
    public sealed class SJson
    {
        public enum Kind { Null, Bool, Number, String, Array, Object }

        public readonly Kind Type;
        public readonly bool BoolValue;
        public readonly double NumberValue;
        public readonly string StringValue;
        public readonly List<SJson> Array;
        public readonly Dictionary<string, SJson> Obj;

        SJson(Kind t) { Type = t; }
        SJson(bool v) { Type = Kind.Bool; BoolValue = v; }
        SJson(double v) { Type = Kind.Number; NumberValue = v; }
        SJson(string v) { Type = Kind.String; StringValue = v; }
        SJson(List<SJson> a) { Type = Kind.Array; Array = a; }
        SJson(Dictionary<string, SJson> o) { Type = Kind.Object; Obj = o; }

        public static SJson Parse(string text)
        {
            int i = 0;
            var v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i < text.Length) throw new FormatException("JSON 尾部有残留字符，位置 " + i);
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { i++; continue; }
                // 允许 // 行注释，便于在数据文件里写说明
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                break;
            }
        }

        static SJson ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new SJson(ParseString(s, ref i));
                case 't': Expect(s, ref i, "true"); return new SJson(true);
                case 'f': Expect(s, ref i, "false"); return new SJson(false);
                case 'n': Expect(s, ref i, "null"); return new SJson(Kind.Null);
                default: return new SJson(ParseNumber(s, ref i));
            }
        }

        static void Expect(string s, ref int i, string lit)
        {
            if (i + lit.Length > s.Length || string.CompareOrdinal(s, i, lit, 0, lit.Length) != 0)
                throw new FormatException("JSON 期望 " + lit + "，位置 " + i);
            i += lit.Length;
        }

        static SJson ParseObject(string s, ref int i)
        {
            i++; // {
            var map = new Dictionary<string, SJson>();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return new SJson(map); }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("JSON 对象键必须是字符串，位置 " + i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 缺少冒号，位置 " + i);
                i++;
                map[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return new SJson(map); }
                throw new FormatException("JSON 对象未正确闭合，位置 " + i);
            }
        }

        static SJson ParseArray(string s, ref int i)
        {
            i++; // [
            var list = new List<SJson>();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return new SJson(list); }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return new SJson(list); }
                throw new FormatException("JSON 数组未正确闭合，位置 " + i);
            }
        }

        static string ParseString(string s, ref int i)
        {
            i++; // "
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON \\u 转义不完整");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("JSON 未知转义 \\" + e);
                }
            }
            throw new FormatException("JSON 字符串未闭合");
        }

        static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
            var span = s.Substring(start, i - start);
            if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new FormatException("JSON 数字非法: " + span);
            return d;
        }

        // ---- 取值辅助（缺字段给默认值，方便数据文件逐步补齐） ----

        public SJson this[string key]
        {
            get
            {
                if (Type != Kind.Object) return null;
                Obj.TryGetValue(key, out var v);
                return v;
            }
        }

        public bool Has(string key) => Type == Kind.Object && Obj.ContainsKey(key);

        public string Str(string key, string def = null)
        {
            var v = this[key];
            return v != null && v.Type == Kind.String ? v.StringValue : def;
        }

        public double Num(string key, double def = 0)
        {
            var v = this[key];
            return v != null && v.Type == Kind.Number ? v.NumberValue : def;
        }

        public float F(string key, float def = 0) => (float)Num(key, def);

        public int I(string key, int def = 0) => (int)Math.Round(Num(key, def));

        public bool B(string key, bool def = false)
        {
            var v = this[key];
            return v != null && v.Type == Kind.Bool ? v.BoolValue : def;
        }

        public List<SJson> Arr(string key)
        {
            var v = this[key];
            return v != null && v.Type == Kind.Array ? v.Array : s_empty;
        }

        static readonly List<SJson> s_empty = new List<SJson>();
    }
}
