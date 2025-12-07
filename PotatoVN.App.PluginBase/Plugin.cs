using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Contracts.PluginUi;
using GalgameManager.WinApp.Base.Models;
using PotatoVN.App.PluginBase.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase
{
    public partial class Plugin : IPlugin, IPluginSetting
    {
        private IPotatoVnApi _hostApi = null!;
        private PluginData _data = new ();

        public PluginInfo Info { get; } = new()
        {
            Id = new Guid("a8b3c9d2-4e7f-5a6b-8c9d-1e2f3a4b5c6d"),
            Name = "Game Save Detector",
            Description = "智能游戏存档检测器，使用外置算法实时监控和变体分析，精准定位游戏存档位置。",
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

            // 【关键】实现游戏存档检测器任务替换逻辑
            await ReplaceGameSaveDetectorTask();
        }

        /// <summary>
        /// 替换原有的GameSaveDetectorTask为插件版本
        /// </summary>
        private async Task ReplaceGameSaveDetectorTask()
        {
            try
            {

                // 查找所有GameSaveDetectorTask类型的任务
                var gameSaveDetectorTask = _hostApi.GetBgTasks()
                    .FirstOrDefault(task => task.GetType().FullName == "GalgameManager.Models.BgTasks.GameSaveDetectorTask");

                // 2. 创建插件版本的检测器任务
                // Use dynamic to access Galgame property as it might not be in BgTaskBase contract
                dynamic taskWithGalgame = gameSaveDetectorTask;
                var pluginTask = new PluginSaveDetectorTask(_hostApi, taskWithGalgame.Galgame);

                // 3. 注册插件版本的任务到系统
                await _hostApi.AddBgTask(pluginTask);
                

                
            }
            catch (Exception ex)
            {
                // 记录错误但不影响插件加载
                System.Diagnostics.Debug.WriteLine($"[GameSaveDetector Plugin] 替换任务时出错: {ex.Message}");

                // 尝试通知用户
                try
                {
                    // 插件成功替换原有任务的通知功能暂时禁用，等待API确认
                    // await _hostApi.ShowNotification("GameSaveDetector_Replaced".GetLocalized(), Info.Name);
                }
                catch
                {
                    // 通知也失败时，静默处理
                }
            }
        }

        private void SaveData()
        {
            var dataJson = System.Text.Json.JsonSerializer.Serialize(_data);
            _ = _hostApi.SaveDataAsync(dataJson);
        }

        protected Guid Id => Info.Id;
    }
}
