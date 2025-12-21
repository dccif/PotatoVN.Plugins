using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.PluginBase.Models;
using PotatoVN.App.PluginBase.Services;
using System;

namespace PotatoVN.App.PluginBase.Views;

public interface IBigScreenView
{
    void OnNavigatedTo(object? parameter);
    void OnNavigatedFrom();
    void PublishHints();
    void OnGamepadInput(GamepadButton button);
}

public abstract class BigScreenViewBase : UserControl, IBigScreenView
{
    protected readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    protected BigScreenViewBase()
    {
        this.Loaded += (s, e) =>
        {
            SimpleEventBus.Instance.Subscribe<GamepadInputMessage>(HandleInternalInput);
            this.GotFocus += (s, e) =>
            {
                if (IsActiveElement(e.OriginalSource)) PublishHints();
            };
        };
        this.Unloaded += (s, e) =>
        {
            SimpleEventBus.Instance.Unsubscribe<GamepadInputMessage>(HandleInternalInput);
        };
    }

    protected virtual bool IsActiveElement(object source) => true;

    private void HandleInternalInput(GamepadInputMessage msg) =>
        _dispatcherQueue.TryEnqueue(() => OnGamepadInput(msg.Button));

    public virtual void OnNavigatedTo(object? parameter) => PublishHints();
    public virtual void OnNavigatedFrom() { }
    public abstract void PublishHints();
    public abstract void OnGamepadInput(GamepadButton button);

    public virtual void FocusDefaultElement() => this.Focus(FocusState.Programmatic);
}
