using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptCore.Collections;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Utility;

namespace TornadoScript.ScriptMain.Script
{
    public class TornadoVortex : ScriptExtension, IDisposable
    {
        #region Constants
        private const float FIXED_TIME_STEP = 0.016666668f;
        private const float DEFAULT_FORCE_SCALE = 3.0f;
        private const int DEFAULT_MAX_ENTITY_COUNT = 300;
        private const float DEFAULT_COLOR_LERP_DURATION = 200.0f;
        private const float DEFAULT_SCALE = 1.0f;
        private const int DEBRIS_SPAWN_INTERVAL = 500; // milliseconds between debris spawns
        #endregion

        #region Particle System
        private class TornadoParticle : IDisposable
        {
            private bool _disposed;
            private int _particleHandle = -1;
            private readonly object _disposeLock = new object();

            public Vector3 Position { get; set; }
            public Vector3 BaseOffset { get; set; }
            public int Layer { get; set; }
            public float Angle { get; set; }
            public float RotationSpeed { get; set; } = 1.0f;
            public float HeightOffset { get; set; }

            public bool StartFx(string effectAsset = null, string effectName = null, float scale = 1.0f)
{
    if (_disposed) return false;

    try
    {
        CleanupExistingParticle();
        
        // Use more stable default effects if none provided
        effectAsset = effectAsset ?? "scr_trevor3";
        effectName = effectName ?? "scr_trev3_trailer_plume";

        Logger.Log($"Loading particle effect: {effectAsset}/{effectName} at position {Position}");

        // Request the effect asset
        Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, effectAsset);
        
        // Wait longer for asset to load with more attempts
        int attempts = 0;
        const int maxAttempts = 30; // Increased from 20
        while (!Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, effectAsset) && attempts < maxAttempts)
        {
            GTA.Script.Wait(100);
            attempts++;
        }

        if (!Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, effectAsset))
        {
            Logger.Error($"Failed to load particle effect asset {effectAsset} after {maxAttempts} attempts");
            return false;
        }

        Function.Call(Hash.USE_PARTICLE_FX_ASSET, effectAsset);

        // Create particle with more conservative initial parameters
        _particleHandle = Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_AT_COORD,
            effectName,
            Position.X, Position.Y, Position.Z,
            0f, 0f, 0f,  // No rotation
            Math.Min(scale, 3.0f),  // Limit scale to prevent issues
            false, false, false,
            false);

        if (_particleHandle == 0 || _particleHandle == -1)
        {
            Logger.Error($"Failed to create particle effect at position {Position}");
            return false;
        }

        // Set more conservative particle settings
        Function.Call(Hash.SET_PARTICLE_FX_LOOPED_ALPHA, _particleHandle, 0.8f);
        Function.Call(Hash.SET_PARTICLE_FX_LOOPED_SCALE, _particleHandle, Math.Min(scale, 3.0f));
        Function.Call(Hash.SET_PARTICLE_FX_LOOPED_FAR_CLIP_DIST, _particleHandle, 2000.0f);
        
        Logger.Log($"Successfully created particle effect at {Position} with handle {_particleHandle}");
        return true;
    }
    catch (Exception ex)
    {
        Logger.Error($"Error in StartFx: {ex.Message}\nStack trace: {ex.StackTrace}");
        return false;
    }
}

            private bool CleanupExistingParticle()
            {
                if (_particleHandle != -1)
                {
                    try
                    {
                        Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, _particleHandle, false);
                        Function.Call(Hash.REMOVE_PARTICLE_FX, _particleHandle, false);
                        _particleHandle = -1;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error cleaning up particle: {ex.Message}");
                    }
                }
                return true;
            }

            protected virtual void Dispose(bool disposing)
            {
                if (_disposed) return;

                lock (_disposeLock)
                {
                    if (_disposed) return;

                    try
                    {
                        if (_particleHandle != -1)
                        {
                            // First stop the looped effect
                            Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, _particleHandle, false);
                            // Then remove it
                            Function.Call(Hash.REMOVE_PARTICLE_FX, _particleHandle, false);
                            // Finally, remove all particle effects in the area
                            Function.Call(Hash.REMOVE_PARTICLE_FX_IN_RANGE, Position.X, Position.Y, Position.Z, 100f);
                            _particleHandle = -1;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error in Dispose: {ex.Message}");
                        Logger.Error($"Stack trace: {ex.StackTrace}");
                    }

                    _disposed = true;
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            ~TornadoParticle() => Dispose(false);
        }
        #endregion

        #region Entity Management
        private readonly ConcurrentBag<int> _pendingRemovalEntities = new ConcurrentBag<int>();
        private readonly ConcurrentDictionary<int, PulledEntity> _pulledEntities = new ConcurrentDictionary<int, PulledEntity>();
        private readonly object _disposeLock = new object();
        private bool _disposed;
        private bool _isDisposing;
        private bool _despawnRequested;
        private int lastParticleShapeTestTime;
        private Vector3 _position;
        private Vector3 _destination;
        private readonly List<TornadoParticle> _particles = new List<TornadoParticle>();
        private ObjectPool<Entity> _entityPool;
        #endregion

        private bool _isInitialized;
        private Materials LastMaterialTraversed { get; set; } = Materials.Tarmac;
        private float _scale = DEFAULT_SCALE;
        private float _rotationSpeed = 1.0f;
        private float _forceScale = DEFAULT_FORCE_SCALE;

        public float Scale
        {
            get => _scale;
            set
            {
                _scale = Math.Max(0.1f, Math.Min(value, 10.0f)); // Clamp between 0.1 and 10
                UpdateParticleScales();
            }
        }

        public float RotationSpeed
        {
            get => _rotationSpeed;
            set => _rotationSpeed = Math.Max(0.1f, Math.Min(value, 5.0f)); // Clamp between 0.1 and 5
        }

        public float ForceScale
        {
            get => _forceScale;
            set => _forceScale = Math.Max(0.1f, Math.Min(value, 10.0f)); // Clamp between 0.1 and 10
        }

        public Vector3 Position
        {
            get => _position;
            set
            {
                _position = value;
                _destination = value;
            }
        }

        private int _createdTime, _nextUpdateTime, _lastFullUpdateTime, _lastDebrisSpawnTime;
        private int _lifeSpan;

        public const int MaxEntityCount = DEFAULT_MAX_ENTITY_COUNT;
        private readonly List<Model> _loadedModels = new List<Model>();
        private int _scriptFire = -1;

        public bool DespawnRequested
        {
            get => _despawnRequested;
            set => _despawnRequested = value;
        }

        private readonly Ped _player = Helpers.GetLocalPed();
        private Color particleColor = Color.Black;
        private bool _useInternalEntityArray;

        public TornadoVortex(Vector3 initialPosition, bool neverDespawn)
        {
            try
            {
                _position = initialPosition;
                _destination = initialPosition;
                _createdTime = Game.GameTime;
                _nextUpdateTime = Game.GameTime;
                _lastFullUpdateTime = Game.GameTime;

                _lifeSpan = neverDespawn ? -1 : 60000; // Default 60 seconds

                Logger.Log($"Created TornadoVortex at {initialPosition}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in TornadoVortex Constructor: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public override void OnThreadAttached()
        {
            try
            {
                base.OnThreadAttached();
                InitializeScriptVariables();
                InitializeEntityPoolIfNeeded();

                // Initialize other variables
                _createdTime = Game.GameTime;
                _scale = GetOrSetDefaultVar("vortexBaseScale", DEFAULT_SCALE);
                _rotationSpeed = GetOrSetDefaultVar("vortexRotationSpeed", 1.0f);
                _forceScale = GetOrSetDefaultVar("vortexForceScale", DEFAULT_FORCE_SCALE);

                Logger.Log($"TornadoVortex initialized with lifespan: {_lifeSpan}, scale: {_scale}, rotation: {_rotationSpeed}");
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OnThreadAttached: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void InitializeScriptVariables()
        {
            SetDefaultVar("vortexMaxEntityDist", 100f);
            SetDefaultVar("vortexVerticalPullForce", 1.0f);
            SetDefaultVar("vortexHorizontalPullForce", 1.0f);
            SetDefaultVar("vortexTopEntitySpeed", 50.0f);
            SetDefaultVar("vortexMovementEnabled", true);

            if (_lifeSpan > 0) // Only update if not set to never despawn
            {
                _lifeSpan = GetOrSetDefaultVar("vortexLifeSpan", 60000);
            }
        }

        private void InitializeEntityPoolIfNeeded()
        {
            if (GetOrSetDefaultVar("vortexUseEntityPool", true))
            {
                InitializeEntityPool();
            }
        }

        private void InitializeEntityPool()
        {
            // Current pool size of 3 is very small
            // Could cause performance issues with large numbers of entities
            _entityPool = new ObjectPool<Entity>(CreateEntity, 3, ResetEntity);
            Logger.Log("Basic entity pool initialized");

            for (int i = 0; i < 3; i++)
            {
                var entity = _entityPool.Get();
                if (entity != null && entity.Exists())
                {
                    ResetEntity(entity);
                }
            }

            _useInternalEntityArray = true;
        }

        private Entity CreateEntity()
        {
            try
            {
                var model = new Model("prop_barrel_02a");
                if (!model.IsLoaded && !model.Request(1000))
                {
                    Logger.Log("Model load failed - running without entity pool");
                    return null;
                }

                _loadedModels.Add(model);
                var entity = World.CreateProp(model, _position, false, false);
                if (entity != null)
                {
                    entity.IsInvincible = true;
                    Function.Call(Hash.FREEZE_ENTITY_POSITION, entity.Handle, true);
                }
                return entity;
            }
            catch
            {
                return null;
            }
        }

        private void ResetEntity(Entity entity)
        {
            try
            {
                if (entity != null && entity.Exists())
                {
                    entity.Position = _position + new Vector3(0, 0, 100);
                    entity.Velocity = Vector3.Zero;
                }
            }
            catch
            {
                // Ignore reset errors
            }
        }

        private T GetOrSetDefaultVar<T>(string name, T defaultValue)
        {
            try
            {
                var scriptVar = ScriptThread.GetVar<T>(name);
                if (scriptVar == null)
                {
                    Logger.Log($"Variable {name} not found, setting default value: {defaultValue}");
                    ScriptThread.SetVar(name, defaultValue);
                    return defaultValue;
                }

                var value = scriptVar.Value;
                if (EqualityComparer<T>.Default.Equals(value, default(T)))
                {
                    Logger.Log($"Variable {name} has default value, setting to: {defaultValue}");
                    ScriptThread.SetVar(name, defaultValue);
                    return defaultValue;
                }

                Logger.Log($"Found existing value for {name}: {value}");
                return value;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in GetOrSetDefaultVar for {name}: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                ScriptThread.SetVar(name, defaultValue);
                return defaultValue;
            }
        }

        private void SetDefaultVar<T>(string name, T defaultValue)
        {
            if (ScriptThread.GetVar<T>(name) == null)
            {
                Logger.Log($"Setting default value for {name}: {defaultValue}");
                ScriptThread.SetVar(name, defaultValue);
            }
        }

        private float _accumulatedTime;
        private readonly Stopwatch _updateStopwatch = new Stopwatch();

        public override void OnUpdate(int gameTime)
        {
            try
            {
                if (!_isInitialized || _isDisposing) return;

                float deltaTime = Game.LastFrameTime;
                if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                {
                    Logger.Error("Invalid deltaTime in TornadoVortex.OnUpdate");
                    return;
                }

                UpdateComponent(deltaTime);
                UpdateMovementIfNeeded(deltaTime);
                CheckLifespan(gameTime);
                UpdateCrosswinds(gameTime);
                UpdateSurfaceDetection(gameTime);
                UpdatePulledEntities(gameTime);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OnUpdate: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                Dispose();
            }
        }

        private void UpdateMovementIfNeeded(float deltaTime)
        {
            if (_position != _destination)
            {
                UpdateMovement(deltaTime);
            }
        }

        private void CheckLifespan(int gameTime)
        {
            if (_lifeSpan > 0 && gameTime - _createdTime > _lifeSpan)
            {
                DespawnRequested = true;
            }
        }

        private const int ENTITY_UPDATE_BATCH_SIZE = 2;
        private const int UPDATE_INTERVAL_MS = 8;
        private const int MAX_REMOVALS_PER_FRAME = 3;
        private const float MIN_FORCE_THRESHOLD = 0.05f;
        private const float MIN_SPEED_THRESHOLD = 0.1f;

        private float _lastUpdateTime;
        private int _currentEntityBatch;
        private readonly HashSet<int> _processedEntities = new HashSet<int>();

        protected virtual void UpdatePulledEntities(int gameTime)
        {
            try
            {
                if (gameTime - _lastUpdateTime < UPDATE_INTERVAL_MS) return;

                _lastUpdateTime = gameTime;
                var vortexPos = Position;
                float maxEntityDist = ScriptThread.GetVar<float>("vortexMaxEntityDist")?.Value ?? 100f;
                float forceScale = ForceScale;
                float verticalPullForce = ScriptThread.GetVar<float>("vortexVerticalPullForce")?.Value ?? 1.0f;
                float horizontalPullForce = ScriptThread.GetVar<float>("vortexHorizontalPullForce")?.Value ?? 1.0f;
                float topSpeed = ScriptThread.GetVar<float>("vortexTopEntitySpeed")?.Value ?? 50.0f;

                var currentBatch = _pulledEntities.ToList()
                                                .Skip(_currentEntityBatch * ENTITY_UPDATE_BATCH_SIZE)
                                                .Take(ENTITY_UPDATE_BATCH_SIZE)
                                                .ToList();

                if (!currentBatch.Any())
                {
                    _currentEntityBatch = 0;
                    _processedEntities.Clear();
                    return;
                }

                foreach (var kvp in currentBatch)
                {
                    if (_processedEntities.Contains(kvp.Key)) continue;

                    if (kvp.Value?.Entity == null || !kvp.Value.Entity.Exists())
                    {
                        _pendingRemovalEntities.Add(kvp.Key);
                        continue;
                    }

                    var entity = kvp.Value.Entity;
                    var entityPos = entity.Position;

                    float distanceToVortex = Vector3.DistanceSquared(entityPos, vortexPos);
                    float maxDistSquared = maxEntityDist * maxEntityDist;
                    if (distanceToVortex > maxDistSquared)
                    {
                        _pendingRemovalEntities.Add(kvp.Key);
                        continue;
                    }

                    ApplyForcesToEntity(entity, vortexPos, distanceToVortex,
                        horizontalPullForce, verticalPullForce, forceScale, topSpeed, gameTime);

                    _processedEntities.Add(kvp.Key);
                    GTA.Script.Yield();
                }

                UpdateBatchCounter();
                ProcessPendingRemovals();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in UpdatePulledEntities: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }

            GTA.Script.Yield();
        }

        private void ApplyForcesToEntity(Entity entity, Vector3 vortexPos,
            float distanceToVortex, float horizontalPullForce,
            float verticalPullForce, float forceScale, float topSpeed, int gameTime)
        {
            var entityPos = entity.Position;

            var directionToTarget = vortexPos - entityPos;
            directionToTarget.Z = 0;

            float forceMagnitude = directionToTarget.Length();
            if (forceMagnitude > MIN_FORCE_THRESHOLD)
            {
                directionToTarget /= forceMagnitude;

                float distanceScale = 1.0f - (float)Math.Sqrt(distanceToVortex) / (float)Math.Sqrt(horizontalPullForce);
                float scaledForce = horizontalPullForce * forceScale * distanceScale;

                if (scaledForce > MIN_FORCE_THRESHOLD)
                {
                    entity.ApplyForce(directionToTarget * scaledForce);
                }

                float heightDiff = vortexPos.Z - entityPos.Z;
                float verticalScale = Math.Min(1.0f, Math.Max(0.1f, heightDiff / 30.0f));
                float scaledVerticalForce = verticalPullForce * forceScale * verticalScale * distanceScale;

                if (scaledVerticalForce > MIN_FORCE_THRESHOLD)
                {
                    entity.ApplyForce(new Vector3(0, 0, scaledVerticalForce));
                }
            }

            if (gameTime % 16 == 0)
            {
                LimitEntitySpeed(entity, topSpeed);
            }
        }

        private void LimitEntitySpeed(Entity entity, float topSpeed)
        {
            var velocity = entity.Velocity;
            var speed = velocity.Length();
            if (speed > topSpeed + MIN_SPEED_THRESHOLD)
            {
                entity.Velocity = velocity * (topSpeed / speed);
            }
        }

        private void UpdateBatchCounter()
        {
            _currentEntityBatch++;
            if (_currentEntityBatch * ENTITY_UPDATE_BATCH_SIZE >= _pulledEntities.Count)
            {
                _currentEntityBatch = 0;
                _processedEntities.Clear();
            }
        }

        private void ProcessPendingRemovals()
        {
            if (_pendingRemovalEntities.Count > 0)
            {
                foreach (var e in _pendingRemovalEntities.Take(MAX_REMOVALS_PER_FRAME))
                {
                    _pulledEntities.TryRemove(e, out _);
                }

                for (int i = 0; i < MAX_REMOVALS_PER_FRAME && _pendingRemovalEntities.Count > 0; i++)
                {
                    _pendingRemovalEntities.TryTake(out _);
                }
            }
        }

        protected virtual void FixedUpdate(float fixedDeltaTime)
        {
            try
            {
                if (ScriptThread.GetVar<bool>("vortexMovementEnabled")?.Value ?? true)
                {
                    UpdateMovement(fixedDeltaTime);
                }

                UpdatePulledEntities(Game.GameTime);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in FixedUpdate: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateVariables(float deltaTime)
        {
            try
            {
                if (_disposed || _isDisposing || !_isInitialized) return;

                int gameTime = Game.GameTime;

                if (ScriptThread.GetVar<bool>("vortexEnableSurfaceDetection")?.Value ?? false)
                {
                    UpdateSurfaceDetectionIfValid(gameTime);
                }

                if (gameTime - _lastFullUpdateTime >= 250) // 4 times per second
                {
                    CollectNearbyEntities(gameTime);
                    _lastFullUpdateTime = gameTime;
                }

                if (LastMaterialTraversed != Materials.None)
                {
                    UpdateDebrisLayer(LastMaterialTraversed);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in UpdateVariables: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateSurfaceDetectionIfValid(int gameTime)
        {
            if (_position != null && _position != default(Vector3))
            {
                UpdateSurfaceDetection(gameTime);
            }
        }

        protected override void UpdateComponent(float deltaTime)
        {
            if (!_isInitialized || _isDisposing) return;

            try
            {
                _updateStopwatch.Restart();

                if (!float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime))
                {
                    _accumulatedTime += deltaTime;
                }

                while (_accumulatedTime >= FIXED_TIME_STEP)
                {
                    if (!_disposed && !_isDisposing)
                    {
                        FixedUpdate(FIXED_TIME_STEP);
                    }
                    _accumulatedTime -= FIXED_TIME_STEP;
                }

                if (!_disposed && !_isDisposing)
                {
                    UpdateVariables(deltaTime);
                }

                _updateStopwatch.Stop();
                if (_updateStopwatch.ElapsedMilliseconds > 16)
                {
                    Logger.Log($"Long update detected: {_updateStopwatch.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in UpdateComponent: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                try
                {
                    Dispose();
                }
                catch (Exception disposeEx)
                {
                    Logger.Error($"Error during emergency dispose: {disposeEx.Message}");
                }
            }
        }

        private bool IsValid => !_disposed && !_isDisposing && _isInitialized && _position != null && _pulledEntities != null;

        private void UpdateMovement(float deltaTime)
        {
            try
            {
                // Early return if position or destination not set
                if (_position == null)
                {
                    Logger.Log("MoveTowardsDestination: Position is null");
                    return;
                }

                // If no destination is set, use current position
                if (_destination == null)
                {
                    _destination = _position;
                    Logger.Log("MoveTowardsDestination: Destination was null, set to current position");
                    return;
                }

                float moveSpeed;
                try
                {
                    moveSpeed = GetOrSetDefaultVar<float>("vortexMoveSpeedScale", 1.0f) * 0.287f;
                }
                catch
                {
                    moveSpeed = 0.287f; // Default if variable access fails
                }

                Vector3 moveDirection = (_destination - _position);
                float distance = moveDirection.Length();

                // If we're close enough to destination, stop moving
                if (distance < 0.1f)
                {
                    return;
                }

                moveDirection.Normalize();
                _position += moveDirection * moveSpeed * deltaTime;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in MoveTowardsDestination: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            lock (_disposeLock)
            {
                if (_disposed) return;
                _isDisposing = true;

                if (disposing)
                {
                    CleanupEntityPool();
                    CleanupPulledEntities();
                    CleanupParticles();
                    CleanupModels();
                    CleanupNativeResources();
                }

                _disposed = true;
                _isDisposing = false;
            }
        }

        private void CleanupEntityPool()
        {
            if (_useInternalEntityArray && _entityPool != null)
            {
                foreach (var entity in _entityPool)
                {
                    if (entity != null && entity.Exists())
                    {
                        ResetEntityBeforeDeletion(entity);
                        entity.Delete();
                    }
                }
                _entityPool.Clear();
            }
        }

        private void ResetEntityBeforeDeletion(Entity entity)
        {
            Function.Call(Hash.FREEZE_ENTITY_POSITION, entity.Handle, false);
            Function.Call(Hash.SET_ENTITY_DYNAMIC, entity.Handle, true);
            Function.Call(Hash.SET_ENTITY_HAS_GRAVITY, entity.Handle, true);

            if (entity is Ped ped)
            {
                ped.Task.ClearAllImmediately();
            }

            entity.Velocity = Vector3.Zero;
            entity.LocalRotationVelocity = Vector3.Zero;
        }

        private void CleanupPulledEntities()
        {
            foreach (var kvp in _pulledEntities)
            {
                kvp.Value?.Dispose();
            }
            _pulledEntities.Clear();
            while (_pendingRemovalEntities.TryTake(out _)) { }
        }

        private void CleanupParticles()
        {
            foreach (var particle in _particles)
            {
                particle?.Dispose();
            }
            _particles.Clear();
        }

        private void CleanupModels()
        {
            foreach (var model in _loadedModels)
            {
                if (model.IsValid && model.IsLoaded)
                {
                    model.MarkAsNoLongerNeeded();
                }
            }
            _loadedModels.Clear();
        }

        private void CleanupNativeResources()
        {
            if (_scriptFire != -1)
            {
                Function.Call(Hash.REMOVE_SCRIPT_FIRE, _scriptFire);
                _scriptFire = -1;
            }
            Function.Call(Hash.STOP_FIRE_IN_RANGE, Position.X, Position.Y, Position.Z, 100f);
        }

        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
            base.Dispose();
        }

        ~TornadoVortex() => Dispose(false);

        private Entity GetOrCreateEntity(Vector3 position)
        {
            if (!_useInternalEntityArray || _entityPool == null) return null;

            var entity = _entityPool.Get();
            if (entity != null && entity.Exists())
            {
                entity.Position = position;
                Function.Call(Hash.FREEZE_ENTITY_POSITION, entity.Handle, false);
                return entity;
            }

            return null;
        }

        private void ReturnEntityToPool(Entity entity)
        {
            if (entity == null || !entity.Exists() || !_useInternalEntityArray || _entityPool == null) return;

            if (_entityPool.Return(entity))
            {
                Logger.Log($"Returned entity to pool. Current pool size: {_entityPool.Count}");
            }
            else
            {
                entity.Delete();
                Logger.Log("Pool full, deleted entity");
            }
        }

        public bool Build()
        {
            try
            {
                ClearExistingParticles();

                // Improved tornado shape parameters
                int numParticles = 80;  // More particles for denser appearance
                float baseScale = 3.0f;
                float heightStep = 2.5f; // Closer particles vertically
                
                // Create funnel shape
                for (int layer = 0; layer < numParticles; layer++)
                {
                    float heightRatio = layer / (float)numParticles;
                    // Exponential funnel shape formula
                    float radius = 20.0f * (float)Math.Pow(1.0f - heightRatio, 2) + 1.0f;
                    
                    var particle = new TornadoParticle
                    {
                        Position = _position + new Vector3(0, 0, layer * heightStep),
                        Layer = layer,
                        BaseOffset = new Vector3(radius, radius, 0),
                        // Faster rotation at bottom
                        RotationSpeed = 4.0f + ((1.0f - heightRatio) * 3.0f),
                        HeightOffset = layer * heightStep
                    };

                    // Use more intense particle effect
                    if (particle.StartFx("scr_trevor3", "scr_trev3_trailer_plume", 
                        baseScale * (1.0f + heightRatio * 0.5f)))
                    {
                        _particles.Add(particle);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in Build: {ex.Message}");
                return false;
            }
        }

        private void ClearExistingParticles()
        {
            foreach (var particle in _particles)
            {
                particle?.Dispose();
            }
            _particles.Clear();
        }

        private void ChangeDestination(bool trackToPlayer)
        {
            for (int i = 0; i < 50; i++)
            {
                _destination = trackToPlayer ? _player.Position.Around(130.0f) : Helpers.GetRandomPositionFromCoords(_destination, 100.0f);

                float groundHeight;
                World.GetGroundHeight(_destination, out groundHeight);
                _destination.Z = groundHeight - 10.0f;

                var nearestRoadPos = World.GetNextPositionOnStreet(_destination);

                if (_destination.DistanceTo(nearestRoadPos) < 40.0f && Math.Abs(nearestRoadPos.Z - _destination.Z) < 10.0f)
                {
                    break;
                }
            }
        }

        public void ReleaseEntity(int entityIdx)
        {
            _pendingRemovalEntities.Add(entityIdx);
        }

        private void AddEntity(Entity entity, float xBias, float yBias)
        {
            var pulledEntity = new PulledEntity(entity, entity.Position.DistanceTo(_position))
            {
                XBias = xBias,
                YBias = yBias,
                IsPullingIn = true,
                LastPosition = entity.Position
            };
            _pulledEntities.TryAdd(entity.Handle, pulledEntity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CollectNearbyEntities(int gameTime)
        {
            if (gameTime < _nextUpdateTime) return;

            foreach (var ent in World.GetAllEntities())
            {
                if (_pulledEntities.Count >= MaxEntityCount) break;
                if (_pulledEntities.ContainsKey(ent.Handle) || ent.Position.DistanceTo2D(_position) > 100f + 4.0f || ent.HeightAboveGround > 300.0f) continue;

                if (ent is Ped && !(ent as Ped).IsRagdoll)
                {
                    Function.Call(Hash.SET_PED_TO_RAGDOLL, ent.Handle, 800, 1500, 2, 1, 1, 0);
                }

                AddEntity(ent, 3.0f * Probability.GetScalar(), 3.0f * Probability.GetScalar());
            }

            _nextUpdateTime = gameTime + 600;
        }

        private void UpdateCrosswinds(int gameTime)
        {
            try
            {
                if (gameTime - lastParticleShapeTestTime < 100) return; // Limit update frequency

                var crosswindStrength = ScriptThread.GetVar<float>("vortexCrosswindStrength")?.Value ?? 1.0f;
                var crosswindRadius = ScriptThread.GetVar<float>("vortexCrosswindRadius")?.Value ?? 50.0f;

                // Create crosswind effects around tornado
                for (float angle = 0; angle < 360; angle += 45)
                {
                    var radians = angle * (Math.PI / 180);
                    var offset = new Vector3(
                        (float)Math.Cos(radians) * crosswindRadius,
                        (float)Math.Sin(radians) * crosswindRadius,
                        0
                    );

                    var windPos = _position + offset;
                    Function.Call(Hash.SET_PARTICLE_FX_NON_LOOPED_COLOUR, 0.5f, 0.5f, 0.5f);
                    Function.Call(Hash.USE_PARTICLE_FX_ASSET, "core");
                    Function.Call(Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD,
                        "ent_dst_gen_water_spray",
                        windPos.X, windPos.Y, windPos.Z,
                        0.0f, 0.0f, 0.0f,
                        crosswindStrength,
                        false, false, false);
                }

                lastParticleShapeTestTime = gameTime;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in UpdateCrosswinds: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateSurfaceDetection(int gameTime)
        {
            try
            {
                if (_position == null || _position == default(Vector3)) return;

                // Perform raycast to detect ground material
                float groundHeight;
                World.GetGroundHeight(_position, out groundHeight);
                var surfacePos = new Vector3(_position.X, _position.Y, groundHeight);

                var rayHandle = Function.Call<int>(Hash.START_SHAPE_TEST_LOS_PROBE,
                    surfacePos.X, surfacePos.Y, surfacePos.Z + 1.0f,
                    surfacePos.X, surfacePos.Y, surfacePos.Z - 1.0f,
                    -1, 0, 0);

                OutputArgument outPosition = new OutputArgument();
                OutputArgument outNormal = new OutputArgument();
                OutputArgument outHit = new OutputArgument();
                OutputArgument outMaterialHash = new OutputArgument();
                OutputArgument outEntityHit = new OutputArgument();

                bool hitResult = Function.Call<bool>(Hash.GET_SHAPE_TEST_RESULT, rayHandle, outHit, outPosition,
                    outNormal, outMaterialHash, outEntityHit);

                if (hitResult)
                {
                    int hit = outHit.GetResult<int>();
                    if (hit != 0)
                    {
                        Vector3 endCoords = outPosition.GetResult<Vector3>();
                        Vector3 surfaceNormal = outNormal.GetResult<Vector3>();
                        int materialHash = outMaterialHash.GetResult<int>();
                        var material = (Materials)materialHash;
                        if (material != LastMaterialTraversed)
                        {
                            LastMaterialTraversed = material;
                            Logger.Log($"Surface material changed to: {material}");

                            // Update particle effects based on material
                            UpdateParticleEffectsForMaterial(material);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in UpdateSurfaceDetection: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateDebrisLayer(Materials material)
        {
            try
            {
                if (!IsValid) return;

                var debrisSpawnInterval = ScriptThread.GetVar<int>("vortexDebrisSpawnInterval")?.Value ?? DEBRIS_SPAWN_INTERVAL;
                var gameTime = Game.GameTime;

                if (gameTime - _lastDebrisSpawnTime < debrisSpawnInterval) return;

                var debrisConfig = GetDebrisConfigForMaterial(material);
                if (debrisConfig == null) return;

                // Spawn debris with material-specific properties
                var spawnPos = _position + new Vector3(
                    Probability.GetScalar() * 10.0f - 5.0f,
                    Probability.GetScalar() * 10.0f - 5.0f,
                    0.0f
                );

                var debris = new TDebris(this, spawnPos, 5.0f)
                {
                    ForceMultiplier = debrisConfig.ForceMultiplier,
                    LiftMultiplier = debrisConfig.LiftMultiplier,
                    MaxSpeed = debrisConfig.MaxSpeed
                };

                if (debris.Initialize(debrisConfig.ModelName))
                {
                    Logger.Log($"Spawned {material} debris at {spawnPos}");
                }

                _lastDebrisSpawnTime = gameTime;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in UpdateDebrisLayer: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateParticleEffectsForMaterial(Materials material)
        {
            try
            {
                string effectAsset = "core";
                string effectName = "ent_dst_gen_dust";
                float scale = 1.0f;

                switch (material)
                {
                    case Materials.Water:
                        effectAsset = "core";
                        effectName = "ent_dst_gen_water_spray";
                        scale = 1.5f;
                        break;
                    case Materials.Grass:
                    case Materials.GrassLong:
                    case Materials.GrassShort:
                    case Materials.DirtTrack:
                    case Materials.MudHard:
                    case Materials.MudSoft:
                    case Materials.Soil:
                        effectAsset = "core";
                        effectName = "ent_dst_gen_grass";
                        scale = 1.2f;
                        break;
                    case Materials.SandLoose:
                    case Materials.SandCompact:
                    case Materials.SandWet:
                    case Materials.SandTrack:
                    case Materials.SandDryDeep:
                    case Materials.SandWetDeep:
                    case Materials.SandstoneSolid:
                    case Materials.SandstoneBrittle:
                        effectAsset = "core";
                        effectName = "ent_dst_gen_sand";
                        scale = 1.3f;
                        break;
                    case Materials.Tarmac:
                    case Materials.Concrete:
                        effectAsset = "core";
                        effectName = "ent_dst_gen_dust";
                        scale = 1.0f;
                        break;
                }

                foreach (var particle in _particles)
                {
                    particle?.StartFx(effectAsset, effectName, scale);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in UpdateParticleEffectsForMaterial: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private class DebrisConfig
        {
            public string ModelName { get; set; }
            public float ForceMultiplier { get; set; }
            public float LiftMultiplier { get; set; }
            public float MaxSpeed { get; set; }
        }

        private DebrisConfig GetDebrisConfigForMaterial(Materials material)
        {
            switch (material)
            {
                case Materials.Water:
                    return new DebrisConfig
                    {
                        ModelName = "prop_watercrate_01",
                        ForceMultiplier = 1.2f,
                        LiftMultiplier = 1.5f,
                        MaxSpeed = 40.0f
                    };
                case Materials.Grass:
                    return new DebrisConfig
                    {
                        ModelName = "prop_veg_crop_01",
                        ForceMultiplier = 1.0f,
                        LiftMultiplier = 1.3f,
                        MaxSpeed = 45.0f
                    };
                case Materials.SandLoose:
                case Materials.SandCompact:
                case Materials.SandWet:
                case Materials.SandTrack:
                case Materials.SandDryDeep:
                case Materials.SandWetDeep:
                case Materials.SandstoneSolid:
                case Materials.SandstoneBrittle:
                    return new DebrisConfig
                    {
                        ModelName = "prop_beach_sandcas_01",
                        ForceMultiplier = 0.8f,
                        LiftMultiplier = 1.1f,
                        MaxSpeed = 35.0f
                    };
                case Materials.Tarmac:
                case Materials.Concrete:
                    return new DebrisConfig
                    {
                        ModelName = "prop_rub_binbag_01",
                        ForceMultiplier = 1.1f,
                        LiftMultiplier = 1.2f,
                        MaxSpeed = 50.0f
                    };
                default:
                    return new DebrisConfig
                    {
                        ModelName = "prop_barrel_02a",
                        ForceMultiplier = 1.0f,
                        LiftMultiplier = 1.0f,
                        MaxSpeed = 40.0f
                    };
            }
        }

        private void UpdateParticleScales()
        {
            try
            {
                foreach (var particle in _particles)
                {
                    if (particle != null)
                    {
                        particle.StartFx(scale: _scale);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in UpdateParticleScales: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}