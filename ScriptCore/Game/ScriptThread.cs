using GTA;
using System;
using System.Windows.Forms;

namespace TornadoScript.ScriptCore.Game
{
    /// <summary>
    /// Base class for a script thread.
    /// </summary>
    public abstract class ScriptThread : Script
    {
        /// <summary>
        /// Script extension pool.
        /// </summary>
        private static ScriptExtensionPool _extensions;

        /// <summary>
        /// Script vars.
        /// </summary>
        public static ScriptVarCollection Vars { get; private set; }

        private static readonly object _initLock = new object();
        private static bool _isInitialized = false;

        protected ScriptThread()
        {
            lock (_initLock)
            {
                if (!_isInitialized)
                {
                    _extensions = new ScriptExtensionPool();
                    Vars = new ScriptVarCollection();
                    _isInitialized = true;
                }
            }
            Tick += (s, e) => OnUpdate(GTA.Game.GameTime);
            KeyDown += KeyPressedInternal;
        }

        /// <summary>
        /// Get a script extension from the underlying pool by its type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T Get<T>() where T : ScriptExtension
        {
            return _extensions.Get<T>();
        }

        /// <summary>
        /// Adds a script extension to this thread.
        /// </summary>
        /// <param name="extension"></param>
        public static void Add(ScriptExtension extension)
        {
            if (_extensions.Contains(extension)) return;

            extension.RegisterEvent("keydown");

            _extensions.Add(extension);

            extension.OnThreadAttached();
        }

        /// <summary>
        /// Adds a script extension to this thread.
        /// </summary>
        public static void Create<T>() where T : ScriptExtension, new()
        {
            var extension = Get<T>();

            if (extension != null) return;

            extension = new T();

            Add(extension);
        }

        /// <summary>
        /// Get an extension, or create it if it doesn't exist.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T GetOrCreate<T>() where T : ScriptExtension, new()
        {
            var extension = Get<T>();

            if (extension != null)
                return extension;

            extension = new T();

            Add(extension);

            return extension;
        }

        internal static void Remove(ScriptExtension extension)
        {
            extension.OnThreadDetached();

            _extensions.Remove(extension);
        }

        /// <summary>
        /// Register a new script variable and add it to the collection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name">The name of the var</param>
        /// <param name="defaultValue">The default (reset) value</param>
        /// <param name="readOnly"></param>
        public static void RegisterVar<T>(string name, T defaultValue, bool readOnly = false)
        {
            if (Vars == null)
            {
                lock (_initLock)
                {
                    if (Vars == null)
                    {
                        Vars = new ScriptVarCollection();
                        _isInitialized = true;
                    }
                }
            }

            if (!Vars.ContainsKey(name))
            {
                Vars.Add(name, new ScriptVar<T>(defaultValue, readOnly));
                Logger.Log($"Registered variable {name} with default value {defaultValue}");
            }
        }

        /// <summary>
        /// Get a script variable attached to this thread.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <returns></returns>
        public static ScriptVar<T> GetVar<T>(string name)
        {
            return Vars.Get<T>(name);
        }

        /// <summary>
        /// Set the value of a script variable attached to this thread.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static bool SetVar<T>(string name, T value)
        {
            try
            {
                if (Vars == null)
                {
                    lock (_initLock)
                    {
                        if (Vars == null)
                        {
                            Vars = new ScriptVarCollection();
                            _isInitialized = true;
                            Logger.Log("Initialized ScriptVarCollection in SetVar");
                        }
                    }
                }

                var foundVar = GetVar<T>(name);
                if (foundVar == null)
                {
                    // Variable doesn't exist, create it
                    Logger.Log($"Creating new variable {name} with value {value}");
                    RegisterVar(name, value);
                    return true;
                }

                if (foundVar.ReadOnly)
                {
                    Logger.Log($"Cannot set readonly variable {name}");
                    return false;
                }

                foundVar.Value = value;
                Logger.Log($"Set variable {name} to value {value}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in SetVar for {name}: {ex.Message}");
                return false;
            }
        }

        internal virtual void KeyPressedInternal(object sender, KeyEventArgs e)
        {
            foreach (ScriptExtension s in _extensions)
            {
                s.NotifyEvent("keydown", new ScriptEventArgs(e));
            }
        }

        /// <summary>
        /// Updates the thread.
        /// </summary>
        public virtual void OnUpdate(int gameTime)
        {
            for (int i = 0; i < _extensions.Count; i++)
            {
                _extensions[i].OnUpdate(gameTime);
            }
        }

        /// <summary>
        /// Removes the thread and all extensions.
        /// </summary>
        /// <param name="A_0"></param>

        public void Dispose()
        {
            _extensions.Clear();
            Vars.Clear();
        }
    }
}
