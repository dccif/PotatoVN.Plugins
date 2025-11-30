using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using GalgameManager.Contracts.Phrase;
using GalgameManager.Enums;
using GalgameManager.Models;

namespace PotatoVN.App.PluginBase;

public class GetChuParser : IGalInfoPhraser
{
    private const int ParserId = 114514;
    
    // 使用静态构造函数来确保 HttpClient 只被初始化一次，并配置好Cookie和Header
    private static readonly HttpClient _client;
    static GetChuParser()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = true,
        };
        _client = new(handler);
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/108.0.0.0 Safari/537.36");
        
        // 确保编码提供程序被注册，以便支持 EUC-JP
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<Galgame?> GetGalgameInfo(Galgame galgame)
    {
        if (!galgame.IdForPlugins.TryGetValue(ParserId, out var id) || string.IsNullOrEmpty(id)) 
            return null;
        
        // 直接请求带有 &gc=gc 参数的URL以绕过年龄验证
        string targetUrl = $"https://www.getchu.com/soft.phtml?id={id}&gc=gc";
        
        try
        {
            var responseBytes = await _client.GetByteArrayAsync(targetUrl);
            var eucJpEncoding = Encoding.GetEncoding("EUC-JP");
            string htmlContent = eucJpEncoding.GetString(responseBytes);

            // 检查是否仍然收到了验证页面
            if (htmlContent.Contains("R18 年齢認証ページ"))
            {
                Console.WriteLine($"警告：尝试访问 {targetUrl} 时，仍然收到了年龄验证页面。");
                return null;
            }

            // 解析HTML内容
            htmlContent = htmlContent.Replace("charset=EUC-JP", "charset=utf-8", StringComparison.OrdinalIgnoreCase);
            GameData data = await ParseAsync(htmlContent);
            
            // 检查解析结果是否有效，防止因页面大改版导致返回空对象
            if (string.IsNullOrEmpty(data.CoverImageUrl) && string.IsNullOrEmpty(data.Story) && !data.Staff.Any())
            {
                Console.WriteLine($"警告：无法从 {targetUrl} 解析出有效信息。可能是页面结构已发生重大变化。");
                return null;
            }

            // 填充Galgame对象
            Galgame result = new();                                 
            result.Description.Value = data.Story;
            result.ImageUrl = data.CoverImageUrl;
            result.RssType = GetPhraseType();
            result.Id = galgame.Id;
            
            // 在这里可以添加对 Staff 信息的进一步处理，例如填充到某个属性中
            // 示例：
            // var staffInfo = data.Staff.Select(s => $"{s.Role}: {string.Join(", ", s.Members)}");
            // result.Staff.Value = string.Join("\n", staffInfo); // 假设Staff是字符串属性

            return result;
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"\n网络请求错误: {e.Message} (URL: {targetUrl})");
            Console.WriteLine("请检查您的网络连接或URL是否正确。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n处理过程中发生错误: {ex.Message}");
        }
        return null;
    }

    public RssType GetPhraseType() => (RssType)ParserId;
    
    /// <summary>
    /// 用于存储解析结果的数据模型
    /// </summary>
    public class GameData
    {
        public string? Story { get; set; }
        public string? CoverImageUrl { get; set; }
        public List<(string Role, List<string> Members)> Staff { get; set; } = new();
    }

    /// <summary>
    /// 异步解析给定的 HTML 文本内容。
    /// </summary>
    public async Task<GameData> ParseAsync(string htmlContent)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(htmlContent));
        var gameData = new GameData
        {
            Story = ExtractStory(document),
            CoverImageUrl = ExtractCoverImageUrl(document),
            Staff = ExtractStaffAndCVs(document)
        };
        return gameData;
    }

    /// <summary>
    /// 1. 提取故事（ストーリー）块的内容。
    /// </summary>
    private string? ExtractStory(IDocument document)
    {
        var tmp = document.QuerySelectorAll(".tabletitle").Select(t => t.TextContent).ToList();
        // 修改：不再限制为div，查找所有带.tabletitle类的元素
        var storyTitleElement = document.QuerySelectorAll(".tabletitle")
            .FirstOrDefault(el => el.TextContent.Contains("ストーリー"));
        if (storyTitleElement != null)
        {
            var storyBodyDiv = storyTitleElement.NextElementSibling;
            if (storyBodyDiv != null && storyBodyDiv.ClassList.Contains("tablebody"))
            {
                return storyBodyDiv.QuerySelector("span.bootstrap")?.TextContent.Trim();
            }
        }
        return null;
    }


    /// <summary>
    /// 2. 提取游戏封面的 URL。
    /// </summary>
    private string? ExtractCoverImageUrl(IDocument document)
    {
        var imageElement = document.QuerySelector("#soft_table img[src*='package.jpg']");
        var src = imageElement?.GetAttribute("src");

        // 将相对路径转换为绝对路径
        if (src != null && src.StartsWith("./"))
        {
            return "https://www.getchu.com" + src.Substring(1);
        }
        return src;
    }

    /// <summary>
    /// 3 & 4. 提取制作员工和声优列表。
    /// </summary>
    private List<(string Role, List<string> Members)> ExtractStaffAndCVs(IDocument document)
    {
        var staffList = new List<(string Role, List<string> Members)>();
        var knownRoles = new HashSet<string> { "原画", "シナリオ", "音楽" };

        // --- 提取员工信息 ---
        // 直接在 #soft_table 下查找所有行，以适应新版HTML结构
        var staffRows = document.QuerySelectorAll("#soft_table tr");
        foreach (var row in staffRows)
        {
            var cells = row.QuerySelectorAll("td").ToList();
            if (cells.Count < 2) continue;

            string role = cells[0].TextContent.Trim().Replace("：", "");
            if (knownRoles.Contains(role))
            {
                var members = cells[1].QuerySelectorAll("a")
                                      .Select(a => a.TextContent.Trim())
                                      .Where(name => !string.IsNullOrEmpty(name))
                                      .ToList();
                if (members.Any())
                {
                    staffList.Add((role, members));
                }
            }
        }

        // --- 提取声优 (CV) 信息 ---
        var cvs = new List<string>();
        // 查找 "キャラクター" 标题，注意其前面可能有一个特殊的不间断空格
        var characterTitleDiv = document.QuerySelectorAll("div.tabletitle")
                                        .FirstOrDefault(el => el.TextContent.Trim() == "キャラクター" || el.TextContent.Trim() == " キャラクター");
        
        if (characterTitleDiv != null)
        {
            var characterTable = characterTitleDiv.NextElementSibling;
            if (characterTable != null && characterTable.TagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
            {
                var charaNameHeaders = characterTable.QuerySelectorAll("h2.chara-name");
                foreach (var header in charaNameHeaders)
                {
                    // 直接获取 h2 的文本内容
                    string text = header.TextContent;
                    int cvIndex = text.IndexOf("CV：", StringComparison.Ordinal);
                    if (cvIndex > -1)
                    {
                        string cvName = text.Substring(cvIndex + 3).Trim();
                        if (!string.IsNullOrEmpty(cvName))
                        {
                            cvs.Add(cvName);
                        }
                    }
                }
            }
        }

        if (cvs.Any())
        {
            staffList.Add(("声優", cvs.Distinct().ToList()));
        }

        return staffList;
    }
}
