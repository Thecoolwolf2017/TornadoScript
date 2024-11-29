using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptCore.Game;
using TornadoScript.ScriptMain.Utility;

namespace TornadoScript.ScriptMain.Script
{
    /// <summary>
    /// Extension to manage the spawning of tornadoes.
    /// </summary>
    public class TornadoFactory : ScriptExtension
    {
        private WavePlayer _tornadoWarningSiren;
        private WavePlayer _tornadoLowRumble;
        private const int VortexLimit = 30;
        private int _lastSpawnAttempt;
        public int ActiveVortexCount { get; private set; }
        private bool soundEnabled = true, sirenEnabled = true;
        private readonly TornadoVortex[] _activeVortexList = new TornadoVortex[VortexLimit];
        public TornadoVortex[] ActiveVortexList => _activeVortexList;

        /// <summary>
        /// Whether we are in the process of spawning a tornado.
        /// If this is true, the script will prepare to spawn a tornado based on the
        /// set parameters.
        /// </summary>
        private bool spawnInProgress = false;

        public TornadoFactory()
        {
            InitSounds();
        }

        private void InitSounds()
        {
            try
            {
                // Try to get sound enabled variable, default to false if not found
                var soundVar = ScriptThread.GetVar<bool>("soundenabled");
                soundEnabled = soundVar != null ? soundVar.Value : false;
                Logger.Log($"Sound enabled: {soundEnabled}");

                if (!soundEnabled)
                {
                    Logger.Log("Sound disabled, skipping sound initialization");
                    return;
                }

                // Try to get siren enabled variable, default to false if not found
                var sirenVar = ScriptThread.GetVar<bool>("sirenenabled");
                sirenEnabled = sirenVar != null ? sirenVar.Value : false;
                Logger.Log($"Siren enabled: {sirenEnabled}");

                try
                {
                    if (sirenEnabled)
                    {
                        Logger.Log("Loading siren sound...");
                        SoundLoad("tornado-weather-alert.wav", ref _tornadoWarningSiren, true);
                        if (_tornadoWarningSiren != null)
                        {
                            _tornadoWarningSiren.SetDistanceParameters(50.0f, 500.0f); // Siren should be heard from far away
                        }
                    }

                    Logger.Log("Loading rumble sound...");
                    SoundLoad("rumble-bass-2.wav", ref _tornadoLowRumble, true);
                    if (_tornadoLowRumble != null)
                    {
                        _tornadoLowRumble.SetDistanceParameters(10.0f, 200.0f); // Rumble is more localized
                    }

                    Logger.Log("Sound initialization complete");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error loading sound files: {ex.Message}");
                    // Disable sound on file load error
                    soundEnabled = false;
                    sirenEnabled = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in InitSounds: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Logger.Error($"Inner Exception: {ex.InnerException.Message}");
                }
                // Don't throw - just disable sound if there's an error
                soundEnabled = false;
                sirenEnabled = false;
            }
        }

        private void UpdateSounds(Vector3 tornadoPosition)
        {
            if (!soundEnabled) return;

            // Update siren position if active
            if (sirenEnabled && _tornadoWarningSiren != null && _tornadoWarningSiren.IsPlaying())
            {
                _tornadoWarningSiren.SetPosition(tornadoPosition);
            }

            // Update rumble position
            if (_tornadoLowRumble != null && _tornadoLowRumble.IsPlaying())
            {
                _tornadoLowRumble.SetPosition(tornadoPosition);
            }
        }

        /// <summary>
        /// Load a sound by name from the mod's installation directory
        /// </summary>
        /// <param name="soundName"></param>
        private void SoundLoad(string soundName, ref WavePlayer loadedSound, bool looping = false)
        {
            try
            {
                // Get potential sound file paths with access checks
                string modPath = null;
                string gtaPath = null;
                string scriptPath = null;

                try
                {
                    modPath = AppDomain.CurrentDomain.BaseDirectory;
                    Logger.Log($"Mod directory: {modPath}");
                    if (!HasWriteAccess(modPath))
                    {
                        Logger.Error($"No write access to mod directory: {modPath}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error accessing mod directory: {ex.Message}");
                }

                try
                {
                    gtaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GTA V");
                    Logger.Log($"GTA V directory: {gtaPath}");
                    if (!HasWriteAccess(gtaPath))
                    {
                        Logger.Error($"No write access to GTA V directory: {gtaPath}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error accessing GTA V directory: {ex.Message}");
                }

                try
                {
                    scriptPath = Path.Combine(gtaPath, "scripts", "TornadoScript");
                    Logger.Log($"Script directory: {scriptPath}");
                    if (!HasWriteAccess(scriptPath))
                    {
                        Logger.Error($"No write access to script directory: {scriptPath}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error accessing script directory: {ex.Message}");
                }

                var possiblePaths = new List<string>();

                // Only add paths we have access to
                if (modPath != null) possiblePaths.Add(Path.Combine(modPath, "sounds", soundName));
                if (scriptPath != null) possiblePaths.Add(Path.Combine(scriptPath, "sounds", soundName));
                if (gtaPath != null) possiblePaths.Add(Path.Combine(gtaPath, "scripts", "sounds", soundName));

                string foundPath = null;
                foreach (var path in possiblePaths)
                {
                    try
                    {
                        Logger.Log($"Checking for sound file at: {path}");
                        if (File.Exists(path))
                        {
                            // Test if we can actually read the file
                            using (var fs = File.OpenRead(path))
                            {
                                foundPath = path;
                                Logger.Log($"Found and verified access to sound file at: {path}");
                                break;
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Logger.Error($"Access denied to path: {path}");
                    }
                    catch (IOException ex)
                    {
                        Logger.Error($"IO error checking path {path}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error checking path {path}: {ex.Message}");
                    }
                }

                if (foundPath == null)
                {
                    // Try to create sounds directory in the most accessible location
                    string soundsDir = null;
                    if (HasWriteAccess(scriptPath))
                    {
                        soundsDir = Path.Combine(scriptPath, "sounds");
                    }
                    else if (HasWriteAccess(gtaPath))
                    {
                        soundsDir = Path.Combine(gtaPath, "scripts", "sounds");
                    }
                    else if (HasWriteAccess(modPath))
                    {
                        soundsDir = Path.Combine(modPath, "sounds");
                    }

                    if (soundsDir != null)
                    {
                        try
                        {
                            if (!Directory.Exists(soundsDir))
                            {
                                Directory.CreateDirectory(soundsDir);
                                Logger.Log($"Created sounds directory at: {soundsDir}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to create sounds directory: {ex.Message}");
                        }
                    }
                    else
                    {
                        Logger.Error("Could not find a writable location for sounds directory");
                    }

                    Logger.Error($"Could not find or access sound file: {soundName}");
                    Logger.Error("Please place sound files in one of these locations with proper permissions:");
                    foreach (var path in possiblePaths)
                    {
                        Logger.Error($"- {Path.GetDirectoryName(path)}");
                    }
                    soundEnabled = false;
                    return;
                }

                try
                {
                    loadedSound = new WavePlayer(foundPath);
                    loadedSound.SetLoopAudio(looping);
                    Logger.Log($"Successfully loaded sound file: {foundPath}");
                }
                catch (UnauthorizedAccessException)
                {
                    Logger.Error($"Access denied when loading sound file: {foundPath}");
                    soundEnabled = false;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to load sound file: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Logger.Error($"Inner Exception: {ex.InnerException.Message}");
                    }
                    soundEnabled = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Critical error in SoundLoad for {soundName}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Logger.Error($"Inner Exception: {ex.InnerException.Message}");
                }
                soundEnabled = false;
            }
        }

        private bool HasWriteAccess(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;

                // First check if directory exists
                if (!Directory.Exists(path))
                {
                    return false;
                }

                // Try to create a temporary file
                string testFile = Path.Combine(path, "write_test.tmp");
                using (FileStream fs = File.Create(testFile))
                {
                    fs.Close();
                }
                File.Delete(testFile);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Create a vortex at the given position.
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public async Task<TornadoVortex> CreateVortex(Vector3 position)
        {
            if (spawnInProgress || ActiveVortexCount >= VortexLimit)
            {
                return null;
            }

            spawnInProgress = true;

            try
            {
                Logger.Log("Starting CreateVortex");
                // Ensure ScriptThread.Vars is initialized
                int maxAttempts = 10;
                int attempts = 0;
                while (ScriptThread.Vars == null && attempts < maxAttempts)
                {
                    Function.Call(Hash.WAIT, 100);
                    attempts++;
                }

                if (ScriptThread.Vars == null)
                {
                    Logger.Error("Failed to create vortex: ScriptThread.Vars is null after max attempts");
                    spawnInProgress = false;
                    return null;
                }

                Logger.Log("Shifting vortex list");
                for (var i = _activeVortexList.Length - 1; i > 0; i--)
                {
                    if (_activeVortexList[i] != null && _activeVortexList[i].DespawnRequested)
                    {
                        _activeVortexList[i] = null;
                        ActiveVortexCount--;
                    }
                    else
                    {
                        _activeVortexList[i] = _activeVortexList[i - 1];
                    }
                }

                float groundHeight = 0f;  // Initialize with default value
                bool groundFound = false;
                var checkPos = position;
                checkPos.Z = 1000f;  // Start from high up

                // Try to find ground by sampling at different heights
                for (int i = 0; i < 10; i++)
                {
                    if (World.GetGroundHeight(checkPos, out float tempHeight))
                    {
                        groundHeight = tempHeight;
                        groundFound = true;
                        break;
                    }
                    checkPos.Z -= 200f;
                }

                if (!groundFound)
                {
                    Logger.Error("Failed to get ground height after multiple attempts");
                    spawnInProgress = false;
                    return null;
                }

                position.Z = groundHeight + 2.0f; // Spawn slightly above ground
                Logger.Log($"Ground height: {groundHeight}, Final position: {position}");

                Logger.Log("Creating new TornadoVortex");
                var tVortex = new TornadoVortex(position, false);

                try
                {
                    Logger.Log("Adding vortex to script thread");
                    ScriptThread.Add(tVortex);

                    Logger.Log("Building vortex");
                    if (!await tVortex.Build())
                    {
                        Logger.Error("Failed to build vortex");
                        ScriptThread.Remove(tVortex);
                        spawnInProgress = false;
                        return null;
                    }

                    _activeVortexList[0] = tVortex;
                    ActiveVortexCount++;
                    Logger.Log($"Vortex created successfully. Active count: {ActiveVortexCount}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error building vortex: {ex.Message}");
                    if (tVortex != null)
                    {
                        ScriptThread.Remove(tVortex);
                    }
                    throw;
                }
                finally
                {
                    spawnInProgress = false;
                }

                return tVortex;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in CreateVortex: {ex.Message}\n{ex.StackTrace}");
                spawnInProgress = false;
                return null;
            }
        }

        public override void OnUpdate(int gameTime)
        {
            try
            {
                // Update existing vortexes
                for (int i = 0; i < _activeVortexList.Length; i++)
                {
                    if (_activeVortexList[i] != null)
                    {
                        if (_activeVortexList[i].DespawnRequested)
                        {
                            ScriptThread.Remove(_activeVortexList[i]);
                            _activeVortexList[i] = null;
                            ActiveVortexCount = Math.Max(0, ActiveVortexCount - 1);
                        }
                        else
                        {
                            UpdateSounds(_activeVortexList[i].Position);
                        }
                    }
                }

                // Spawn logic
                if (ActiveVortexCount < 1)
                {
                    bool spawnInStorm = false;
                    try
                    {
                        var spawnInStormVar = ScriptThread.GetVar<bool>("spawnInStorm");
                        spawnInStorm = spawnInStormVar != null ? spawnInStormVar.Value : false;
                    }
                    catch
                    {
                        // If variable doesn't exist, default to false
                        spawnInStorm = false;
                    }

                    if (World.Weather == Weather.ThunderStorm && spawnInStorm)
                    {
                        if (!spawnInProgress && (gameTime - _lastSpawnAttempt) > 1000)
                        {
                            if (Probability.GetBoolean(0.05f))
                            {
                                try
                                {
                                    var player = Game.Player.Character;
                                    if (player != null && player.Exists())
                                    {
                                        var position = player.Position + player.ForwardVector * 100f;
                                        var spawnPos = position.Around(150.0f).Around(175.0f);

                                        Task.Run(async () =>
                                        {
                                            var tVortex = await CreateVortex(spawnPos);
                                            if (tVortex != null)
                                            {
                                                UpdateSounds(tVortex.Position);
                                            }
                                        }).ContinueWith(t =>
                                        {
                                            if (t.Exception != null)
                                            {
                                                Logger.Error($"Error spawning tornado: {t.Exception.GetBaseException().Message}");
                                            }
                                        }, TaskScheduler.Current);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Error in spawn logic: {ex.Message}");
                                }
                            }
                            _lastSpawnAttempt = gameTime;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OnUpdate: {ex.Message}");
            }

            base.OnUpdate(gameTime);
        }

        public void RemoveAll()
        {
            spawnInProgress = false;

            if (_tornadoWarningSiren != null && _tornadoWarningSiren.IsPlaying())
                _tornadoWarningSiren.DoFadeOut(3000, 0.0f);

            if (_tornadoLowRumble != null && _tornadoLowRumble.IsPlaying())
                _tornadoLowRumble.DoFadeOut(3000, 0.0f);

            for (var i = 0; i < ActiveVortexCount; i++)
            {
                _activeVortexList[i].Dispose();

                _activeVortexList[i] = null;
            }

            ActiveVortexCount = 0;
        }

        public override void Dispose()
        {
            for (var i = 0; i < ActiveVortexCount; i++)
            {
                _activeVortexList[i].Dispose();
            }

            base.Dispose();
        }
    }
}
