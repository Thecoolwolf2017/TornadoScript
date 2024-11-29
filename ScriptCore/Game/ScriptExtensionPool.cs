using System.Collections.Generic;

namespace TornadoScript.ScriptCore.Game
{
    internal class ScriptExtensionPool : List<ScriptExtension>
    {
        /// <summary>
        /// Get an extension from the pool by its type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Get<T>() where T : ScriptComponent
        {
            for (int i = 0; i < Count; i++)
            {
                if (this[i] is T item)
                {
                    return item;
                }
            }

            return null;
        }
    }
}
