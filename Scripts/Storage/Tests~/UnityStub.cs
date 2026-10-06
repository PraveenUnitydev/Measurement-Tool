using System; using System.Collections; using System.Collections.Generic; using System.Globalization; using System.Reflection; using System.Text;
namespace UnityEngine {
  // Stand-in for UnityEngine.JsonUtility: public instance fields only, unknown keys ignored,
  // missing keys keep their initialisers, invalid JSON throws, null strings serialise as "".
  public static class JsonUtility {
    public static string ToJson(object o, bool pretty) { return ToJson(o); }
    public static string ToJson(object o) { var sb = new StringBuilder(); Write(sb, o); return sb.ToString(); }
    static void Write(StringBuilder sb, object v) {
      if (v == null) { sb.Append("\"\""); return; }
      var t = v.GetType();
      if (v is string) { WriteStr(sb, (string)v); return; }
      if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
      if (v is int || v is long) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
      if (v is IList) { sb.Append('['); bool f = true; foreach (var x in (IList)v) { if (!f) sb.Append(','); f = false; Write(sb, x); } sb.Append(']'); return; }
      sb.Append('{'); bool first = true;
      foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) { if (!first) sb.Append(','); first = false; WriteStr(sb, fi.Name); sb.Append(':'); Write(sb, fi.GetValue(v)); }
      sb.Append('}');
    }
    static void WriteStr(StringBuilder sb, string s) { sb.Append('"'); foreach (var c in s) { if (c == '"' || c == '\\') sb.Append('\\').Append(c); else if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c); else sb.Append(c); } sb.Append('"'); }

    public static T FromJson<T>(string json) {
      if (json == null) throw new ArgumentNullException("json");
      int i = 0; var tree = ParseValue(json, ref i); Skip(json, ref i);
      if (i != json.Length) throw new ArgumentException("JSON parse error: trailing characters");
      return (T)Bind(tree, typeof(T));
    }
    static object Bind(object node, Type t) {
      if (t == typeof(string)) return node as string ?? "";
      if (t == typeof(bool)) return node is bool && (bool)node;
      if (t == typeof(int)) return node is double ? (int)(double)node : 0;
      if (t == typeof(long)) return node is double ? (long)(double)node : 0L;
      if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) {
        var list = (IList)Activator.CreateInstance(t); var el = t.GetGenericArguments()[0];
        var arr = node as List<object>; if (arr != null) foreach (var x in arr) list.Add(Bind(x, el)); return list; }
      var obj = Activator.CreateInstance(t); var dict = node as Dictionary<string, object>;
      if (dict == null) throw new ArgumentException("JSON parse error: expected object");
      foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) { object v; if (dict.TryGetValue(fi.Name, out v)) fi.SetValue(obj, Bind(v, fi.FieldType)); }
      return obj;
    }
    static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
    static object ParseValue(string s, ref int i) {
      Skip(s, ref i); if (i >= s.Length) throw new ArgumentException("JSON parse error: unexpected end");
      char c = s[i];
      if (c == '{') { i++; var d = new Dictionary<string, object>(); Skip(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return d; }
        while (true) { Skip(s, ref i); var k = ParseString(s, ref i); Skip(s, ref i); Expect(s, ref i, ':'); d[k] = ParseValue(s, ref i); Skip(s, ref i);
          if (i < s.Length && s[i] == ',') { i++; continue; } Expect(s, ref i, '}'); return d; } }
      if (c == '[') { i++; var l = new List<object>(); Skip(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return l; }
        while (true) { l.Add(ParseValue(s, ref i)); Skip(s, ref i); if (i < s.Length && s[i] == ',') { i++; continue; } Expect(s, ref i, ']'); return l; } }
      if (c == '"') return ParseString(s, ref i);
      if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
      if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
      if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
      int st = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
      double n; if (i == st || !double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) throw new ArgumentException("JSON parse error at " + st);
      return n;
    }
    static void Expect(string s, ref int i, char c) { if (i >= s.Length || s[i] != c) throw new ArgumentException("JSON parse error: expected " + c); i++; }
    static string ParseString(string s, ref int i) {
      Expect(s, ref i, '"'); var sb = new StringBuilder();
      while (true) { if (i >= s.Length) throw new ArgumentException("JSON parse error: unterminated string"); char c = s[i++];
        if (c == '"') return sb.ToString();
        if (c == '\\') { char e = s[i++]; if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; } else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e); }
        else sb.Append(c); } }
  }
}
