using GTA;
using GTA.Math;
using GTA.Native;
using System;
using TornadoScript.ScriptCore;

namespace TornadoScript.ScriptMain.Script
{
    /// <summary>
    /// Represents an entity that is being pulled by the tornado's vortex.
    /// </summary>
    public class PulledEntity : IDisposable
    {
        /// <summary>
        /// The entity being pulled by the vortex.
        /// </summary>
        public Entity Entity { get; }

        /// <summary>
        /// The initial distance from the entity to the vortex when it was first pulled.
        /// </summary>
        public float InitialDistance { get; }

        /// <summary>
        /// The time when the entity was first pulled by the vortex.
        /// </summary>
        public DateTime PullStartTime { get; }

        /// <summary>
        /// The current angular velocity of the entity around the vortex.
        /// </summary>
        public float AngularVelocity { get; set; }

        /// <summary>
        /// The current vertical velocity of the entity.
        /// </summary>
        public float VerticalVelocity { get; set; }

        /// <summary>
        /// The X-axis bias for entity movement.
        /// </summary>
        public float XBias { get; set; }

        /// <summary>
        /// The Y-axis bias for entity movement.
        /// </summary>
        public float YBias { get; set; }

        /// <summary>
        /// Whether the entity is currently being pulled inward.
        /// </summary>
        public bool IsPullingIn { get; set; }

        /// <summary>
        /// The last known position of the entity.
        /// </summary>
        public Vector3 LastPosition { get; set; }

        /// <summary>
        /// Whether this entity is the player character.
        /// </summary>
        public bool IsPlayer { get; }

        private bool _disposed;
        private readonly object _disposeLock = new object();

        /// <summary>
        /// Creates a new instance of PulledEntity.
        /// </summary>
        /// <param name="entity">The entity to be pulled</param>
        /// <param name="initialDistance">Initial distance from the vortex</param>
        public PulledEntity(Entity entity, float initialDistance)
        {
            Entity = entity;
            InitialDistance = initialDistance;
            PullStartTime = DateTime.Now;
            IsPlayer = entity == Game.Player?.Character;
            LastPosition = entity.Position;
            XBias = 0;
            YBias = 0;
            AngularVelocity = 0;
            VerticalVelocity = 0;
            IsPullingIn = true;
        }

        /// <summary>
        /// Updates the entity's last known position.
        /// </summary>
        public void UpdateLastPosition()
        {
            if (Entity != null && Entity.Exists())
            {
                LastPosition = Entity.Position;
            }
        }

        /// <summary>
        /// Gets the time elapsed since the entity was first pulled.
        /// </summary>
        public TimeSpan ElapsedTime => DateTime.Now - PullStartTime;

        /// <summary>
        /// Checks if the entity still exists and is valid.
        /// </summary>
        public bool IsValid => Entity != null && Entity.Exists() && !Entity.IsDead;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            lock (_disposeLock)
            {
                if (_disposed) return;

                if (disposing)
                {
                    try
                    {
                        // Reset entity state if it still exists
                        if (Entity != null && Entity.Exists())
                        {
                            // Reset invincibility and physics state
                            Entity.IsInvincible = false;
                            Entity.Velocity = Vector3.Zero;
                            Entity.LocalRotationVelocity = Vector3.Zero;

                            // Handle different entity types
                            if (Entity is Ped ped)
                            {
                                // Reset ped state
                                Function.Call(Hash.FREEZE_ENTITY_POSITION, ped.Handle, false);
                                Function.Call(Hash.SET_ENTITY_DYNAMIC, ped.Handle, true);
                                Function.Call(Hash.SET_ENTITY_HAS_GRAVITY, ped.Handle, true);
                                Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, ped.Handle, 1.0f);
                                Function.Call(Hash.RESET_PED_WEAPON_MOVEMENT_CLIPSET, ped.Handle);

                                // Clear all tasks and animations
                                ped.Task.ClearAllImmediately();
                                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, ped.Handle);
                                Function.Call(Hash.STOP_ANIM_TASK, ped.Handle, "", "", -4.0f);
                            }
                            else if (Entity is Vehicle vehicle)
                            {
                                // Reset vehicle state
                                Function.Call(Hash.FREEZE_ENTITY_POSITION, vehicle.Handle, false);
                                Function.Call(Hash.SET_ENTITY_DYNAMIC, vehicle.Handle, true);
                                Function.Call(Hash.SET_ENTITY_HAS_GRAVITY, vehicle.Handle, true);
                                Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, vehicle.Handle, 0.0f);
                                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle.Handle, true, true, false);

                                // Reset vehicle damage
                                vehicle.IsInvincible = false;
                                vehicle.Repair();
                            }
                            else if (Entity is Prop prop)
                            {
                                // Reset prop state
                                Function.Call(Hash.FREEZE_ENTITY_POSITION, prop.Handle, false);
                                Function.Call(Hash.SET_ENTITY_DYNAMIC, prop.Handle, true);
                                Function.Call(Hash.SET_ENTITY_HAS_GRAVITY, prop.Handle, true);
                                Function.Call(Hash.SET_ENTITY_COLLISION, prop.Handle, true, false);

                                // Reset prop physics
                                Function.Call(Hash.SET_ACTIVATE_OBJECT_PHYSICS_AS_SOON_AS_IT_IS_UNFROZEN, prop.Handle, true);
                            }

                            // Clean up any particle effects attached to the entity
                            Function.Call(Hash.REMOVE_PARTICLE_FX_FROM_ENTITY, Entity.Handle);

                            // Reset LOD distance
                            Entity.LodDistance = 1000;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error in PulledEntity.Dispose: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Logger.Error($"Inner Exception: {ex.InnerException.Message}");
                        }
                    }
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~PulledEntity()
        {
            Dispose(false);
        }
    }
}
