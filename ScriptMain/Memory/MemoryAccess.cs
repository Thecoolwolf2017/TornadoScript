using GTA;
using GTA.Native;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using TornadoScript.ScriptCore;

namespace TornadoScript.ScriptMain.Memory
{
    /// <summary>
    /// Provides safe access to game memory and handles native function calls.
    /// </summary>
    public static class MemoryAccess
    {
        private static IntPtr _scriptEntityPoolAddr;
        private static bool _isInitialized;

        static MemoryAccess()
        {
            try
            {
                InitializeEntityPools();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"MemoryAccess initialization failed: {ex.Message}");
                _isInitialized = false;
            }
        }

        public static bool IsEntityPoolAvailable()
        {
            return _isInitialized && _scriptEntityPoolAddr != IntPtr.Zero;
        }

        private static bool IsAddressValid(IntPtr address)
        {
            if (address == IntPtr.Zero) return false;
            try
            {
                return address.ToInt64() > 0 && address.ToInt64() < long.MaxValue;
            }
            catch
            {
                return false;
            }
        }

        private static void InitializeEntityPools()
        {
            try
            {
                Logger.Log("Starting entity pool initialization...");

                // Simplified pattern for latest GTA V
                var pattern = new Pattern("4C8B0D????????488B05????????4C8BC0", "xxxx????xxxx????xxxx");
                var result = pattern.Get(3);

                if (result != IntPtr.Zero && IsAddressValid(result))
                {
                    _scriptEntityPoolAddr = result;
                    Logger.Log($"Found entity pool at {result.ToInt64():X}");
                }
                else
                {
                    Logger.Log("Using fallback mode - no entity pool");
                    _scriptEntityPoolAddr = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Memory init failed: {ex.Message} - using fallback");
                _scriptEntityPoolAddr = IntPtr.Zero;
            }
        }

        public static bool GetEntityFromPool(int handle, out Entity entity)
        {
            entity = null;
            try
            {
                if (!IsEntityPoolAvailable() || handle <= 0)
                    return false;

                var addr = GetEntityAddress(handle);
                if (addr != IntPtr.Zero)
                {
                    // Get entity type and use proper World methods
                    var entityType = Function.Call<int>(Hash.GET_ENTITY_TYPE, handle);
                    switch (entityType)
                    {
                        case 1: // Ped
                            entity = World.GetAllPeds().FirstOrDefault(p => p.Handle == handle);
                            break;
                        case 2: // Vehicle
                            entity = World.GetAllVehicles().FirstOrDefault(v => v.Handle == handle);
                            break;
                        case 3: // Prop
                            entity = World.GetAllProps().FirstOrDefault(p => p.Handle == handle);
                            break;
                        default:
                            Logger.Log($"Unknown entity type {entityType} for handle {handle}");
                            return false;
                    }

                    return entity != null && entity.Exists();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"GetEntityFromPool failed: {ex.Message}");
            }
            return false;
        }

        private static IntPtr GetEntityAddress(int handle)
        {
            try
            {
                if (!IsEntityPoolAvailable() || handle <= 0)
                    return IntPtr.Zero;

                var addr = Marshal.ReadIntPtr(_scriptEntityPoolAddr);
                if (!IsAddressValid(addr))
                    return IntPtr.Zero;

                return addr;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public static void Initialize(GTA.Script script = null)
        {
            try
            {
                if (_isInitialized)
                    return;

                Logger.Log("Initializing memory access...");

                // Wait for game to be ready
                if (script != null)
                {
                    void OnTick(object sender, EventArgs args)
                    {
                        if (!_isInitialized && Game.Player?.Character != null && Game.Player.Character.Exists())
                        {
                            InitializeEntityPools();
                            _isInitialized = true;
                            script.Tick -= OnTick;
                            Logger.Log("Memory access initialized");
                        }
                    }
                    script.Tick += OnTick;
                }
                else
                {
                    InitializeEntityPools();
                    _isInitialized = true;
                    Logger.Log("Memory access initialized (sync)");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Memory access initialization failed: {ex.Message}");
                _isInitialized = false;
            }
        }

        public static void Shutdown()
        {
            _isInitialized = false;
            _scriptEntityPoolAddr = IntPtr.Zero;
            Logger.Log("Memory access shut down");
        }
    }
}
