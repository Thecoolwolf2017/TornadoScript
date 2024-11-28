using GTA.UI;
using System.Collections.Generic;
using System;
using TornadoScript.ScriptCore.Game;

public class ScriptExtensionEventPool
{
    private readonly object _lock = new object();
    private readonly Dictionary<string, ScriptExtensionEventHandler> _eventHandlers =
        new Dictionary<string, ScriptExtensionEventHandler>();

    public delegate void ScriptExtensionEventHandler(object sender, ScriptEventArgs e);

    public void Register(string name)
    {
        lock (_lock)
        {
            if (!_eventHandlers.ContainsKey(name))
            {
                _eventHandlers[name] = null;
            }
        }
    }

    public void Subscribe(string name, ScriptExtensionEventHandler handler)
    {
        lock (_lock)
        {
            if (!_eventHandlers.ContainsKey(name))
            {
                Register(name);
            }
            _eventHandlers[name] += handler;
        }
    }

    public void Remove(string name, ScriptExtensionEventHandler handler)
    {
        lock (_lock)
        {
            if (_eventHandlers.ContainsKey(name))
            {
                _eventHandlers[name] -= handler;
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _eventHandlers.Clear();
        }
    }

    public void Invoke(string name, object sender, ScriptEventArgs args)
    {
        ScriptExtensionEventHandler handler;
        lock (_lock)
        {
            if (!_eventHandlers.TryGetValue(name, out handler))
                return;
        }

        try
        {
            handler?.Invoke(sender, args);
        }
        catch (Exception ex)
        {
            Notification.PostTicker($"Error in event handler: {ex.Message}", false, false);
        }
    }

    public ScriptExtensionEventHandler this[string name]
    {
        get
        {
            lock (_lock)
            {
                return _eventHandlers.ContainsKey(name) ? _eventHandlers[name] : null;
            }
        }
        set
        {
            lock (_lock)
            {
                _eventHandlers[name] = value;
            }
        }
    }

    public bool HasEvent(string name)
    {
        lock (_lock)
        {
            return _eventHandlers.ContainsKey(name);
        }
    }

    public bool HasSubscribers(string name)
    {
        lock (_lock)
        {
            return _eventHandlers.ContainsKey(name) && _eventHandlers[name] != null;
        }
    }
}
