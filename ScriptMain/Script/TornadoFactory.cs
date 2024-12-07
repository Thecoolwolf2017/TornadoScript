using GTA.Math;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Utility;

namespace TornadoScript.ScriptMain.Script
{
    public class TornadoFactory : ScriptExtension, ITornadoFactory
    {
        private const string DEFAULT_SOUND_DIR = "scripts\\TornadoScript\\sounds";
        private const string WARNING_SOUND = "warning.wav";
        private const string RUMBLE_SOUND = "rumble.wav";
        public async Task InitializeSoundsAsync()
        {
            try
            {
                string[] searchPaths = new[]
                {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DEFAULT_SOUND_DIR),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", "sounds"),
                Path.Combine("scripts", "sounds")
            };

                string soundPath = null;
                foreach (var path in searchPaths)
                {
                    if (!Directory.Exists(path))
                    {
                        try
                        {
                            Directory.CreateDirectory(path);
                            Logger.Log($"Created sound directory: {path}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to create sound directory {path}: {ex.Message}");
                            continue;
                        }
                    }

                    if (ValidateSoundFiles(path))
                    {
                        soundPath = path;
                        break;
                    }
                }

                if (soundPath == null)
                {
                    Logger.Warning("No valid sound directory found. Using fallback sounds.");
                    return;
                }

                await LoadSoundFiles(soundPath);
            }
            catch (Exception ex)
            {
                Logger.Error($"Sound initialization failed: {ex.Message}");
            }
        }

        private bool ValidateSoundFiles(string path)
        {
            return File.Exists(Path.Combine(path, WARNING_SOUND)) &&
                   File.Exists(Path.Combine(path, RUMBLE_SOUND));
        }

        private async Task LoadSoundFiles(string soundPath)
        {
            try
            {
                await Task.Run(() =>
                {
                    string warningPath = Path.Combine(soundPath, WARNING_SOUND);
                    string rumblePath = Path.Combine(soundPath, RUMBLE_SOUND);

                    _tornadoWarningSiren?.Dispose();
                    _tornadoLowRumble?.Dispose();

                    _tornadoWarningSiren = new WavePlayer(warningPath);
                    _tornadoLowRumble = new WavePlayer(rumblePath);

                    Logger.Log("Sound files loaded successfully");
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load sound files: {ex.Message}");
                throw; // Re-throw to handle in the calling method
            }
        }

        private WavePlayer _tornadoWarningSiren;
        private WavePlayer _tornadoLowRumble;
        private const int VortexLimit = 30;
        private readonly object _tornadoLock;
        private readonly Dictionary<int, TornadoVortex> _activeTornados;
        private int _nextTornadoId;
        private bool _isDisposed;

        public int ActiveVortexCount => _activeTornados.Count;
        public IReadOnlyList<TornadoVortex> ActiveVortexList
        {
            get
            {
                lock (_tornadoLock)
                {
                    return new List<TornadoVortex>(_activeTornados.Values);
                }
            }
        }

        public TornadoFactory()
        {
            _tornadoLock = new object();
            _activeTornados = new Dictionary<int, TornadoVortex>();
            _nextTornadoId = 1;

            try
            {
                Logger.Log("Starting TornadoFactory initialization...");

                Logger.Log("Base initialization complete, attempting to load sounds...");

                // Try multiple possible paths for sounds
                string[] possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", "TornadoScript", "sounds"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", "sounds"),
                    Path.Combine("scripts", "TornadoScript", "sounds")
                };

                Logger.Log($"Base directory: {AppDomain.CurrentDomain.BaseDirectory}");
                foreach (var path in possiblePaths)
                {
                    Logger.Log($"Checking sound path: {path}");
                }

                string soundPath = null;
                foreach (var path in possiblePaths)
                {
                    if (Directory.Exists(path))
                    {
                        soundPath = path;
                        Logger.Log($"Found valid sound directory: {path}");
                        break;
                    }
                }

                if (soundPath != null)
                {
                    string warningPath = Path.Combine(soundPath, "warning.wav");
                    string rumblePath = Path.Combine(soundPath, "rumble.wav");

                    Logger.Log($"Checking warning sound at: {warningPath}");
                    Logger.Log($"Checking rumble sound at: {rumblePath}");

                    if (File.Exists(warningPath))
                    {
                        _tornadoWarningSiren?.Dispose();
                        _tornadoWarningSiren = new WavePlayer(warningPath);
                        Logger.Log("Warning sound loaded successfully");
                    }
                    else
                    {
                        Logger.Warning($"Warning sound file not found at: {warningPath}");
                    }

                    if (File.Exists(rumblePath))
                    {
                        _tornadoLowRumble?.Dispose();
                        _tornadoLowRumble = new WavePlayer(rumblePath);
                        Logger.Log("Rumble sound loaded successfully");
                    }
                    else
                    {
                        Logger.Warning($"Rumble sound file not found at: {rumblePath}");
                    }
                }
                else
                {
                    Logger.Warning("No valid sound directory found. Sounds will be disabled.");
                }

                Logger.Log("TornadoFactory initialization completed successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize TornadoFactory: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                throw; // Rethrow to let MainScript handle it
            }
        }

        public void SpawnTornado(Vector3 position, bool neverDespawn = false)
        {
            ThrowIfDisposed();

            if (_activeTornados == null)
            {
                Logger.Error("Active tornados collection is null!");
                return;
            }

            lock (_tornadoLock)
            {
                if (_activeTornados.Count >= VortexLimit)
                {
                    Logger.Warning($"Cannot spawn more tornados: Limit of {VortexLimit} reached");
                    return;
                }

                try
                {
                    Logger.Log($"SpawnTornado: Attempting to spawn tornado at position: {position}");
                    var tornado = new TornadoVortex(position, neverDespawn);

                    if (tornado == null)
                    {
                        Logger.Error("SpawnTornado: Failed to create TornadoVortex instance");
                        return;
                    }

                    // Add to ScriptThread first
                    Logger.Log("SpawnTornado: Adding tornado to ScriptThread");
                    ScriptThread.Add(tornado);
                    Logger.Log("SpawnTornado: Added tornado to ScriptThread successfully");

                    Logger.Log("SpawnTornado: Adding tornado to active tornados collection");
                    _activeTornados[_nextTornadoId++] = tornado;
                    Logger.Log($"SpawnTornado: Tornado spawned successfully with ID: {_nextTornadoId - 1}");

                    // Play warning sound if available
                    try
                    {
                        Logger.Log("SpawnTornado: Attempting to play warning siren");
                        _tornadoWarningSiren?.Play();
                        Logger.Log("SpawnTornado: Warning siren played successfully");

                        Logger.Log("SpawnTornado: Attempting to play rumble sound");
                        _tornadoLowRumble?.Play(true);
                        Logger.Log("SpawnTornado: Rumble sound started successfully");
                    }
                    catch (Exception soundEx)
                    {
                        Logger.Error($"SpawnTornado: Error playing sounds: {soundEx.Message}");
                        // Continue even if sounds fail
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"SpawnTornado: Failed to spawn tornado: {ex.Message}");
                    Logger.Error($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        public TornadoVortex CreateVortex(Vector3 position, bool neverDespawn = false)
        {
            ThrowIfDisposed();

            if (_activeTornados == null)
            {
                Logger.Error("Active tornados collection is null!");
                return null;
            }

            if (_activeTornados.Count >= VortexLimit)
            {
                Logger.Warning($"Cannot create more vortexes: Limit of {VortexLimit} reached");
                return null;
            }

            try
            {
                Logger.Log($"Attempting to create vortex at position: {position}");
                var tornado = new TornadoVortex(position, neverDespawn);

                if (tornado == null)
                {
                    Logger.Error("Failed to create TornadoVortex instance");
                    return null;
                }

                lock (_tornadoLock)
                {
                    _activeTornados[_nextTornadoId++] = tornado;
                }

                // Build the tornado visuals
                if (!tornado.Build())
                {
                    Logger.Error("Failed to build tornado visuals");
                    return null;
                }

                Logger.Log($"Vortex created successfully with ID: {_nextTornadoId - 1}");
                return tornado;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to create vortex: {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        public object GetActiveTornado()
        {
            ThrowIfDisposed();

            if (_activeTornados == null)
            {
                Logger.Error("Active tornados collection is null!");
                return null;
            }

            lock (_tornadoLock)
            {
                foreach (var tornado in _activeTornados.Values)
                {
                    return tornado; // Return the first active tornado
                }
                return null;
            }
        }

        public void ClearActiveTornados()
        {
            ThrowIfDisposed();

            if (_activeTornados == null)
            {
                Logger.Error("Active tornados collection is null!");
                return;
            }

            lock (_tornadoLock)
            {
                _activeTornados.Clear();
                Logger.Log("Cleared active tornados collection");
            }
        }

        public override void OnUpdate(int gameTime)
        {
            if (_isDisposed) return;

            if (_activeTornados == null)
            {
                Logger.Error("Active tornados collection is null!");
                return;
            }

            lock (_tornadoLock)
            {
                var toRemove = new List<int>();

                foreach (var pair in _activeTornados)
                {
                    try
                    {
                        pair.Value.OnUpdate(gameTime);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error updating tornado {pair.Key}: {ex.Message}");
                        Logger.Error($"Stack trace: {ex.StackTrace}");
                        toRemove.Add(pair.Key);
                    }
                }

                // Remove any failed tornados
                foreach (var id in toRemove)
                {
                    if (_activeTornados.TryGetValue(id, out var tornado))
                    {
                        try
                        {
                            tornado.Dispose();
                        }
                        catch (Exception disposeEx)
                        {
                            Logger.Error($"Error disposing tornado: {disposeEx.Message}");
                            Logger.Error($"Stack trace: {disposeEx.StackTrace}");
                        }
                        _activeTornados.Remove(id);
                    }
                }
            }
        }

        public override void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_activeTornados == null)
            {
                Logger.Error("Active tornados collection is null!");
                return;
            }

            try
            {
                lock (_tornadoLock)
                {
                    foreach (var tornado in _activeTornados.Values)
                    {
                        try
                        {
                            tornado.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error disposing tornado: {ex.Message}");
                            Logger.Error($"Stack trace: {ex.StackTrace}");
                        }
                    }
                    _activeTornados.Clear();
                }

                // Stop and clean up sound players
                _tornadoWarningSiren?.Stop();
                _tornadoWarningSiren = null;
                _tornadoLowRumble?.Stop();
                _tornadoLowRumble = null;
            }
            finally
            {
                base.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(TornadoFactory));
            }
        }
    }
}
