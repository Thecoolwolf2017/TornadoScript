using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptCore.Collections;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Memory;
using TornadoScript.ScriptMain.Utility;

namespace TornadoScript.ScriptMain.Script
{
    public class TornadoVortex : ScriptExtension, IDisposable
    {
        #region Constants
        private const float FIXED_TIME_STEP = 0.016666668f;
        private const float DEFAULT_FORCE_SCALE = 3.0f;
        private const float DEFAULT_INTERNAL_FORCES_DIST = 5.0f;
        private const int DEFAULT_MAX_ENTITY_COUNT = 300;
        private const float DEFAULT_ENTITY_REUSE_DISTANCE = 20f;
        private const int DEFAULT_MAX_POOL_SIZE = 50;
        private const float DEFAULT_COLOR_LERP_DURATION = 200.0f;
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

            public async Task<bool> StartFxAsync(string effectAsset = null, string effectName = null, float scale = 1.0f)
            {
                if (_disposed) return false;

                try
                {
                    await CleanupExistingParticle();

                    effectAsset ??= ScriptThread.GetVar<string>("vortexParticleAsset")?.Value ?? "scr_rcbarry2";
                    effectName ??= ScriptThread.GetVar<string>("vortexParticleName")?.Value ?? "scr_clown_appears";

                    Logger.Log($"Loading particle effect: {effectAsset}/{effectName}");

                    if (!await LoadParticleAsset(effectAsset))
                        return false;

                    return await CreateParticleEffect(effectName, scale);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in StartFx: {ex.Message}");
                    Logger.Error($"Stack trace: {ex.StackTrace}");
                    return false;
                }
            }

            private async Task<bool> LoadParticleAsset(string effectAsset)
            {
                Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, effectAsset);

                const int maxAttempts = 50;
                int attempts = 0;

                while (!Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, effectAsset))
                {
                    if (attempts >= maxAttempts)
                    {
                        Logger.Error($"Timed out waiting for particle effect asset {effectAsset}");
                        return false;
                    }
                    await Task.Delay(100);
                    attempts++;
                }

                return true;
            }

            private async Task<bool> CreateParticleEffect(string effectName, float scale)
            {
                try
                {
                    await Task.Yield(); // Ensure we're running asynchronously

                    Function.Call(Hash.USE_PARTICLE_FX_ASSET, effectName);

                    _particleHandle = Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_AT_COORD,
                        effectName,
                        Position.X, Position.Y, Position.Z,
                        0f, 0f, 0f,
                        scale,
                        false, false, false,
                        false);

                    if (_particleHandle == -1)
                    {
                        Logger.Error($"Failed to create particle effect {effectName}");
                        return false;
                    }

                    Logger.Log($"Created particle effect {effectName} at {Position}");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error creating particle effect: {ex.Message}");
                    Logger.Error($"Stack trace: {ex.StackTrace}");
                    return false;
                }
            }

            private async Task CleanupExistingParticle()
            {
                try
                {
                    if (_particleHandle != -1)
                    {
                        Function.Call(Hash.REMOVE_PARTICLE_FX, _particleHandle, false);
                        _particleHandle = -1;
                        await Task.Delay(50); // Small delay to ensure cleanup
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error cleaning up particle: {ex.Message}");
                    Logger.Error($"Stack trace: {ex.StackTrace}");
                }
            }

            public void Dispose()
            {
                if (_disposed) return;

                lock (_disposeLock)
                {
                    if (_disposed) return;

                    try
                    {
                        if (_particleHandle != -1)
                        {
                            Function.Call(Hash.REMOVE_PARTICLE_FX, _particleHandle, false);
                            _particleHandle = -1;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error disposing particle: {ex.Message}");
                    }

                    _disposed = true;
                }
            }

            ~TornadoParticle()
            {
                Dispose();
            }
        }

        #endregion

        #region Entity Management
        private readonly ConcurrentDictionary<int, PulledEntity> _pulledEntities;
        private readonly ConcurrentBag<int> _pendingRemovalEntities;
        private readonly List<TornadoParticle> _particles = new List<TornadoParticle>();
        private ObjectPool<Entity> _entityPool;
        #endregion

        private bool _isInitialized;

        private Materials LastMaterialTraversed { get; set; } = Materials.Tarmac;
        /// <summary>
        /// Scale of the vortex forces.
        /// </summary>
        public float ForceScale { get; } = DEFAULT_FORCE_SCALE;

        /// <summary>
        /// Maximum distance entites must be from the vortex before we start using internal vortext forces on them.
        /// </summary>
        public float InternalForcesDist { get; } = DEFAULT_INTERNAL_FORCES_DIST;

        private int _createdTime, _nextUpdateTime;
        private int _lastDebrisSpawnTime = 0;
        private int _lastFullUpdateTime;
        private int _lifeSpan;

        public const int MaxEntityCount = DEFAULT_MAX_ENTITY_COUNT;

        private Vector3 _position, _destination;
        private bool _despawnRequested;
        private bool _disposed;
        private bool _isDisposing;
        private readonly object _disposeLock = new object();

        private readonly List<Model> _loadedModels = new List<Model>();
        private int _scriptFire = -1;

        public Vector3 Position
        {
            get { return _position; }
            set { _position = value; }
        }

        public bool DespawnRequested
        {
            get { return _despawnRequested; }
            set { _despawnRequested = value; }
        }

        private readonly Ped _player = Helpers.GetLocalPed();
        private int _lastPlayerShapeTestTime;
        private bool _lastRaycastResultFailed;
        private int lastParticleShapeTestTime = 0;
        private Color particleColorPrev, particleColorGoal;
        private Color particleColor = Color.Black;
        private float particleLerpTime = 0.0f;
        private const float ColorLerpDuration = DEFAULT_COLOR_LERP_DURATION;
        private bool _useInternalEntityArray = false;

        // todo: Add crosswinds at vortex base w/ raycast
        public TornadoVortex(Vector3 initialPosition, bool neverDespawn)
        {
            _pulledEntities = new ConcurrentDictionary<int, PulledEntity>();
            _pendingRemovalEntities = new ConcurrentBag<int>();

            try
            {
                _position = initialPosition;
                _destination = initialPosition;
                _createdTime = Game.GameTime;
                _nextUpdateTime = Game.GameTime;
                _lastFullUpdateTime = Game.GameTime;

                // Default lifespan if script vars not initialized yet
                _lifeSpan = neverDespawn ? -1 : 60000; // Default 60 seconds

                Logger.Log($"Created TornadoVortex at {initialPosition}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in TornadoVortex constructor: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public override void OnThreadAttached()
        {
            try
            {
                base.OnThreadAttached();

                // Now that script thread is initialized, we can safely get script vars
                if (_lifeSpan > 0) // Only update if not set to never despawn
                {
                    _lifeSpan = GetOrSetDefaultVar("vortexLifeSpan", 60000);
                }

                // Initialize entity pool if enabled
                _useInternalEntityArray = GetOrSetDefaultVar("vortexUseEntityPool", true);
                if (_useInternalEntityArray)
                {
                    InitializeEntityPool();
                }

                Logger.Log($"TornadoVortex initialized with lifespan: {_lifeSpan}");
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in TornadoVortex.OnThreadAttached: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void InitializeEntityPool()
        {
            if (_useInternalEntityArray)
            {
                var model = new Model("prop_barrel_02a");
                _loadedModels.Add(model);

                _entityPool = new ObjectPool<Entity>(
                    () =>
                    {
                        if (!model.IsLoaded && !model.Request(1000))
                            throw new InvalidOperationException("Failed to load model: prop_barrel_02a");

                        var entity = World.CreateProp(model, _position, false, false);
                        if (entity != null)
                        {
                            entity.IsInvincible = true;
                            Function.Call(Hash.FREEZE_ENTITY_POSITION, entity.Handle, true);
                        }
                        return entity;
                    },
                    DEFAULT_MAX_POOL_SIZE,
                    (entity) =>
                    {
                        if (entity != null && entity.Exists())
                        {
                            Function.Call(Hash.FREEZE_ENTITY_POSITION, entity.Handle, true);
                            entity.Position = _position + new Vector3(0, 0, 1000); // Move far up
                            entity.Velocity = Vector3.Zero;
                            entity.LocalRotationVelocity = Vector3.Zero;
                            entity.Quaternion = Quaternion.Identity;
                        }
                    });

                Logger.Log($"Initialized entity pool with max size: {DEFAULT_MAX_POOL_SIZE}");
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
                if (typeof(T) == typeof(float) && EqualityComparer<T>.Default.Equals(value, default(T)))
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
                Logger.Error($"Error getting/setting variable {name}: {ex.Message}");
                ScriptThread.SetVar(name, defaultValue);
                return defaultValue;
            }
        }

        private float _accumulatedTime;
        private readonly Stopwatch _updateStopwatch = new Stopwatch();

        protected override void UpdateComponent(float deltaTime)
        {
            if (!_isInitialized || _isDisposing) return;

            try
            {
                _updateStopwatch.Restart();

                // Accumulate time for fixed updates
                _accumulatedTime += deltaTime;

                // Run fixed time updates
                while (_accumulatedTime >= FIXED_TIME_STEP)
                {
                    FixedUpdate(FIXED_TIME_STEP);
                    _accumulatedTime -= FIXED_TIME_STEP;
                }

                // Run variable time updates
                VariableUpdate(deltaTime);

                _updateStopwatch.Stop();
                if (_updateStopwatch.ElapsedMilliseconds > 16) // Log if update takes longer than one frame at 60fps
                {
                    Logger.Log($"Long update detected: {_updateStopwatch.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in TornadoVortex.UpdateComponent: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                Dispose();
            }
        }

        public override void OnUpdate(int gameTime)
        {
            try
            {
                // Early exit if not initialized or disposing
                if (!_isInitialized || _isDisposing)
                {
                    return;
                }

                // Ensure ScriptThread.Vars is available
                if (ScriptThread.Vars == null)
                {
                    Logger.Error("ScriptThread.Vars is null in TornadoVortex.OnUpdate");
                    return;
                }

                // Calculate delta time with safety check
                float deltaTime = Game.LastFrameTime;
                if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                {
                    Logger.Error("Invalid deltaTime in TornadoVortex.OnUpdate");
                    return;
                }

                // Update component with delta time
                UpdateComponent(deltaTime);

                // Update position if needed
                if (_position != _destination)
                {
                    UpdateMovement(deltaTime);
                }

                // Check lifespan
                if (_lifeSpan > 0 && Game.GameTime - _createdTime > _lifeSpan)
                {
                    _despawnRequested = true;
                }

                // Update vortex effects with null checks
                if (!_isDisposing)
                {
                    UpdateCrosswinds(gameTime);
                    UpdateSurfaceDetection(gameTime);
                    UpdateDebrisLayer(LastMaterialTraversed);

                    // Collect and update entities
                    if (_pulledEntities != null)
                    {
                        CollectNearbyEntities(gameTime, 100f);
                        UpdatePulledEntities(gameTime, 100f);

                        // Cleanup pending entities
                        if (_pendingRemovalEntities?.Count > 0)
                        {
                            foreach (int handle in _pendingRemovalEntities)
                            {
                                if (_pulledEntities.TryGetValue(handle, out PulledEntity entity))
                                {
                                    _pulledEntities.TryRemove(handle, out _);
                                    entity?.Dispose();
                                }
                            }
                            while (_pendingRemovalEntities.TryTake(out _)) { }
                        }
                    }
                }

                base.OnUpdate(gameTime);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in TornadoVortex.Update: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                Dispose();
            }
        }

        private void FixedUpdate(float fixedDeltaTime)
        {
            // Physics and movement updates (need to be frame-rate independent)
            if (ScriptThread.GetVar<bool>("vortexMovementEnabled"))
            {
                UpdateMovement(fixedDeltaTime);
            }

            float maxEntityDist = ScriptThread.GetVar<float>("vortexMaxEntityDist");
            UpdatePulledEntities(Game.GameTime, maxEntityDist);
        }

        private void VariableUpdate(float deltaTime)
        {
            int gameTime = Game.GameTime;

            // Visual and non-physics updates (can run at variable rate)
            if (ScriptThread.GetVar<bool>("vortexEnableSurfaceDetection"))
            {
                UpdateSurfaceDetection(gameTime);
            }

            // Collect entities less frequently to reduce performance impact
            if (gameTime - _lastFullUpdateTime >= 250) // 4 times per second
            {
                float maxEntityDist = ScriptThread.GetVar<float>("vortexMaxEntityDist");
                CollectNearbyEntities(gameTime, maxEntityDist);
                _lastFullUpdateTime = gameTime;
            }

            UpdateDebrisLayer(LastMaterialTraversed);
        }

        private void UpdateMovement(float deltaTime)
        {
            // Check if we need to change destination
            if (_destination == Vector3.Zero || _position.DistanceTo(_destination) < 15.0f)
            {
                ChangeDestination(false);
            }

            if (_position.DistanceTo(_player.Position) > 200.0f)
            {
                ChangeDestination(true);
            }

            // Frame-independent movement calculation
            float moveSpeed = ScriptThread.GetVar<float>("vortexMoveSpeedScale") * 0.287f;
            Vector3 moveDirection = (_destination - _position);
            if (moveDirection.Length() > 0.001f)
            {
                moveDirection.Normalize();
                Vector3 targetPosition = _position + (moveDirection * moveSpeed * deltaTime * 60f); // Scale for 60fps equivalent
                _position = Vector3.Lerp(_position, targetPosition, deltaTime * 20.0f);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            lock (_disposeLock)
            {
                if (_disposed) return;
                _isDisposing = true;

                if (disposing)
                {
                    try
                    {
                        // Clean up entity pool with proper model cleanup
                        if (_useInternalEntityArray && _entityPool != null)
                        {
                            foreach (var entity in _entityPool)
                            {
                                if (entity != null && entity.Exists())
                                {
                                    // Reset entity state before deletion
                                    Function.Call(Hash.FREEZE_ENTITY_POSITION, entity.Handle, false);
                                    Function.Call(Hash.SET_ENTITY_DYNAMIC, entity.Handle, true);
                                    Function.Call(Hash.SET_ENTITY_HAS_GRAVITY, entity.Handle, true);

                                    // Clear any tasks if it's a Ped
                                    if (entity is Ped ped)
                                    {
                                        ped.Task.ClearAllImmediately();
                                    }

                                    // Reset physics state
                                    entity.Velocity = Vector3.Zero;
                                    entity.LocalRotationVelocity = Vector3.Zero;

                                    // Delete the entity
                                    entity.Delete();
                                }
                            }
                            _entityPool.Clear();
                        }

                        // Clean up pulled entities with proper disposal
                        foreach (var kvp in _pulledEntities)
                        {
                            if (kvp.Value != null)
                            {
                                kvp.Value.Dispose();
                            }
                        }
                        _pulledEntities.Clear();
                        while (_pendingRemovalEntities.TryTake(out _)) { }

                        // Clean up particles with proper model cleanup
                        foreach (var particle in _particles)
                        {
                            if (particle != null)
                            {
                                // Stop particle effects
                                particle.Dispose();
                            }
                        }
                        _particles.Clear();

                        // Clean up any models that were loaded
                        foreach (var model in _loadedModels)
                        {
                            if (model.IsValid && model.IsLoaded)
                            {
                                model.MarkAsNoLongerNeeded();
                            }
                        }
                        _loadedModels.Clear();

                        // Clean up any remaining native resources
                        if (_scriptFire != -1)
                        {
                            Function.Call(Hash.REMOVE_SCRIPT_FIRE, _scriptFire);
                            _scriptFire = -1;
                        }
                        Function.Call(Hash.STOP_FIRE_IN_RANGE, Position.X, Position.Y, Position.Z, 100f);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error in TornadoVortex.Dispose: {ex.Message}");
                        Logger.Error($"Stack trace: {ex.StackTrace}");
                    }
                }

                _disposed = true;
                _isDisposing = false;
            }
        }

        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
            base.Dispose();
        }

        ~TornadoVortex()
        {
            Dispose(false);
        }

        private Entity GetOrCreateEntity(Vector3 position)
        {
            if (!_useInternalEntityArray || _entityPool == null)
                return null;

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
            if (entity == null || !entity.Exists() || !_useInternalEntityArray || _entityPool == null)
                return;

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

        public async Task<bool> Build()
        {
            try
            {
                if (_position == null || _position == default(Vector3))
                {
                    Logger.Error("Cannot build vortex - position not initialized");
                    return false;
                }

                // Clear existing particles
                foreach (var particle in _particles)
                {
                    particle?.Dispose();
                }
                _particles.Clear();

                // Get configuration with defaults
                var numParticlesVar = ScriptThread.GetVar<int>("vortexParticleCount");
                var particleScaleVar = ScriptThread.GetVar<float>("vortexParticleScale");

                int numParticles = numParticlesVar?.Value ?? 10; // Default to 10 particles
                float particleScale = particleScaleVar?.Value ?? 1.0f; // Default to scale 1.0

                Logger.Log($"Building vortex at position {_position} with {numParticles} particles at scale {particleScale}");

                for (int layer = 0; layer < numParticles; layer++)
                {
                    float heightOffset = layer * 2.0f;
                    float angle = layer * 137.5f;

                    Vector3 offset = new Vector3(
                        (float)Math.Cos(angle * Math.PI / 180.0) * 2.0f,
                        (float)Math.Sin(angle * Math.PI / 180.0) * 2.0f,
                        heightOffset
                    );

                    Vector3 particlePosition = _position + offset;

                    var particle = new TornadoParticle()
                    {
                        Position = particlePosition,
                        BaseOffset = offset,
                        Layer = layer,
                        Angle = angle
                    };

                    if (particle != null)
                    {
                        try
                        {
                            Logger.Log($"Creating particle at position {particlePosition}");
                            await particle.StartFxAsync(scale: particleScale);
                            _particles.Add(particle);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to start particle effect: {ex.Message}");
                            particle.Dispose();
                        }
                    }
                }

                Logger.Log($"Successfully built vortex with {_particles.Count} particles");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in Build: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                return false;
            }
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

        /// <summary>
        /// Adds an entity to be processed by the tornado vortex
        /// </summary>
        /// <param name="entity">The entity to add</param>
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
        private void CollectNearbyEntities(int gameTime, float maxDistanceDelta)
        {
            if (gameTime < _nextUpdateTime)
                return;

            foreach (var ent in MemoryAccess.GetAllEntities())
            {
                if (_pulledEntities.Count >= MaxEntityCount) break;

                if (_pulledEntities.ContainsKey(ent.Handle) ||
                        ent.Position.DistanceTo2D(_position) > maxDistanceDelta + 4.0f || ent.HeightAboveGround > 300.0f) continue;

                if (ent is Ped && /*entities[p].Handle != _player.Handle &&*/ !(ent as Ped).IsRagdoll)
                {
                    Function.Call(Hash.SET_PED_TO_RAGDOLL, ent.Handle, 800, 1500, 2, 1, 1, 0);
                }

                AddEntity(ent, 3.0f * Probability.GetScalar(), 3.0f * Probability.GetScalar());
            }

            _nextUpdateTime = gameTime + 600;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CollectNearbyEntitiesInternal(int gameTime, float maxDistanceDelta)
        {
            if (gameTime - _lastFullUpdateTime > 5000)
            {
                // Cache entities periodically
                _lastFullUpdateTime = gameTime;
            }

            if (gameTime > _nextUpdateTime)
            {
                // Collect all types of entities
                var entities = new List<Entity>();
                entities.AddRange(World.GetAllPeds());
                entities.AddRange(World.GetAllVehicles());
                entities.AddRange(World.GetAllProps());

                foreach (var ent in entities)
                {
                    if (_pulledEntities.Count >= MaxEntityCount) break;

                    if (_pulledEntities.ContainsKey(ent.Handle) ||
                        ent.Position.DistanceTo2D(_position) > maxDistanceDelta ||
                        ent.HeightAboveGround > 300.0f) continue;

                    if (ent is Ped && !(ent as Ped).IsRagdoll && ent.HeightAboveGround > 2.0f)
                    {
                        Function.Call(Hash.SET_PED_TO_RAGDOLL, ent.Handle, 800, 1500, 2, 1, 1, 0);
                    }

                    AddEntity(ent, 3.0f * Probability.GetScalar(), 3.0f * Probability.GetScalar());
                }

                _nextUpdateTime = gameTime + 200;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdatePulledEntities(int gameTime, float maxDistanceDelta)
        {
            float verticalForce = ScriptThread.GetVar<float>("vortexVerticalPullForce");
            float horizontalForce = ScriptThread.GetVar<float>("vortexHorizontalPullForce");
            float topSpeed = ScriptThread.GetVar<float>("vortexTopEntitySpeed");

            // Clear existing pending removals
            while (_pendingRemovalEntities.TryTake(out _)) { }

            foreach (var e in _pulledEntities)
            {
                var entity = e.Value.Entity;

                var dist = Vector2.Distance(entity.Position.Vec2(), _position.Vec2());

                if (dist > maxDistanceDelta - 13f || entity.HeightAboveGround > 300.0f)
                {
                    ReleaseEntity(e.Key);
                    continue;
                }

                var targetPos = new Vector3(_position.X + e.Value.XBias, _position.Y + e.Value.YBias, entity.Position.Z);

                var direction = Vector3.Normalize(targetPos - entity.Position);

                var forceBias = Probability.NextFloat();

                var force = ForceScale * (forceBias + forceBias / dist);

                if (e.Value.IsPlayer)
                {
                    verticalForce *= 1.62f;

                    horizontalForce *= 1.2f;

                    //  horizontalForce *= 1.5f;

                    if (gameTime - _lastPlayerShapeTestTime > 1000)
                    {
                        var start = entity.Position;
                        var end = targetPos;
                        var rayHandle = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                            start.X, start.Y, start.Z,
                            end.X, end.Y, end.Z,
                            (int)(IntersectFlags.Map | IntersectFlags.Objects | IntersectFlags.Vehicles | IntersectFlags.Peds),
                            null,
                            0);

                        var hitArg = new OutputArgument();
                        var endCoordsArg = new OutputArgument();
                        var surfaceNormalArg = new OutputArgument();
                        var entityHandleArg = new OutputArgument();

                        Function.Call(Hash.GET_SHAPE_TEST_RESULT, rayHandle, hitArg, endCoordsArg, surfaceNormalArg, entityHandleArg);

                        bool hit = hitArg.GetResult<bool>();
                        Vector3 endCoords = endCoordsArg.GetResult<Vector3>();
                        Vector3 surfaceNormal = surfaceNormalArg.GetResult<Vector3>();
                        Entity hitEntity = Entity.FromHandle(entityHandleArg.GetResult<int>());

                        _lastRaycastResultFailed = hit;
                        _lastPlayerShapeTestTime = gameTime;
                    }

                    if (_lastRaycastResultFailed)
                        continue;
                }

                if (entity.Model.IsPlane)
                {
                    force *= 6.0f;
                    verticalForce *= 6.0f;
                }

                // apply a directional force pulling them into the tornado...
                entity.ApplyForce(direction * horizontalForce,
                    new Vector3(Probability.NextFloat(), 0, Probability.GetScalar()));

                var upDir = Vector3.Normalize(new Vector3(_position.X, _position.Y, _position.Z + 1000.0f) -
                                              entity.Position);
                // apply vertical forces
                entity.ApplyForceToCenterOfMass(upDir * verticalForce);

                var cross = Vector3.Cross(direction, Vector3.WorldUp);

                // move them along side the vortex.
                entity.ApplyForceToCenterOfMass(Vector3.Normalize(cross) * force *
                                                horizontalForce);

                Function.Call(Hash.SET_ENTITY_MAX_SPEED, entity.Handle, topSpeed);
            }

            foreach (var e in _pendingRemovalEntities)
            {
                _pulledEntities.TryRemove(e, out _);
            }
        }

        private static void ApplyDirectionalForce(Entity entity, Vector3 origin, Vector3 direction, float scale)
        {
            try
            {
                // Validate inputs
                if (entity == null || !entity.Exists())
                {
                    Logger.Error("Invalid entity in ApplyDirectionalForce");
                    return;
                }

                if (origin == null || origin == default(Vector3) ||
                    direction == null || direction == default(Vector3))
                {
                    Logger.Error("Invalid vectors in ApplyDirectionalForce");
                    return;
                }

                // Skip planes and high-altitude entities
                if (Function.Call<int>(Hash.GET_VEHICLE_CLASS, entity) == 16 ||
                    entity.HeightAboveGround > 15.0f)
                {
                    return;
                }

                float entityDist = Vector3.Distance(entity.Position, origin);
                if (entityDist <= 0)
                {
                    Logger.Error("Invalid entity distance in ApplyDirectionalForce");
                    return;
                }

                float zForce, scaleModifier;
                Vector3 rotationalForce;

                // Handle different entity types
                if (entity is Vehicle)
                {
                    zForce = Probability.GetBoolean(0.50f) ? 0.0332f : 0.0318f;
                    scaleModifier = 22.0f;
                    rotationalForce = new Vector3(0.0f, 0.1f, 0.40f);
                }
                else if (entity is Ped ped)
                {
                    if (!ped.IsRagdoll)
                    {
                        try
                        {
                            Function.Call(Hash.SET_PED_TO_RAGDOLL, entity.Handle, 800, 1500, 2, 1, 1, 0);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to set ped to ragdoll: {ex.Message}");
                        }
                    }
                    zForce = 0.0034f;
                    scaleModifier = 30.0f;
                    rotationalForce = new Vector3(0.0f, 0.0f, 0.12f);
                }
                else
                {
                    zForce = 0.000f;
                    scaleModifier = 30.0f;
                    rotationalForce = new Vector3(0.0f, 0.338f, 0.0f);
                }

                // Calculate and apply force
                var forceScale = Math.Min(1.0f, scaleModifier / entityDist) * scale;
                var force = (direction + new Vector3(0, 0, zForce)) * forceScale;

                try
                {
                    entity.ApplyForce(force, rotationalForce, ForceType.InternalImpulse);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to apply force to entity: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in ApplyDirectionalForce: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateCrosswinds(int _gameTime)
        {
            try
            {
                if (_position == null || _position == default(Vector3))
                {
                    Logger.Error("Invalid position in UpdateCrosswinds");
                    return;
                }

                var forwardLeft = _position + Vector3.WorldNorth * 100.0f;
                var rearLeft = _position - Vector3.WorldNorth * 100.0f;
                var direction = Vector3.Normalize(rearLeft - forwardLeft);

                Entity target;
                if (DoEntityCapsuleTest(forwardLeft, rearLeft, 22.0f, null, out target) &&
                    target != null && target.Exists())
                {
                    ApplyDirectionalForce(target, forwardLeft, direction, 4.0f);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in UpdateCrosswinds: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private bool DoEntityCapsuleTest(Vector3 start, Vector3 target, float radius, Entity ignore, out Entity hitEntity)
        {
            var shapeTest = ShapeTest.StartTestCapsule(
                start,
                target,
                radius,
                IntersectFlags.Everything,
                ignore
            );

            var hitArg = new OutputArgument();
            var endCoordsArg = new OutputArgument();
            var surfaceNormalArg = new OutputArgument();
            var entityHandleArg = new OutputArgument();

            Function.Call(Hash.GET_SHAPE_TEST_RESULT, shapeTest, hitArg, endCoordsArg, surfaceNormalArg, entityHandleArg);

            hitEntity = Entity.FromHandle(entityHandleArg.GetResult<int>());
            return hitArg.GetResult<bool>();
        }

        private void UpdateSurfaceDetection(int gameTime)
        {
            if (gameTime - lastParticleShapeTestTime > 1200)
            {
                var start = _position;
                var end = _position - Vector3.WorldUp * 100.0f;
                var rayHandle = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                    start.X, start.Y, start.Z,
                    end.X, end.Y, end.Z,
                    (int)(IntersectFlags.Map | IntersectFlags.Objects | IntersectFlags.Vehicles | IntersectFlags.Peds),
                    null,
                    0);

                var hitArg = new OutputArgument();
                var endCoordsArg = new OutputArgument();
                var surfaceNormalArg = new OutputArgument();
                var entityHandleArg = new OutputArgument();
                var materialHashArg = new OutputArgument();

                Function.Call(Hash.GET_SHAPE_TEST_RESULT_INCLUDING_MATERIAL, rayHandle, hitArg, endCoordsArg, surfaceNormalArg, materialHashArg, entityHandleArg);

                if (materialHashArg.GetResult<int>() != (int)LastMaterialTraversed)
                {
                    switch (LastMaterialTraversed)
                    {
                        case Materials.SandTrack:
                        case Materials.SandCompact:
                        case Materials.SandDryDeep:
                        case Materials.SandLoose:
                        case Materials.SandWet:
                        case Materials.SandWetDeep:
                            {
                                particleColorPrev = particleColor;
                                particleColorGoal = Color.NavajoWhite;
                                particleLerpTime = 0.0f;
                            }

                            break;
                        default:
                            particleColorPrev = particleColor;
                            particleColorGoal = Color.Black;
                            particleLerpTime = 0.0f;
                            break;
                    }

                    LastMaterialTraversed = (Materials)materialHashArg.GetResult<int>();
                }

                lastParticleShapeTestTime = gameTime;
            }

            if (particleLerpTime < 1.0f)
            {
                particleLerpTime += Game.LastFrameTime / DEFAULT_COLOR_LERP_DURATION;
                particleColor = particleColor.Lerp(particleColorGoal, particleLerpTime);
            }

            Function.Call(Hash.SET_PARTICLE_FX_LOOPED_COLOUR, "scr_rcbarry2", "scr_clown_appears", particleColor.R / 255.0f, particleColor.G / 255.0f, particleColor.B / 255.0f);
        }

        private void UpdateDebrisLayer(Materials _material)
        {
            if (Game.GameTime - _lastDebrisSpawnTime > 3000 + Probability.GetInteger(0, 5000))
            {
                //  UI.ShowSubtitle("spawn debris");

                new TDebris(this, _position, ScriptThread.GetVar<float>("vortexRadius"));
            }
        }
    }
}
