using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace PotatoVN.App.PluginBase.Models;

public class SunshineConfig
{
    // --- 1. 环境变量 (Env) ---
    // 兼容：{} (对象), "" (空字符串), null, 甚至 "null" 字符串
    [JsonProperty("env", NullValueHandling = NullValueHandling.Ignore)]
    public JToken? EnvToken { get; set; }

    [JsonIgnore]
    public Dictionary<string, string> Env
    {
        get => JsonSafeParse.GetSafeDictionary<string, string>(EnvToken);
        set => EnvToken = value != null ? JToken.FromObject(value) : null;
    }

    [JsonProperty("apps", NullValueHandling = NullValueHandling.Ignore)]
    public List<SunshineApp> Apps { get; set; } = new();
}

public class SunshineApp
{
    [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("output", NullValueHandling = NullValueHandling.Ignore)]
    public string Output { get; set; } = string.Empty;

    [JsonProperty("cmd", NullValueHandling = NullValueHandling.Ignore)]
    public string Cmd { get; set; } = string.Empty;

    [JsonProperty("index", NullValueHandling = NullValueHandling.Ignore)]
    public int? Index { get; set; }

    // --- 2. 列表类型的字段 (Lists) ---
    // These fields are required by Sunshine API (even if empty) to avoid "No such node" errors.
    // Removed NullValueHandling.Ignore and added backing fields to ensure they default to [] if null.

    // Detached (分离进程列表)
    [JsonProperty("detached")]
    public JToken? DetachedToken
    {
        get => _detachedToken ?? new JArray();
        set => _detachedToken = value;
    }

    private JToken? _detachedToken;

    [JsonIgnore]
    public List<string> Detached
    {
        get => JsonSafeParse.GetSafeList<string>(DetachedToken);
        set => DetachedToken = value != null ? JToken.FromObject(value) : null;
    }

    // PrepCmd (准备命令列表)
    [JsonProperty("prep-cmd")]
    public JToken? PrepCmdToken
    {
        get => _prepCmdToken ?? new JArray();
        set => _prepCmdToken = value;
    }

    private JToken? _prepCmdToken;

    [JsonIgnore]
    public List<SunshinePrepCmd> PrepCmd
    {
        get => JsonSafeParse.GetSafeList<SunshinePrepCmd>(PrepCmdToken);
        set => PrepCmdToken = value != null ? JToken.FromObject(value) : null;
    }

    // MenuCmd (菜单命令列表)
    [JsonProperty("menu-cmd")]
    public JToken? MenuCmdToken
    {
        get => _menuCmdToken ?? new JArray();
        set => _menuCmdToken = value;
    }

    private JToken? _menuCmdToken;

    [JsonIgnore]
    public List<SunshineMenuCmd> MenuCmd
    {
        get => JsonSafeParse.GetSafeList<SunshineMenuCmd>(MenuCmdToken);
        set => MenuCmdToken = value != null ? JToken.FromObject(value) : null;
    }

    // --- 3. 基础类型兼容性处理 ---
    // 虽然 Sunshine 通常用 string "true"/"false"，但为了防止用户手写成 boolean true/false
    // 这里使用 string 接收，但通过 helper 确保转换安全

    [JsonProperty("elevated", NullValueHandling = NullValueHandling.Ignore)]
    public object? ElevatedRaw { get; set; } // 接收 string 或 bool

    [JsonIgnore]
    public string Elevated
    {
        get => ElevatedRaw?.ToString()?.ToLower() ?? "false";
        set => ElevatedRaw = value == "false" ? null : value; // 写：如果是 false，存为 null (不写入)
    }

    [JsonProperty("auto-detach", NullValueHandling = NullValueHandling.Ignore)]
    public object? AutoDetachRaw { get; set; }

    [JsonIgnore]
    public string AutoDetach
    {
        get => AutoDetachRaw?.ToString()?.ToLower() ?? "true";
        set => AutoDetachRaw = value;
    }

    [JsonProperty("wait-all", NullValueHandling = NullValueHandling.Ignore)]
    public string WaitAll { get; set; } = "true";

    [JsonProperty("exit-timeout", NullValueHandling = NullValueHandling.Ignore)]
    public string ExitTimeout { get; set; } = "5";

    [JsonProperty("image-path", NullValueHandling = NullValueHandling.Ignore)]
    public string ImagePath { get; set; } = string.Empty;

    [JsonProperty("working-dir", NullValueHandling = NullValueHandling.Ignore)]
    public string WorkingDir { get; set; } = string.Empty;

    [JsonProperty("exclude-global-prep-cmd", NullValueHandling = NullValueHandling.Ignore)]
    public string ExcludeGlobalPrepCmd { get; set; } = "false";
}

// 定义 PrepCmd
public class SunshinePrepCmd
{
    [JsonProperty("do", NullValueHandling = NullValueHandling.Ignore)]
    public string Do { get; set; } = string.Empty;

    [JsonProperty("undo", NullValueHandling = NullValueHandling.Ignore)]
    public string Undo { get; set; } = string.Empty;

    [JsonProperty("elevated", NullValueHandling = NullValueHandling.Ignore)]
    public string Elevated { get; set; } = "false";
}

// 定义 MenuCmd
public class SunshineMenuCmd
{
    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("cmd", NullValueHandling = NullValueHandling.Ignore)]
    public string Cmd { get; set; } = string.Empty;

    [JsonProperty("elevated", NullValueHandling = NullValueHandling.Ignore)]
    public string Elevated { get; set; } = "false";
}

// --- 核心工具类：安全解析器 ---
// 将这段代码放在同一个文件或公共工具类中
public static class JsonSafeParse
{
    // 安全获取列表：处理 [], null, "", "null", 甚至单个对象的情况
    public static List<T> GetSafeList<T>(JToken? token)
    {
        if (token == null) return new List<T>();

        // 1. 如果是标准的数组
        if (token.Type == JTokenType.Array) return token.ToObject<List<T>>() ?? new List<T>();

        // 2. 某些怪异情况：如果是单个对象，但代码期望 List (容错)
        // 例如用户把 "prep-cmd": [...] 写成了 "prep-cmd": { ... }
        if (token.Type == JTokenType.Object)
            try
            {
                var item = token.ToObject<T>();
                if (item != null) return new List<T> { item };
            }
            catch
            {
                /* 忽略转换失败 */
            }

        // 3. 其它情况 (String, Null, etc.) 全部返回空列表
        return new List<T>();
    }

    // 安全获取字典：处理 {}, null, "", "null"
    public static Dictionary<K, V> GetSafeDictionary<K, V>(JToken? token) where K : notnull
    {
        if (token == null) return new Dictionary<K, V>();

        // 只有当它是真正的 Object 时才转换
        if (token.Type == JTokenType.Object) return token.ToObject<Dictionary<K, V>>() ?? new Dictionary<K, V>();

        // 如果是 String ("") 或其他类型，返回空字典，避免报错
        return new Dictionary<K, V>();
    }
}