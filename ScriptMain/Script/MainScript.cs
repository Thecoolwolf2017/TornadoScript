using GTA;
using GTA.Math;
using GTA.UI;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Commands;
using TornadoScript.ScriptMain.Frontend;

namespace TornadoScript.ScriptMain.Script
{
    public interface ITornadoFactory : IDisposable
    {
        void OnUpdate(int gameTime);
        void SpawnTornado(Vector3 position, bool neverDespawn = false);
        object GetActiveTornado();
        void ClearActiveTornados();
    }


    public class MainScript : ScriptThread, IDisposable
    {
        private static MainScript _instance;
        private readonly ITornadoFactory _factory;
        private readonly CommandManager _commandManager;
        private bool _isInitialized;
        private bool _isDisposing;
        private readonly Dictionary<string, object> _settings;
        private readonly object _initLock = new object();
        private bool _showingConsole = false;
        public MainScript() : base()
        {
            try
            {
                Logger.Log("Starting MainScript initialization...");

                if (_instance != null)
                {
                    Logger.Warning("MainScript instance already exists!");
                    return;
                }

                _instance = this;
                Logger.Log("Instance set successfully");

                // Initialize and register FrontendManager first
                var frontendManager = new FrontendManager();
                Add(frontendManager); // Register with ScriptExtensionPool
                Logger.Log("FrontendManager created and registered successfully");

                // Create and register TornadoFactory
                _factory = new TornadoFactory();
                Add((ScriptExtension)_factory); // Register with ScriptThread
                Logger.Log("TornadoFactory created and registered successfully");

                _commandManager = new CommandManager();
                Logger.Log("CommandManager created successfully");

                _settings = new Dictionary<string, object>();
                Logger.Log("Settings dictionary initialized");

                InitializeManagers();
                Logger.Log("Managers initialized");

                InitializeComponents();
                Logger.Log("Components initialized");

                // Subscribe to events
                Tick += OnTick;
                KeyDown += OnKeyDown;
                Logger.Log("Events subscribed successfully");

                Logger.Log("MainScript constructor completed successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in MainScript constructor: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void InitializeManagers()
        {
            try
            {
                Logger.Log("Starting manager initialization...");

                // No need to create frontend manager here as it's already created in the constructor
                Logger.Log("Managers initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing managers: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                throw new InvalidOperationException("Failed to initialize critical components", ex);
            }
        }

        private void InitializeComponents()
        {
            try
            {
                Logger.Log("Starting component initialization...");

                // Initialize default settings
                _settings["enablekeybinds"] = true;
                _settings["enableconsole"] = true;

                Logger.Log("Components initialized successfully");

                // Set initialization flag
                _isInitialized = true;
                Logger.Log("Script initialization completed - Ready to process inputs");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error initializing components: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                throw new InvalidOperationException("Failed to initialize components", ex);
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_isDisposing || !_isInitialized) return;

            try
            {
                if (_factory == null)
                {
                    Initialize();
                    return;
                }

                _factory.OnUpdate(Game.GameTime);
            }
            catch (Exception ex)
            {
                LogError(ex, "OnTick");
                // Consider auto-recovery or safe shutdown
                if (ex is InvalidOperationException)
                {
                    _isDisposing = true;
                    Dispose();
                }
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (!_isInitialized || _isDisposing)
                {
                    return;
                }

                // Skip logging modifier keys
                if (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.ControlKey ||
                    e.KeyCode == Keys.Alt || e.KeyCode == Keys.Menu)
                {
                    return;
                }

                // Only log F6 and F8 key presses since those are our action keys
                if (e.KeyCode == Keys.F6 || e.KeyCode == Keys.F8)
                {
                    Logger.Log($"OnKeyDown: Processing key {e.KeyCode}");
                }

                if (e.KeyCode == Keys.F6)
                {
                    Logger.Log("F6 pressed - Processing tornado spawn/despawn");
                    var tornado = _factory?.GetActiveTornado();
                    if (tornado != null)
                    {
                        Logger.Log("Active tornado found - Disposing");
                        // First remove from ScriptThread
                        ScriptThread.Remove((ScriptExtension)tornado);
                        // Then dispose
                        ((IDisposable)tornado).Dispose();
                        // Clear from active tornados
                        _factory.ClearActiveTornados();
                        Notification.PostTicker("~r~Tornado removed", true, false);
                    }
                    else
                    {
                        Logger.Log("No active tornado - Spawning new tornado");
                        var playerPos = Game.Player.Character.Position;
                        Logger.Log($"Player position: {playerPos}");

                        // Save console state
                        var frontendManager = ScriptThread.Get<FrontendManager>();
                        bool wasConsoleShowing = _showingConsole;

                        // Hide console temporarily if it's showing
                        if (wasConsoleShowing && frontendManager != null)
                        {
                            frontendManager.HideConsole();
                            _showingConsole = false;
                        }

                        // Spawn tornado
                        _factory?.SpawnTornado(playerPos);
                        Notification.PostTicker("~g~Tornado spawned", true, false);

                        // Restore console if it was showing
                        if (wasConsoleShowing && frontendManager != null)
                        {
                            frontendManager.ShowConsole();
                            _showingConsole = true;
                        }
                    }
                }
                else if (e.KeyCode == Keys.F8)
                {
                    // Get the frontend manager
                    var frontendManager = ScriptThread.Get<FrontendManager>();
                    if (frontendManager != null)
                    {
                        try
                        {
                            if (!_showingConsole)
                            {
                                Logger.Log("Opening console...");
                                frontendManager.ShowConsole();
                                _showingConsole = true;
                                Logger.Log("Console opened successfully");
                            }
                            else
                            {
                                Logger.Log("Closing console...");
                                frontendManager.HideConsole();
                                _showingConsole = false;
                                Logger.Log("Console closed successfully");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error toggling console: {ex.Message}");
                            Logger.Error($"Stack trace: {ex.StackTrace}");
                        }
                    }
                    else
                    {
                        Logger.Error("FrontendManager not found!");
                    }
                }
                else if (_showingConsole)
                {
                    // Forward key events to FrontendManager when console is open
                    var frontendManager = ScriptThread.Get<FrontendManager>();
                    if (frontendManager != null)
                    {
                        try
                        {
                            frontendManager.HandleKeyPress(e);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error handling key press: {ex.Message}");
                            Logger.Error($"Stack trace: {ex.StackTrace}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "OnKeyDown");
            }
        }

        public new void Dispose()
        {
            if (_isDisposing) return;

            lock (_initLock)
            {
                if (_isDisposing) return;
                _isDisposing = true;

                try
                {
                    // Unsubscribe from events first
                    Tick -= OnTick;
                    KeyDown -= OnKeyDown;

                    // Dispose managers in reverse order of creation
                    _commandManager?.Dispose();
                    _factory?.Dispose();

                    // Clear settings
                    _settings.Clear();

                    Logger.Log("MainScript disposed successfully");
                }
                catch (Exception ex)
                {
                    LogError(ex, "Dispose");
                }
                finally
                {
                    _instance = null;
                    base.Dispose();
                }
            }
        }

        private void Initialize()
        {
            try
            {
                _isInitialized = true;
                Notification.PostTicker("~g~TornadoScript initialized successfully", true, false);
            }
            catch (Exception ex)
            {
                LogError(ex, "Initialize");
                Notification.PostTicker("~r~Failed to initialize TornadoScript", true, false);
            }
        }

        private void LogError(Exception ex, string context)
        {
            var message = $"Error in {context}: {ex.Message}";
            Logger.Error(message);
            Notification.PostTicker($"~r~{message}", true, false);
        }
    }
}
