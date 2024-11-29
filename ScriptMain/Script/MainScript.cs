using GTA;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Commands;
using TornadoScript.ScriptMain.Config;
using TornadoScript.ScriptMain.Frontend;

namespace TornadoScript.ScriptMain.Script
{
    public sealed class MainScript : ScriptThread, IDisposable
    {
        private readonly TornadoFactory _factory;
        private bool _isInitialized;
        private bool _isDisposing;
        private bool _disposed;
        private const int ENTITY_POOL_SIZE = 50;
        private const float ENTITY_REUSE_DISTANCE = 20f;
        private List<Entity> entityPool = new List<Entity>();
        private bool vortexUseEntityPool = true;
        private KeyEventHandler _keyDownHandler;

        public MainScript()
        {
            try
            {
                Logger.Log("MainScript constructor starting...");

                // Register basic variables
                RegisterVars();
                Logger.Log("Variables registered");

                // Basic manager setup
                _factory = GetOrCreate<TornadoFactory>();
                GetOrCreate<CommandManager>();
                GetOrCreate<FrontendManager>();
                Logger.Log("Managers created");

                // Key handler
                _keyDownHandler = new KeyEventHandler(KeyPressed);
                KeyDown += _keyDownHandler;
                Logger.Log("Key handler registered");

                _isInitialized = true;
                Logger.Log("MainScript initialization complete");
            }
            catch (Exception ex)
            {
                LogError(ex, "Constructor");
                throw;
            }
        }

        void IDisposable.Dispose()
        {
            if (_disposed || _isDisposing) return;
            _isDisposing = true;

            try
            {
                Logger.Log("MainScript disposing...");

                // Clean up key handler
                if (_keyDownHandler != null)
                {
                    KeyDown -= _keyDownHandler;
                    _keyDownHandler = null;
                    Logger.Log("Key handler removed");
                }

                // Clean up entity pool
                if (entityPool != null)
                {
                    foreach (var entity in entityPool)
                    {
                        if (entity != null && entity.Exists())
                        {
                            entity.Delete();
                        }
                    }
                    entityPool.Clear();
                    Logger.Log("Entity pool cleaned");
                }

                // Clean up factory
                if (_factory != null)
                {
                    _factory.RemoveAll();
                    Logger.Log("Factory cleaned");
                }

                _isInitialized = false;
                Logger.Log("MainScript disposed successfully");
            }
            catch (Exception ex)
            {
                LogError(ex, "Dispose");
            }
            finally
            {
                base.Dispose();
                _disposed = true;
                _isDisposing = false;
            }
        }

        private void LogError(Exception ex, string context)
        {
            Logger.Error($"Error in {context}: {ex.Message}");
            if (ex.InnerException != null)
            {
                Logger.Error($"Inner Exception: {ex.InnerException.Message}");
            }
        }

        private void RegisterVars()
        {
            // Core settings
            RegisterVar("enablekeybinds", IniHelper.GetValue("Other", "EnableKeybinds", true));
            RegisterVar("enableconsole", IniHelper.GetValue("Other", "EnableConsole", true));
            RegisterVar("multiVortex", IniHelper.GetValue("Other", "MultiVortex", false));
            RegisterVar("vortexUseEntityPool", IniHelper.GetValue("VortexAdvanced", "UseInternalPool", true));

            // UI and Control Variables
            RegisterVar("toggleconsole", IniHelper.GetValue("Controls", "ToggleConsole", Keys.F8), true);
            RegisterVar("notifications", IniHelper.GetValue("Other", "Notifications", true));

            // Sound Variables
            RegisterVar("soundenabled", IniHelper.GetValue("Sound", "Enabled", true));
            RegisterVar("sirenenabled", IniHelper.GetValue("Sound", "SirenEnabled", true));

            // Vortex behavior
            RegisterVar("vortexRadius", IniHelper.GetValue("VortexCore", "Radius", 15.0f));
            RegisterVar("vortexParticleCount", IniHelper.GetValue("VortexCore", "ParticleCount", 10));
            RegisterVar("vortexMaxParticleLayers", IniHelper.GetValue("VortexCore", "MaxParticleLayers", 8));
            RegisterVar("vortexLayerSeperationScale", IniHelper.GetValue("VortexCore", "LayerSeparationScale", 12.0f));
            RegisterVar("vortexParticleAsset", IniHelper.GetValue("VortexCore", "ParticleAsset", "core"));
            RegisterVar("vortexParticleName", IniHelper.GetValue("VortexCore", "ParticleName", "eject_gas"));
            RegisterVar("vortexEnableCloudTopParticle", IniHelper.GetValue("VortexCore", "EnableCloudTop", true));
            RegisterVar("vortexEnableCloudTopParticleDebris", IniHelper.GetValue("VortexCore", "EnableCloudTopDebris", true));

            // Vortex Movement Variables
            RegisterVar("vortexMovementEnabled", IniHelper.GetValue("VortexMovement", "Enabled", true));
            RegisterVar("vortexMoveSpeedScale", IniHelper.GetValue("VortexMovement", "SpeedScale", 1.0f));
            RegisterVar("vortexRotationSpeed", IniHelper.GetValue("VortexMovement", "RotationSpeed", 1.0f));
            RegisterVar("vortexEnableSurfaceDetection", IniHelper.GetValue("VortexMovement", "EnableSurfaceDetection", true));

            // Entity Interaction Variables
            RegisterVar("vortexMaxEntityDist", IniHelper.GetValue("VortexEntities", "MaxEntityDistance", 100.0f));
            RegisterVar("vortexHorizontalPullForce", IniHelper.GetValue("VortexForces", "HorizontalPull", 15.0f));
            RegisterVar("vortexVerticalPullForce", IniHelper.GetValue("VortexForces", "VerticalPull", 12.0f));
            RegisterVar("vortexTopEntitySpeed", IniHelper.GetValue("VortexForces", "TopEntitySpeed", 30.0f));
            RegisterVar("vortexForceScale", IniHelper.GetValue("VortexForces", "ForceScale", 5.0f));
        }

        private Entity GetPooledEntity()
        {
            if (!vortexUseEntityPool || entityPool.Count >= ENTITY_POOL_SIZE)
                return null;

            // Try to reuse an existing entity that's far enough away
            foreach (Entity entity in entityPool)
            {
                if (entity != null && entity.Position.DistanceTo(Game.Player.Character.Position) > ENTITY_REUSE_DISTANCE)
                {
                    entityPool.Remove(entity);
                    return entity;
                }
            }

            return null;
        }

        private void AddToEntityPool(Entity entity)
        {
            if (!vortexUseEntityPool || entityPool.Count >= ENTITY_POOL_SIZE)
                return;

            if (entity != null && !entityPool.Contains(entity))
            {
                entityPool.Add(entity);
                Console.WriteLine($"Added entity to pool. Pool size: {entityPool.Count}/{ENTITY_POOL_SIZE}");
            }
        }

        private void CleanupEntityPool()
        {
            if (entityPool == null)
                return;

            foreach (Entity entity in entityPool)
            {
                if (entity != null)
                {
                    entity.Delete();
                }
            }
            entityPool.Clear();
        }

        private async void KeyPressed(object sender, KeyEventArgs e)
        {
            try
            {
                if (!GetVar<bool>("enablekeybinds"))
                {
                    Logger.Log("Keybinds are disabled");
                    return;
                }

                var frontendMgr = GetOrCreate<FrontendManager>();
                if (frontendMgr == null) return;

                // Handle console toggle
                if (e.KeyCode == Keys.F8 && GetVar<bool>("enableconsole"))
                {
                    Logger.Log("F8 key pressed, toggling console");
                    if (frontendMgr.IsConsoleShowing)
                        frontendMgr.HideConsole();
                    else
                        frontendMgr.ShowConsole();
                    return;
                }

                // Pass key events to console if it's showing
                if (frontendMgr.IsConsoleShowing)
                {
                    frontendMgr.HandleKeyPress(e);
                    e.Handled = true;  // Mark the event as handled to prevent it from reaching the game
                    return;
                }

                // Handle tornado toggle
                if (_factory != null && e.KeyCode == Keys.F6)
                {
                    Logger.Log("F6 key pressed, attempting to handle tornado");
                    if (_factory.ActiveVortexCount > 0 && !GetVar<bool>("multiVortex"))
                    {
                        Logger.Log("Removing existing tornado");
                        _factory.RemoveAll();
                    }
                    else
                    {
                        Logger.Log("Creating new tornado");
                        var position = Game.Player.Character.Position + Game.Player.Character.ForwardVector * 180f;
                        Logger.Log($"Spawn position: {position}");
                        var task = _factory.CreateVortex(position);
                        try
                        {
                            await task;
                            Logger.Log("Tornado created successfully");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to create tornado: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "KeyPressed");
            }
        }

        public override void OnUpdate(int gameTime)
        {
            try
            {
                if (_isInitialized && !_disposed)
                {
                    base.OnUpdate(gameTime);
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "OnUpdate");
            }
        }
    }
}
