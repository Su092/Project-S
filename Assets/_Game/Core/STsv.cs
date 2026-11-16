using System;
using System.Collections.Generic;
using System.Globalization;

namespace S.Core
{
    /// <summary>
    /// 一行数据。列名 → 原始字符串。**不做类型转换**，由 SCfgLoader 按字段语义取值，
    /// 这样一行里同一列既可以是数字也可以是 `{a,b,c}`，不必为每个字段写一套解析。
    /// </summary>
    public sealed class STsvRow
    {
        public int RowNumber;                 // 在文件中的物理行号（1 起），报错定位用
        public string Source;                 // 表名，报错定位用
        readonly Dictionary<string, string> _values;

        public STsvRow(string source, int rowNumber, Dictionary<string, string> values)
        {
            Source = source;
            RowNumber = rowNumber;
            _values = values ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public bool Has(string field) => _values.ContainsKey(field);

        public string Raw(string field)
        {
            string v;
            return _values.TryGetValue(field, out v) ? v : null;
        }

        // ---- 取值：缺列与空值一律走 fallback，不抛异常 ----
        // 为什么宽松：策划改表时留空是常态（「这列我不关心」）。
        // 空值报错会让人被迫填一堆默认值，反而掩盖真正的笔误。
        // 真正必须存在的字段由 SCfgValidator 单独断言。

        public string Str(string field, string fallback = "")
        {
            string v = Raw(field);
            if (v == null) return fallback;
            v = v.Trim();
            return v.Length == 0 ? fallback : v;
        }

        public int I(string field, int fallback = 0)
        {
            string v = Str(field, null);
            if (v == null) return fallback;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : fallback;
        }

        /// <summary>可空整数：空串返回 null，便于区分「没写」与「写了 0」。</summary>
        public int? INull(string field)
        {
            string v = Str(field, null);
            if (v == null) return null;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : null;
        }

        public float F(string field, float fallback = 0f)
        {
            string v = Str(field, null);
            if (v == null) return fallback;
            return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : fallback;
        }

        public bool B(string field, bool fallback = false)
        {
            string v = Str(field, null);
            if (v == null) return fallback;
            if (v == "1") return true;
            if (v == "0") return false;
            return bool.TryParse(v, out bool r) ? r : fallback;
        }

        /// <summary>`{a,b,c}` → 字符串列表。空串返回空表。</summary>
        public List<string> StrArr(string field)
        {
            return STsvValues.SplitBraceArray(Raw(field));
        }

        /// <summary>`{1,2,3}` → 整数列表。</summary>
        public List<int> IntArr(string field)
        {
            var raw = StrArr(field);
            var list = new List<int>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
                if (int.TryParse(raw[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                    list.Add(v);
            return list;
        }

        /// <summary>`{k=v;k=v}` → 字典。`=` 也接受 `:{` 之类的分隔写法见 STsvValues。</summary>
        public Dictionary<string, string> Map(string field)
        {
            return STsvValues.SplitBraceDict(Raw(field));
        }

        /// <summary>报错用的位置串：`Gun.tsv:14`。</summary>
        public string Where => Source + ":" + RowNumber;
    }

    /// <summary>一张表。</summary>
    public sealed class STsvTable
    {
        public string Name;                   // 表名（文件名，不含扩展名）
        public string[] Fields;               // 列名
        public readonly List<STsvRow> Rows = new List<STsvRow>();
    }

    /// <summary>
    /// 三行表头的 TSV 读取器。零 Unity 依赖，dotnet 与运行时共用。
    ///
    /// 为什么是**三行表头**（对齐 Haunted Match 3 的 Cfg 约定）：
    ///   第 1 行 字段名  —— 代码按它取值，改列序不影响代码
    ///   第 2 行 中文名  —— 给人看，策划直接改数值不会认错列
    ///   第 3 行 类型规则 —— 给校验看，`int` / `"string,unique"` / `{A|B}` 之类
    /// 只有两行表头时早年版本能跑，但「这一列填什么」全靠猜，
    /// 一旦有人手滑把中文名行删了，整张表的列会静默错位。三行是硬约定。
    ///
    /// 关于注释：**不支持** `#` 行内注释 —— 数据里 `#rrggbb` 这类值会被误伤。
    /// 需要说明就写在第 2/3 行，或单独一列 `note`。
    /// </summary>
    public static class STsv
    {
        public const int HeaderRowCount = 3;

        static readonly string[] LineSeparators = { "\r\n", "\n", "\r" };

        public static STsvTable Parse(string tableName, string text)
        {
            if (text == null) throw new FormatException("TSV '" + tableName + "' 内容为空（null）。");

            string[] lines = SplitLines(text);
            if (lines.Length < HeaderRowCount)
                throw new FormatException(
                    "TSV '" + tableName + "' 必须有 " + HeaderRowCount +
                    " 行表头（字段名 / 中文名 / 类型规则），实际只有 " + lines.Length + " 行。");

            var table = new STsvTable { Name = tableName };
            table.Fields = SplitLine(StripBom(lines[0]));

            // 列名去重：重名列会让 Get 静默取到后一个，属最难查的一类错
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < table.Fields.Length; i++)
            {
                string f = table.Fields[i];
                if (string.IsNullOrEmpty(f))
                    throw new FormatException("TSV '" + tableName + "' 第 1 行列名有空列（第 " + (i + 1) + " 列）。");
                if (seen.ContainsKey(f))
                    throw new FormatException(
                        "TSV '" + tableName + "' 列名重复：'" + f + "'（第 " + (seen[f] + 1) + " 与 " +
                        (i + 1) + " 列）。");
                seen[f] = i;
            }

            for (int i = HeaderRowCount; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i].Trim())) continue;   // 空行跳过

                string[] cols = SplitLine(lines[i]);
                if (cols.Length != table.Fields.Length)
                    throw new FormatException(
                        "TSV '" + tableName + "' 第 " + (i + 1) + " 行有 " + cols.Length +
                        " 列，表头是 " + table.Fields.Length + " 列。漏/多一个 Tab 就会这样。");

                var map = new Dictionary<string, string>(table.Fields.Length, StringComparer.Ordinal);
                for (int f = 0; f < table.Fields.Length; f++) map[table.Fields[f]] = cols[f];
                table.Rows.Add(new STsvRow(tableName, i + 1, map));
            }

            return table;
        }

        /// <summary>按行切分并去掉尾部空行（文件末尾的换行不算一行数据）。</summary>
        public static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            string[] lines = text.Split(LineSeparators, StringSplitOptions.None);
            int n = lines.Length;
            while (n > 0 && lines[n - 1].Length == 0) n--;
            if (n != lines.Length) Array.Resize(ref lines, n);
            return lines;
        }

        /// <summary>按 Tab 切列并去掉包裹引号（Excel 导出常见 `"a,b"`）。</summary>
        public static string[] SplitLine(string line)
        {
            string[] cols = line.Split(new[] { '\t' }, StringSplitOptions.None);
            for (int i = 0; i < cols.Length; i++) cols[i] = Unquote(cols[i]);
            return cols;
        }

        /// <summary>去掉一层包裹引号，并把 `""` 还原成 `"`。</summary>
        public static string Unquote(string v)
        {
            if (string.IsNullOrEmpty(v) || v.Length < 2) return v;
            if (v[0] != '"' || v[v.Length - 1] != '"') return v;
            return v.Substring(1, v.Length - 2).Replace("\"\"", "\"");
        }

        static string StripBom(string s)
        {
            return (!string.IsNullOrEmpty(s) && s[0] == '\ufeff') ? s.Substring(1) : s;
        }
    }

    /// <summary>TSV 单元格里的复合值：`{a,b}` 数组、`{k=v;k=v}` 字典。</summary>
    public static class STsvValues
    {
        public static List<string> SplitBraceArray(string raw)
        {
            var list = new List<string>();
            string inner = StripBraces(raw);
            if (inner.Length == 0) return list;

            string[] parts = inner.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string t = STsv.Unquote(parts[i].Trim());
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        public static Dictionary<string, string> SplitBraceDict(string raw)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string inner = StripBraces(raw);
            if (inner.Length == 0) return map;

            string[] pairs = inner.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pairs.Length; i++)
            {
                string pair = pairs[i].Trim();
                int eq = pair.IndexOf('=');
                if (eq <= 0 || eq == pair.Length - 1) continue;   // 畸形项跳过，不炸整张表
                string key = STsv.Unquote(pair.Substring(0, eq).Trim());
                string val = STsv.Unquote(pair.Substring(eq + 1).Trim());
                if (key.Length > 0) map[key] = val;
            }
            return map;
        }

        static string StripBraces(string raw)
        {
            string v = (raw ?? string.Empty).Trim();
            if (v.Length >= 2 && v[0] == '{' && v[v.Length - 1] == '}') return v.Substring(1, v.Length - 2).Trim();
            return v;
        }
    }
}
