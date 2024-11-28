using System;
using System.Collections.Generic;
using TornadoScript.ScriptCore.Game;
using static ScriptExtensionEventPool;

public abstract class ScriptExtension : ScriptComponent, IScriptEventHandler
{
    public ScriptExtensionEventPool Events { get; }

    protected ScriptExtension()
    {
        Events = new ScriptExtensionEventPool();
    }

    public void Invoke(string name, object sender, ScriptEventArgs args)
    {
        Events.Invoke(name, sender, args);
    }

    public virtual void NotifyEvent(string name)
    {
        NotifyEvent(name, new ScriptEventArgs());
    }

    public virtual void NotifyEvent(string name, ScriptEventArgs args)
    {
        Events.Invoke(name, this, args);
    }

    public void RegisterEvent(string name)
    {
        Events.Register(name);
    }

    private readonly Dictionary<Action<ScriptEventArgs>, ScriptExtensionEventHandler> _handlerMappings =
        new Dictionary<Action<ScriptEventArgs>, ScriptExtensionEventHandler>();

    protected void SubscribeEvent(string name, Action<ScriptEventArgs> handler)
    {
        if (!_handlerMappings.TryGetValue(handler, out var eventHandler))
        {
            eventHandler = (sender, args) => handler(args);
            _handlerMappings[handler] = eventHandler;
        }
        Events.Subscribe(name, eventHandler);
    }

    protected void UnsubscribeEvent(string name, Action<ScriptEventArgs> handler)
    {
        if (_handlerMappings.TryGetValue(handler, out var eventHandler))
        {
            Events.Remove(name, eventHandler);
            _handlerMappings.Remove(handler);
        }
    }





    public virtual void Dispose()
    {
        OnThreadDetached();
        ScriptThread.Remove(this);
    }

    protected virtual void OnKeyDown(object sender, ScriptEventArgs args)
    {
        // Override in derived classes to handle key down events
    }
}
