using System;
using GTA;
using GTA.Math;

namespace TornadoScript.ScriptCore.Game
{
    /// <summary>
    /// Represents a game entity.
    /// </summary>
    public abstract class ScriptEntity<T> : ScriptComponent where T : Entity
    {
        /// <summary>
        /// Base game entity reference.
        /// </summary>
        public Entity Entity { get; protected set; }

        /// <summary>
        /// Total entity ticks.
        /// </summary>
        public int TotalTicks { get; private set; }

        /// <summary>
        /// Total time entity has been available to the script.
        /// </summary>
        public TimeSpan TotalTime { get; private set; }

        /// <summary>
        /// Time at which the entity was made avilable to the script.
        /// </summary>
        public int CreatedTime { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the entity is valid.
        /// </summary>
        public bool IsValid => Entity != null && Entity.Exists();

        /// <summary>
        /// Gets a value indicating whether the entity is alive.
        /// </summary>
        public bool IsAlive => IsValid && !Entity.IsDead;

        /// <summary>
        /// Initialize the class.
        /// </summary>
        protected ScriptEntity()
        {
            Name = GetType().Name;
        }

        /// <summary>
        /// Call this method each tick to update entity related information.
        /// </summary>
        public override void OnUpdate(int gameTime)
        {
            TotalTicks++;

            TotalTicks %= int.MaxValue;

            TotalTime = TimeSpan.FromMilliseconds(gameTime - CreatedTime);
        }

        /// <summary>
        /// Creates the entity.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns></returns>
        protected abstract Entity CreateEntity(Vector3 position);

        /// <summary>
        /// Initializes the entity.
        /// </summary>
        /// <param name="position">The position.</param>
        public virtual void Initialize(Vector3 position)
        {
            if (IsValid)
                throw new InvalidOperationException("Entity is already initialized");

            Entity = CreateEntity(position);
            if (Entity == null)
                throw new InvalidOperationException("Failed to create entity");

            CreatedTime = GTA.Game.GameTime;
        }

        /// <summary>
        /// Destroys the entity.
        /// </summary>
        public virtual void Destroy()
        {
            if (!IsValid) return;

            Entity.Delete();
            Entity = null;
        }

        /// <summary>
        /// Teleports the entity to the specified position.
        /// </summary>
        /// <param name="position">The position.</param>
        public virtual void Teleport(Vector3 position)
        {
            if (!IsValid) return;

            Entity.Position = position;
        }

        /// <summary>
        /// Sets the invincibility of the entity.
        /// </summary>
        /// <param name="invincible">if set to <c>true</c> the entity is invincible.</param>
        public virtual void SetInvincible(bool invincible)
        {
            if (!IsValid) return;

            Entity.IsInvincible = invincible;
        }

        /// <summary>
        /// Gets the position of the entity.
        /// </summary>
        /// <returns></returns>
        public override Vector3 GetPosition()
        {
            return IsValid ? Entity.Position : base.GetPosition();
        }

        /// <summary>
        /// Called when the thread is detached.
        /// </summary>
        public override void OnThreadDetached()
        {
            Destroy();
            base.OnThreadDetached();
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public virtual void Dispose()
        {
            Destroy();
        }

        /// <summary>
        /// Updates the entity state.
        /// </summary>
        /// <param name="deltaTime">The delta time.</param>
        protected override void UpdateComponent(float deltaTime)
        {
            if (!IsValid) return;

            UpdateEntity(deltaTime);
        }

        /// <summary>
        /// Updates the entity state.
        /// </summary>
        /// <param name="deltaTime">The delta time.</param>
        protected virtual void UpdateEntity(float deltaTime)
        {
            // Override in derived classes to update entity state
        }
    }
}
