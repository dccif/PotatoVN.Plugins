using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Models;
using GalgameManager.WinApp.Base.Models.Msgs;
using CommunityToolkit.Mvvm.Messaging;
using PotatoVN.App.PluginBase.Models;
using System;
using System.Threading.Tasks;
using System.Diagnostics;

namespace PotatoVN.App.PluginBase
{
    public partial class Plugin : IPlugin
    {
        private IPotatoVnApi _hostApi = null!;
        private PluginData _data = new ();

        public PluginInfo Info { get; } = new()
        {
            Id = new Guid("a8b3c9d2-4e7f-5a6b-8c9d-1e2f3a4b5c6d"),
            Name = "存档位置探测",
            Description = "游戏存档位置探测器，使用算法实时监控文件变更，定位游戏存档位置。",
        };

        public async Task InitializeAsync(IPotatoVnApi hostApi)
        {
            _hostApi = hostApi;
            XamlResourceLocatorFactory.packagePath = _hostApi.GetPluginPath();
            var dataJson = await _hostApi.GetDataAsync();
            if (!string.IsNullOrWhiteSpace(dataJson))
            {
                try
                {
                    _data = System.Text.Json.JsonSerializer.Deserialize<PluginData>(dataJson) ?? new PluginData();
                }
                catch
                {
                    _data = new PluginData();
                }
            }
            _data.PropertyChanged += (_, _) => SaveData(); // 当Observable属性变化时自动保存数据，对于普通属性请手动调用SaveData

            _hostApi.Messenger.Register<GalgamePlayedMessage>(this, (r, m) =>
            {
                // r 是接收者 (即传入的 this)
                // m 是消息本身 (GalgamePlayedMessage)
                // m.Value 是传递的实体 (Galgame)

                if (m.Value != null)
                {
                    Debug.WriteLine($"Plugin 收到 GalgamePlayedMessage 消息: {m.Value.Name}");
                    _hostApi.AddBgTask(new PluginSaveDetectorTask(m.Value, _hostApi.Messenger));
                }
            });
        }

        private void SaveData()
        {
            var dataJson = System.Text.Json.JsonSerializer.Serialize(_data);
            _ = _hostApi.SaveDataAsync(dataJson);
        }

        protected Guid Id => Info.Id;
    }
}
