using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace PotatoVN.App.PluginBase.Models;

/// <summary>
/// 插件数据类示例
///
/// 其中，[ObservableProperty] 特性是用来给UI绑定的，如果你的某个数据需要在UI上实时更新（即代码里修改变量会实时反馈到UI上，反之也成立）
/// 对于不需要反应到UI上的数据，可以直接使用普通的属性。
/// </summary>
public partial class PluginData : ObservableRecipient
{
    //标记为ObservableProperty的变量会自动生成一个大写开头的属性，如这里会生成一个TestBool属性，之后应该永远使用这个属性而不是字段本身
    [ObservableProperty] private bool _testBool;

    /// <summary>
    /// 是否尝试使用管理员权限（ETW）进行检测
    /// </summary>
    [ObservableProperty] private bool _useAdminMode = false;

    /// <summary>
    /// 检测确信次数（稳定性循环次数），默认为3
    /// </summary>
    [ObservableProperty] private int _stabilityCycles = 3;

    private bool? _originalAutoDetectValue;

    /// <summary>
    /// 原始的自动检测存档设置
    /// </summary>
    [System.Text.Json.Serialization.JsonInclude]
    public bool? OriginalAutoDetectValue
    {
        get => _originalAutoDetectValue;
        set => SetProperty(ref _originalAutoDetectValue, value);
    }

    /// <summary>
    /// 缓存的外部 JSON 配置（从 GitHub 拉取后保存，避免每次启动都请求）
    /// </summary>
    public string? CachedExternalConfig { get; set; }

    /// <summary>
    /// 上次从 GitHub 拉取配置的 UTC 时间
    /// </summary>
    public DateTime? ConfigLastFetchedUtc { get; set; }
}