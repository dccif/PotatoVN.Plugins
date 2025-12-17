using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace PotatoVN.App.PluginBase.Models;

// 1. 根配置
public class SunshineConfig
{
    // 兼容处理：env 可能是字符串 "" (如果你没配过) 也可能是字典 {"KEY": "VAL"}
    [JsonProperty("env")]
    public JToken? EnvToken { get; set; }

    [JsonIgnore]
    public Dictionary<string, string> Env
    {
        get
        {
            if (EnvToken is JObject obj)
                return obj.ToObject<Dictionary<string, string>>() ?? new();
            return new Dictionary<string, string>();
        }
        set => EnvToken = value != null ? JToken.FromObject(value) : null;
    }

    [JsonProperty("apps")]
    public List<SunshineApp> Apps { get; set; } = new();
}

// 2. App 定义
public class SunshineApp
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("output")]
    public string Output { get; set; } = string.Empty;

    [JsonProperty("cmd")]
    public string Cmd { get; set; } = string.Empty;

    [JsonProperty("exclude-global-prep-cmd")]
    public string ExcludeGlobalPrepCmd { get; set; } = "false";

    [JsonProperty("elevated")]
    public string Elevated { get; set; } = "false";

    [JsonProperty("auto-detach")]
    public string AutoDetach { get; set; } = "true";

    [JsonProperty("wait-all")]
    public string WaitAll { get; set; } = "true";

    [JsonProperty("exit-timeout")]
    public string ExitTimeout { get; set; } = "5";

    [JsonProperty("image-path")]
    public string ImagePath { get; set; } = string.Empty;

    [JsonProperty("working-dir")]
    public string WorkingDir { get; set; } = string.Empty;

    // 3. 菜单命令 (menu-cmd)
    // 修复的核心：使用 JToken 接收原始 JSON 数据
    // 这样无论是 数组 [] 还是 字符串 "null"，都不会在反序列化时报错
    [JsonProperty("menu-cmd")]
    public JToken? MenuCmdToken { get; set; }

    // 提供给代码使用的强类型 List
    [JsonIgnore]
    public List<SunshineMenuCmd> MenuCmd
    {
        get
        {
            // 如果 JSON 里是数组，正常转换
            if (MenuCmdToken is JArray arr)
            {
                return arr.ToObject<List<SunshineMenuCmd>>() ?? new();
            }
            // 如果是 "null" 字符串、null 值或其他非数组类型，返回空列表，避免崩溃
            return new List<SunshineMenuCmd>();
        }
        set
        {
            // 写入时，始终将其序列化为标准的 JSON 结构
            if (value != null)
                MenuCmdToken = JToken.FromObject(value);
            else
                MenuCmdToken = null;
        }
    }
}

// 4. 子菜单项定义
public class SunshineMenuCmd
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("cmd")]
    public string Cmd { get; set; } = string.Empty;

    [JsonProperty("elevated")]
    public string Elevated { get; set; } = "false";
}