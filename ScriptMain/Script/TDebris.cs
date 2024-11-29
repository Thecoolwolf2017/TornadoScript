using GTA;
using GTA.Math;
using GTA.Native;
using System;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Utility;

namespace TornadoScript.ScriptMain.Script
{
    public sealed class TDebris : ScriptExtension, IScriptEntity
    {
        #region Component Events
        public event ScriptComponentEventHandler OnComponentInitialized;
        public event ScriptComponentEventHandler OnComponentDestroyed;
        #endregion

        #region Event Names
        private const string EventOnSpawn = "OnSpawn";
        private const string EventOnDestroy = "OnDestroy";
        private const string EventOnCollision = "OnCollision";
        private const string EventOnDamage = "OnDamage";
        #endregion

        #region Event Data Classes
        public class CollisionEventData
        {
            public Vector3 Position { get; set; }
            public Vector3 Velocity { get; set; }
            public float Force { get; set; }
            public float Speed { get; set; }
        }

        public class DamageEventData
        {
            public float Damage { get; set; }
            public Vector3 Position { get; set; }
            public bool IsLethal { get; set; }
        }

        public class SpawnEventData
        {
            public Vector3 Position { get; set; }
            public string ModelName { get; set; }
            public float Radius { get; set; }
        }
        #endregion

        #region Constants
        private const int DefaultLodDistance = 1000;
        private const float BaseHeightOffset = 5.0f;
        private const float MinRotationSpeed = 0.02f;
        private const float MaxRotationSpeed = 0.08f;
        private const int DebrisLifetime = 6400;
        private const float UpdateInterval = 1.0f / 60.0f; // Target 60 FPS for physics
        private const float MaxUpdateDelta = 0.1f; // Cap delta time to prevent physics issues
        #endregion

        #region Variable Keys
        private const string VarKeyForceMultiplier = "DebrisForceMultiplier";
        private const string VarKeyLiftMultiplier = "DebrisLiftMultiplier";
        private const string VarKeyMaxSpeed = "DebrisMaxSpeed";
        private const string VarKeyCollisionThreshold = "DebrisCollisionThreshold";
        #endregion

        #region Fields
        public TornadoVortex Parent { get; }
        private readonly float _radius;
        private readonly int _spawnTime;
        private readonly float _rotationSpeed;
        private float _currentAngle;
        private readonly Vector3 _heightOffset;
        private bool _isDestroyed;
        private Vector3 _lastPosition;
        private bool _wasValidLastFrame;
        private string _modelName;
        private Prop _prop;
        #endregion

        #region Properties
        public Entity Entity => _prop;
        public bool IsValid => _prop != null && _prop.Exists();
        public bool IsAlive => IsValid && !_isDestroyed;

        // Script variables with thread-safe access
        private float ForceMultiplier
        {
            get => GetVar<float>(VarKeyForceMultiplier);
            set => SetVar(VarKeyForceMultiplier, value);
        }

        private float LiftMultiplier
        {
            get => GetVar<float>(VarKeyLiftMultiplier);
            set => SetVar(VarKeyLiftMultiplier, value);
        }

        private float MaxSpeed
        {
            get => GetVar<float>(VarKeyMaxSpeed);
            set => SetVar(VarKeyMaxSpeed, value);
        }

        private float CollisionThreshold
        {
            get => GetVar<float>(VarKeyCollisionThreshold);
            set => SetVar(VarKeyCollisionThreshold, value);
        }
        #endregion

        private static readonly string[] DefaultDebris = {
            "prop_bush_med_02",
            "prop_fncwood_16d",
            "prop_bin_01a",
            "prop_postbox_01a"
        };

        public TDebris(TornadoVortex vortex, Vector3 position, float radius)
        {
            Parent = vortex ?? throw new ArgumentNullException(nameof(vortex));
            _radius = radius;
            _spawnTime = Game.GameTime;
            _rotationSpeed = Probability.GetFloat(MinRotationSpeed, MaxRotationSpeed);
            _heightOffset = new Vector3(0, 0, BaseHeightOffset + Probability.GetFloat(-2f, 2f));
            _currentAngle = Probability.GetFloat(0, (float)Math.PI * 2);
            Name = $"Debris_{Guid.ToString().Substring(0, 8)}";

            // Register events
            RegisterEvent(EventOnSpawn);
            RegisterEvent(EventOnDestroy);
            RegisterEvent(EventOnCollision);
            RegisterEvent(EventOnDamage);

            Initialize(position);
        }

        public override void OnThreadAttached()
        {
            base.OnThreadAttached();

            // Initialize script variables
            ForceMultiplier = 0.6f;
            LiftMultiplier = 0.4f;
            MaxSpeed = 30.0f;
            CollisionThreshold = 10.0f;

            // Notify component initialization
            OnComponentInitialized?.Invoke(Thread, new ScriptComponentEventArgs(this));
        }

        private void HandleCollision(float force)
        {
            if (!IsValid || force < 1.0f) return;

            // Create collision event data
            var collisionData = new CollisionEventData
            {
                Position = _prop.Position,
                Force = force,
                Speed = force * 10,
                Velocity = (_prop.Position - _lastPosition).Normalized * force
            };

            NotifyEvent(EventOnCollision, new ScriptEventArgs(collisionData));

            // Apply random rotation on collision
            var rotation = _prop.Rotation;
            rotation.X += Probability.GetFloat(-force * 10, force * 10);
            rotation.Y += Probability.GetFloat(-force * 10, force * 10);
            rotation.Z += Probability.GetFloat(-force * 10, force * 10);
            _prop.Rotation = rotation;

            // Add some upward force on heavy collisions
            if (force > 5.0f)
            {
                var upwardForce = new Vector3(0, 0, force * 2.0f);
                _prop.ApplyForce(upwardForce);
            }
        }

        private void HandleDamage(float damage)
        {
            if (!IsValid || damage < 1.0f) return;

            var isLethal = damage > 50.0f && Probability.GetFloat(0, 100) < damage;

            // Create damage event data
            var damageData = new DamageEventData
            {
                Damage = damage,
                Position = _prop.Position,
                IsLethal = isLethal
            };

            NotifyEvent(EventOnDamage, new ScriptEventArgs(damageData));

            // Handle lethal damage
            if (isLethal)
            {
                Destroy();
                return;
            }

            // Add some random force based on damage
            var randomDir = new Vector3(
                Probability.GetFloat(-1f, 1f),
                Probability.GetFloat(-1f, 1f),
                Probability.GetFloat(0f, 1f)
            ).Normalized;

            _prop.ApplyForce(randomDir * damage * 0.5f);
        }

        private Prop CreateDebrisProp(Vector3 position)
        {
            _modelName = DefaultDebris[Probability.GetInteger(0, DefaultDebris.Length - 1)];
            var model = new Model(_modelName);

            try
            {
                if (!model.IsLoaded && !model.Request(1000))
                    throw new InvalidOperationException($"Failed to load model: {_modelName}");

                var prop = World.CreateProp(model, position, false, false)
                    ?? throw new InvalidOperationException($"Failed to create prop with model: {_modelName}");

                ConfigureProp(prop);
                return prop;
            }
            finally
            {
                if (model.IsValid)
                {
                    model.MarkAsNoLongerNeeded();
                }
            }
        }

        private static void ConfigureProp(Prop prop)
        {
            prop.LodDistance = DefaultLodDistance;
            Function.Call(Hash.FREEZE_ENTITY_POSITION, prop.Handle, false);
            Function.Call(Hash.SET_ENTITY_DYNAMIC, prop.Handle, true);
            Function.Call(Hash.SET_ENTITY_HAS_GRAVITY, prop.Handle, true);
            Function.Call(Hash.SET_ENTITY_LOAD_COLLISION_FLAG, prop.Handle, true);
        }

        public void Initialize(Vector3 position)
        {
            if (IsValid)
                throw new InvalidOperationException("Entity is already initialized");

            _prop = CreateDebrisProp(position);
            if (_prop == null)
                throw new InvalidOperationException("Failed to create debris prop");

            var centerPos = Parent.Position + _heightOffset;
            _prop.Position = centerPos + new Vector3(
                _radius * (float)Math.Cos(_currentAngle),
                _radius * (float)Math.Sin(_currentAngle),
                0
            );
            _lastPosition = _prop.Position;

            // Create spawn event data
            var spawnData = new SpawnEventData
            {
                Position = _prop.Position,
                ModelName = _modelName,
                Radius = _radius
            };

            NotifyEvent(EventOnSpawn, new ScriptEventArgs(spawnData));
        }

        protected override void UpdateComponent(float deltaTime)
        {
            if (!IsAlive)
            {
                Destroy();
                return;
            }

            // Check for entity validity changes
            if (IsValid != _wasValidLastFrame)
            {
                if (IsValid)
                {
                    var spawnData = new SpawnEventData
                    {
                        Position = _prop.Position,
                        ModelName = _modelName,
                        Radius = _radius
                    };
                    NotifyEvent(EventOnSpawn, new ScriptEventArgs(spawnData));
                }
                else if (_wasValidLastFrame)
                {
                    var destroyData = new SpawnEventData
                    {
                        Position = _lastPosition,
                        ModelName = _modelName,
                        Radius = _radius
                    };
                    NotifyEvent(EventOnDestroy, new ScriptEventArgs(destroyData));
                }
                _wasValidLastFrame = IsValid;
            }

            if (Parent == null || Game.GameTime - _spawnTime > DebrisLifetime)
            {
                Destroy();
                return;
            }

            // Cap delta time to prevent physics issues
            deltaTime = Math.Min(deltaTime, MaxUpdateDelta);

            // Only update physics at fixed intervals
            if (deltaTime >= UpdateInterval)
            {
                UpdateDebrisPhysics(deltaTime);
                CheckCollisions(deltaTime);
            }
        }

        private void CheckCollisions(float deltaTime)
        {
            if (!IsValid) return;

            var currentPos = _prop.Position;
            var velocity = (currentPos - _lastPosition) / deltaTime;
            var speed = velocity.Length();

            // Notify collision if speed changes significantly
            if (speed > CollisionThreshold)
            {
                var collisionData = new CollisionEventData
                {
                    Position = currentPos,
                    Velocity = velocity,
                    Force = speed * 0.1f,
                    Speed = speed
                };

                NotifyEvent(EventOnCollision, new ScriptEventArgs(collisionData));
            }

            _lastPosition = currentPos;
        }

        private void UpdateDebrisPhysics(float deltaTime)
        {
            if (!IsValid) return;

            _currentAngle += _rotationSpeed * deltaTime;
            var centerPos = Parent.Position + _heightOffset;

            // Calculate forces
            var toCenter = centerPos - _prop.Position;
            var distance = toCenter.Length();
            var force = Parent.ForceScale * (1.0f / Math.Max(distance, 1.0f));

            // Scale forces by delta time for consistent physics
            force *= deltaTime * 60.0f; // Normalize to 60 FPS

            // Apply circular motion with configurable force
            var tangent = Vector3.Cross(toCenter.Normalized, Vector3.WorldUp);
            _prop.ApplyForce(tangent * force * ForceMultiplier);

            // Apply lift with configurable multiplier
            var lift = new Vector3(0, 0, force * LiftMultiplier);
            _prop.ApplyForce(lift);

            // Limit speed for stability using configurable max speed
            Function.Call(Hash.SET_ENTITY_MAX_SPEED, _prop.Handle, MaxSpeed);
        }

        public void Destroy()
        {
            if (_isDestroyed) return;
            _isDestroyed = true;

            if (IsValid)
            {
                var destroyData = new SpawnEventData
                {
                    Position = _prop.Position,
                    ModelName = _modelName,
                    Radius = _radius
                };
                NotifyEvent(EventOnDestroy, new ScriptEventArgs(destroyData));

                // Notify component destruction before cleanup
                OnComponentDestroyed?.Invoke(Thread, new ScriptComponentEventArgs(this));

                _prop.Delete();
                _prop = null;
            }
        }

        public override void Dispose()
        {
            Destroy();
            base.Dispose();
        }

        public override Vector3 GetPosition()
        {
            return IsValid ? _prop.Position : base.GetPosition();
        }

        public void Teleport(Vector3 position)
        {
            if (!IsValid) return;

            _lastPosition = position;
            _prop.Position = position;
        }

        public void SetInvincible(bool invincible)
        {
            if (!IsValid) return;

            Function.Call(Hash.SET_ENTITY_INVINCIBLE, _prop.Handle, invincible);
        }
    }
}