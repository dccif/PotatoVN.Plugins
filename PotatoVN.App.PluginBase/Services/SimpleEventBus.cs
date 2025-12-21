using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace PotatoVN.App.PluginBase.Services;

public class SimpleEventBus
{
    private static SimpleEventBus? _instance;
    public static SimpleEventBus Instance => _instance ??= new SimpleEventBus();

    private readonly ConcurrentDictionary<Type, List<object>> _subscribers = new();

    public void Subscribe<TEvent>(Action<TEvent> handler)
    {
        var type = typeof(TEvent);
        _subscribers.AddOrUpdate(type,
            _ => new List<object> { handler },
            (_, list) => { lock (list) { list.Add(handler); } return list; });
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        var type = typeof(TEvent);
        if (_subscribers.TryGetValue(type, out var list))
        {
            lock (list)
            {
                list.Remove(handler);
            }
        }
    }

    public void Publish<TEvent>(TEvent eventMessage)
    {
        var type = typeof(TEvent);
        if (_subscribers.TryGetValue(type, out var list))
        {
            object[] handlers;
            lock (list)
            {
                handlers = list.ToArray();
            }

            foreach (var handler in handlers)
            {
                ((Action<TEvent>)handler)(eventMessage);
            }
        }
    }
}
