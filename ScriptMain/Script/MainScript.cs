using System;
using System.Windows.Forms;
using GTA;
using GTA.Native;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Commands;
using TornadoScript.ScriptMain.Config;
using TornadoScript.ScriptMain.Frontend;
using TornadoScript.ScriptMain.Memory;
using TornadoScript.ScriptMain.Utility;
using System.IO;
using System.Threading;

namespace TornadoScript.ScriptMain.Script
{
    public sealed class MainScript : ScriptThread, IDisposable
    {
        private readonly TornadoFactory _factory;
        private bool isInitialized;

        public MainScript()
        {
            try
            {
                // Basic setup - just logging and variables
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TornadoScript");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "tornado.log");
                File.AppendAllText(logPath, $"\n=== TornadoScript Started at {DateTime.Now} ===\n");

                Logger.Log("MainScript initialization starting...");
                
                // Register basic variables
                RegisterVars();
                Logger.Log("Variables registered");

                // Basic manager setup
                _factory = GetOrCreate<TornadoFactory>();
                GetOrCreate<CommandManager>();
                GetOrCreate<FrontendManager>();
                Logger.Log("Managers created");

                // Key handler
                KeyDown += KeyPressed;
                Logger.Log("Key handler registered");

                isInitialized = true;
                Logger.Log("MainScript initialization complete");
            }
            catch (Exception ex)
            {
                try
                {
                    string errorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TornadoScript", "error.log");
                    File.AppendAllText(errorPath, 
                        $"\n=== CRITICAL ERROR at {DateTime.Now} ===\n" +
                        $"Error: {ex.Message}\n" +
                        $"Stack Trace: {ex.StackTrace}\n");
                }
                catch { }
                throw;
            }
        }

        private void RegisterVars()
        {
            // UI and Control Variables
            RegisterVar("toggleconsole", Keys.F8, true);
            RegisterVar("enableconsole", IniHelper.GetValue("Other", "EnableConsole", true));
            RegisterVar("enablekeybinds", IniHelper.GetValue("Other", "EnableKeybinds", true));
            RegisterVar("multiVortex", IniHelper.GetValue("Other", "MultiVortex", false));
            RegisterVar("notifications", IniHelper.GetValue("Other", "Notifications", true));
            
            // Sound Variables
            RegisterVar("soundenabled", IniHelper.GetValue("Sound", "Enabled", true));
            RegisterVar("sirenenabled", IniHelper.GetValue("Sound", "SirenEnabled", true));
            
            // Vortex Core Variables
            RegisterVar("vortexUseEntityPool", IniHelper.GetValue("VortexAdvanced", "UseInternalPool", true));
            RegisterVar("vortexParticleMod", IniHelper.GetValue("Other", "ParticleMod", true));
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
            
            // Movement and Physics
            RegisterVar("vortexForceScale", IniHelper.GetValue("VortexForces", "ForceScale", 5.0f));
            RegisterVar("vortexRotationSpeed", IniHelper.GetValue("VortexMovement", "RotationSpeed", 2.5f));
            RegisterVar("vortexMoveSpeedScale", IniHelper.GetValue("VortexMovement", "SpeedScale", 1.0f));
            RegisterVar("vortexMovementEnabled", IniHelper.GetValue("VortexMovement", "Enabled", true));
            RegisterVar("vortexEnableSurfaceDetection", IniHelper.GetValue("VortexMovement", "EnableSurfaceDetection", true));
        }

        private void KeyPressed(object sender, KeyEventArgs e)
        {
            try
            {
                if (!GetVar<bool>("enablekeybinds"))
                {
                    Logger.Log("Keybinds are disabled");
                    return;
                }

                if (e.KeyCode == Keys.F8 && GetVar<bool>("enableconsole"))
                {
                    Logger.Log("F8 key pressed, toggling console");
                    GetVar<bool>("toggleconsole");
                }

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
                        _factory.CreateVortex(position);
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TornadoScript");
                    Directory.CreateDirectory(logDir);
                    string errorPath = Path.Combine(logDir, "error.log");
                    File.AppendAllText(errorPath, 
                        $"\n=== KEYPRESS ERROR at {DateTime.Now} ===\n" +
                        $"Error: {ex.Message}\n" +
                        $"Stack Trace: {ex.StackTrace}\n");
                    Logger.Log($"Error in KeyPressed: {ex.Message}");
                }
                catch { }
            }
        }

        public override void OnUpdate(int gameTime)
        {
            try
            {
                if (isInitialized)
                {
                    base.OnUpdate(gameTime);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error in OnUpdate: {0}", ex.Message);
            }
        }

        void IDisposable.Dispose()
        {
            try
            {
                if (_factory != null)
                {
                    _factory.RemoveAll();
                }
                KeyDown -= KeyPressed;
            }
            catch (Exception ex)
            {
                Logger.Log("Error during disposal: {0}", ex.Message);
            }
            finally
            {
                base.Dispose();
            }
        }
    }
}
