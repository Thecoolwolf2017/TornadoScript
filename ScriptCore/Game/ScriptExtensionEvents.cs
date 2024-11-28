using System;
using System.Collections.Generic;

namespace TornadoScript.ScriptCore.Game
{
    public delegate void ScriptExtensionEventHandler(object sender, ScriptEventArgs e);

    public class ScriptExtensionEventPool
    {
        private readonly Dictionary<string, ScriptExtensionEventHandler> _events = 
            new Dictionary<string, ScriptExtensionEventHandler>();

        public ScriptExtensionEventHandler this[string name]
        {
            get
            {
                return _events.TryGetValue(name, out var handler) ? handler : null;
            }
        }

        public void Add(string name, ScriptExtensionEventHandler handler)
        {
            if (_events.ContainsKey(name))
                return;

            _events.Add(name, handler);
        }

        public void Remove(string name)
        {
            if (_events.ContainsKey(name))
                _events.Remove(name);
        }
    }
}
