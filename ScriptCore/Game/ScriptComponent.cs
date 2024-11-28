using System;
using GTA;
using GTA.Math;

namespace TornadoScript.ScriptCore.Game
{
    public abstract class ScriptComponent : IScriptComponent
    {
        public string Name { get; protected set; }
        public Guid Guid { get; }
        protected ScriptThread Thread { get; private set; }

        protected ScriptComponent()
        {
            Guid = Guid.NewGuid();
        }

        internal void OnAttached(ScriptThread thread)
        {
            Thread = thread;
            OnThreadAttached();
        }

        internal void OnDetached()
        {
            OnThreadDetached();
            Thread = null;
        }

        public virtual void OnThreadAttached() { }

        public virtual void OnThreadDetached() { }

        public virtual void OnUpdate(int gameTime)
        {
            var deltaTime = GTA.Game.GameTime;
            UpdateComponent(deltaTime);
        }

        protected virtual void UpdateComponent(float deltaTime) { }

        public virtual Vector3 GetPosition()
        {
            if (Thread == null || GTA.Game.Player?.Character == null)
                return Vector3.Zero;

            return GTA.Game.Player.Character.Position;
        }

        public override bool Equals(object obj)
        {
            if (obj is ScriptComponent other)
                return Guid.Equals(other.Guid);
            return false;
        }

        public override int GetHashCode() => Guid.GetHashCode();

        protected T GetVar<T>(string name)
        {
            if (Thread == null)
                throw new InvalidOperationException("Component is not attached to a thread");

            return Thread != null ? ScriptThread.GetVar<T>(name) : default;
        }

        protected bool SetVar<T>(string name, T value) => Thread != null && ScriptThread.SetVar<T>(name, value);

        protected TExtension GetOrCreate<TExtension>() where TExtension : ScriptExtension, new()
        {
            return ScriptThread.GetOrCreate<TExtension>();
        }

    }
}