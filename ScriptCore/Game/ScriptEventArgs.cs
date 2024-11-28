using System;

namespace TornadoScript.ScriptCore.Game
{
    public class ScriptEventArgs : EventArgs
    {
        public object Data { get; }
        public DateTime Timestamp { get; }

        public ScriptEventArgs()
            : this(null)
        {
        }

        public ScriptEventArgs(object data)
        {
            Data = data;
            Timestamp = DateTime.Now;
        }

        public T GetData<T>() where T : class
        {
            return Data as T;
        }
    }

    public class ScriptEventArgs<T> : EventArgs
    {
        public T Data { get; private set; }

        public ScriptEventArgs(T data)
        {
            Data = data;
        }
    }
}
