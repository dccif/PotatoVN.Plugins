using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.Web.WebView2.Core;
using GalgameManager.Models;
using Windows.Storage.Streams;
using System.Reflection;

namespace PotatoVN.App.PluginBase.Views;

public class BigScreenWindow : Window
{
    private readonly List<Galgame> _games;
    private readonly string _webAssetsPath;
    private WebView2 _webView;

    public BigScreenWindow(List<Galgame> games, string pluginPath)
    {
        _games = games;
        _webAssetsPath = Path.Combine(pluginPath, "Web", "dist");

        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        
        var rootGrid = new Grid();
        _webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 26, 31, 41)
        };
        rootGrid.Children.Add(_webView);
        this.Content = rootGrid;

        InitializeWebView();
    }

    private async void InitializeWebView()
    {
        try 
        {
            await _webView.EnsureCoreWebView2Async();

            var webViewType = _webView.GetType();
            var coreProp = webViewType.GetProperty("CoreWebView2");
            if (coreProp == null) return;
            object coreObj = coreProp.GetValue(_webView);
            if (coreObj == null) return;
            dynamic core = coreObj;

            // 0. Inject Error Logger (Crucial for debugging frontend issues)
            try
            {
                string errorLogger = @" 
                    window.onerror = function(message, source, lineno, colno, error) {
                        window.chrome.webview.postMessage(JSON.stringify({type: 'error', message: message, info: source + ':' + lineno}));
                    };
                    window.addEventListener('unhandledrejection', function(event) {
                        window.chrome.webview.postMessage(JSON.stringify({type: 'error', message: 'Unhandled Rejection: ' + event.reason}));
                    });
                ";
                await core.AddScriptToExecuteOnDocumentCreatedAsync(errorLogger);
            }
            catch {}

            // 1. Inject Data
            try
            {
                var gameDtos = _games.Select(g => new
                {
                    id = g.Uuid.ToString(),
                    name = g.Name.Value ?? "Unknown",
                    image = GetImageUrl(g.ImagePath.Value)
                }).ToList();

                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var json = JsonSerializer.Serialize(gameDtos, options);
                var jsCode = $"window.potatoData = {json};";
                
                var addScriptMethod = coreObj.GetType().GetMethod("AddScriptToExecuteOnDocumentCreatedAsync");
                if (addScriptMethod != null)
                {
                    var task = addScriptMethod.Invoke(coreObj, new object[] { jsCode }) as System.Threading.Tasks.Task;
                    if (task != null) await task;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Data Injection Failed: {ex}"); }

            // 2. Map Folders (HTTPS for Secure Context)
            try
            {
                var setMappingMethod = coreObj.GetType().GetMethod("SetVirtualHostNameToFolderMapping");
                if (setMappingMethod != null)
                {
                    var paramTypes = setMappingMethod.GetParameters();
                    if (paramTypes.Length == 3)
                    {
                        var enumType = paramTypes[2].ParameterType;
                        object allowValue = Enum.ToObject(enumType, 1); // Allow

                        if (Directory.Exists(_webAssetsPath))
                        {
                            setMappingMethod.Invoke(coreObj, new object[] { "potato.local", _webAssetsPath, allowValue });
                        }

                        string localImagesPath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Images");
                        if (Directory.Exists(localImagesPath))
                        {
                            setMappingMethod.Invoke(coreObj, new object[] { "potato.images", localImagesPath, allowValue });
                        }
                    }
                }
            }
            catch (Exception mappingEx) { System.Diagnostics.Debug.WriteLine($"Mapping Failed: {mappingEx}"); }

            // 3. Setup Image Interception (HTTPS)
            try 
            {
                var addFilterMethod = coreObj.GetType().GetMethod("AddWebResourceRequestedFilter");
                if (addFilterMethod != null)
                {
                    var paramTypes = addFilterMethod.GetParameters();
                    if (paramTypes.Length == 2)
                    {
                        var enumType = paramTypes[1].ParameterType;
                        object contextValue = Enum.ToObject(enumType, 0); // All
                        // Filter for HTTPS
                        addFilterMethod.Invoke(coreObj, new object[] { "https://potato.images/*", contextValue });
                    }
                }
                
                SubscribeToEvent(coreObj, "WebResourceRequested", (s, e) => HandleWebResourceRequested(e));
            }
            catch(Exception ex) { System.Diagnostics.Debug.WriteLine($"Filter Setup Failed: {ex}"); }

            // 4. Subscribe to Events
            SubscribeToEvent(coreObj, "WebMessageReceived", (s, e) => HandleWebMessage(coreObj, e));
            
            // 5. Navigate (HTTPS)
            core.Navigate("https://potato.local/index.html");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebView2 Init Error: {ex}");
        }
    }

    private void SubscribeToEvent(object target, string eventName, Action<object, object> handler)
    {
        try 
        {
            var eventInfo = target.GetType().GetEvent(eventName);
            if (eventInfo == null) return;
            var handlerType = eventInfo.EventHandlerType;
            var invokeMethod = handlerType.GetMethod("Invoke");
            var parameters = invokeMethod.GetParameters();
            var senderParam = System.Linq.Expressions.Expression.Parameter(parameters[0].ParameterType, "sender");
            var argsParam = System.Linq.Expressions.Expression.Parameter(parameters[1].ParameterType, "args");
            var body = System.Linq.Expressions.Expression.Invoke(
                System.Linq.Expressions.Expression.Constant(handler),
                System.Linq.Expressions.Expression.Convert(senderParam, typeof(object)),
                System.Linq.Expressions.Expression.Convert(argsParam, typeof(object))
            );
            var lambda = System.Linq.Expressions.Expression.Lambda(handlerType, body, senderParam, argsParam);
            eventInfo.AddEventHandler(target, lambda.Compile());
        }
        catch(Exception ex) { System.Diagnostics.Debug.WriteLine($"Subscribe Error: {ex}"); }
    }

    private void HandleWebResourceRequested(object argsObj)
    {
        try
        {
            dynamic args = argsObj;
            dynamic request = args.Request;
            string uriString = request.Uri;
            Uri uri = new Uri(uriString);

            // Check for HTTPS scheme match
            if (uri.Host.Equals("potato.images", StringComparison.OrdinalIgnoreCase))
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var pathBase64 = query["path"];

                if (!string.IsNullOrEmpty(pathBase64))
                {
                    try
                    {
                        var validBase64 = pathBase64.Replace(" ", "+");
                        var bytes = Convert.FromBase64String(validBase64);
                        var path = System.Text.Encoding.UTF8.GetString(bytes);

                        if (path.StartsWith("ms-appx:///"))
                        {
                            string appDir = AppContext.BaseDirectory;
                            path = path.Replace("ms-appx:////", appDir);
                        }

                        if (File.Exists(path))
                        {
                            var stream = File.OpenRead(path);
                            var webViewType = _webView.GetType();
                            object coreObj = webViewType.GetProperty("CoreWebView2").GetValue(_webView);
                            dynamic env = ((dynamic)coreObj).Environment;
                            
                            object streamArg = stream;
                            try { streamArg = stream.AsRandomAccessStream(); } catch {}

                            dynamic response = env.CreateWebResourceResponse(
                                streamArg, 
                                200, 
                                "OK", 
                                "Content-Type: image/jpeg");
                                
                            args.Response = response;
                            return;
                        }
                    }
                    catch (Exception ex) 
                    {
                        System.Diagnostics.Debug.WriteLine($"[ImageRequest] Error: {ex}");
                    }
                }
                
                try {
                    var webViewType = _webView.GetType();
                    object coreObj = webViewType.GetProperty("CoreWebView2").GetValue(_webView);
                    dynamic env = ((dynamic)coreObj).Environment;
                    args.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
                } catch {}
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Image Handler Error: {ex}"); }
    }

    private void HandleWebMessage(object coreObj, object argsObj)
    {
        try
        {
            dynamic args = argsObj;
            string json = args.WebMessageAsJson;
            // Handle error logging messages specially
            if (json.Contains("\"type\":\"error\""))
            {
                System.Diagnostics.Debug.WriteLine($"[Frontend Error] {json}");
                return;
            }

            var msg = JsonSerializer.Deserialize<WebMsg>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (msg?.type == "close")
            {
                this.Close();
            }
            else if (msg?.type == "launch" && msg.id != null)
            {
                System.Diagnostics.Debug.WriteLine($"Launch game: {msg.id}");
            }
        }
        catch { }
    }

    private string GetImageUrl(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        if (path.StartsWith("http") || path.StartsWith("https")) return path;
        
        // 1. Try Virtual Host Mapping (potato.images -> LocalState/Images)
        try
        {
            string localImagesPath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Images");
            string fullPath = Path.GetFullPath(path);
            
            if (fullPath.StartsWith(localImagesPath, StringComparison.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(fullPath);
                // Use HTTPS
                return $"https://potato.images/{Uri.EscapeDataString(fileName)}";
            }
        }
        catch {}

        // 2. Fallback: Base64 Path (Manual Handler)
        var bytes = System.Text.Encoding.UTF8.GetBytes(path);
        var base64 = Convert.ToBase64String(bytes);
        var encodedBase64 = System.Net.WebUtility.UrlEncode(base64);
        // Use HTTPS
        return $"https://potato.images/?path={encodedBase64}";
    }

    private class WebMsg
    {
        public string? type { get; set; }
        public string? id { get; set; }
    }
}