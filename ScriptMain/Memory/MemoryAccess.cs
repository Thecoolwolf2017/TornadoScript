using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace TornadoScript.ScriptMain.Memory
{
    /// <summary>
    /// Provides safe access to game memory and handles native function calls.
    /// </summary>
    public static unsafe class MemoryAccess
    {
        #region Native Function Delegates

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate IntPtr FwGetAssetIndexFn(IntPtr assetStore, out int index, StringBuilder name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int AddEntityToPoolFn(ulong address);

        private delegate IntPtr GetPooledPtfxAddressFn(int handle);

        #endregion

        #region Memory Addresses

        private static IntPtr _ptfxAssetStorePtr;
        private static IntPtr _scriptEntityPoolAddr;
        private static IntPtr _vehiclePoolAddr;
        private static IntPtr _pedPoolAddr;
        private static IntPtr _objectPoolAddr;

        #endregion

        #region Function Pointers

        private static FwGetAssetIndexFn _fwGetAssetIndex;
        internal static AddEntityToPoolFn AddEntityToPool { get; private set; }


        #endregion

        #region Constants

        private static readonly uint PtfxColourHash = StringHash.AtStringHash("ptxu_Colour", 0);
        private const int MaxPoolSize = 1024;

        #endregion

        #region State

        private static bool _initialized;
        private static readonly Dictionary<string, IntPtr> _ptfxRulePtrList = new Dictionary<string, IntPtr>();
        private static readonly object _lock = new object();

        #endregion

        /// <summary>
        /// Initializes the memory access system. Must be called before using any other functionality.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                try
                {
                    InitializePtfxAssetStore();
                    InitializeEntityPools();
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    GTA.UI.Notification.PostTicker($"MemoryAccess initialization failed: {ex.Message}", false, false);
                    throw;
                }
            }
        }

        private static void InitializePtfxAssetStore()
        {
            var pattern = new Pattern("\\x0F\\xBF\\x04\\x9F\\xB9", "xxxxx");
            var result = pattern.Get(0x19);

            if (result != IntPtr.Zero)
            {
                var rip = result.ToInt64() + 7;
                var value = Marshal.ReadInt32(IntPtr.Add(result, 3));
                _ptfxAssetStorePtr = new IntPtr(rip + value);
            }
            else
            {
                throw new InvalidOperationException("Failed to find PTFX asset store pattern");
            }

            pattern = new Pattern("\\x41\\x8B\\xDE\\x4C\\x63\\x00", "xxxxx?");
            result = pattern.Get();

            if (result != IntPtr.Zero)
            {
                var rip = result.ToInt64();
                var value = Marshal.ReadInt32(result - 4);
                _fwGetAssetIndex = Marshal.GetDelegateForFunctionPointer<FwGetAssetIndexFn>(new IntPtr(rip + value));
            }
            else
            {
                throw new InvalidOperationException("Failed to find FwGetAssetIndex pattern");
            }
        }

        private static void InitializeEntityPools()
        {
            // Entity Pool
            InitializePool("\\x4C\\x8B\\x0D\\x00\\x00\\x00\\x00\\x44\\x8B\\xC1\\x49\\x8B\\x41\\x08",
                "xxx????xxxxxxx", 7, addr => _scriptEntityPoolAddr = addr);

            // Vehicle Pool
            InitializePool("\\x48\\x8B\\x05\\x00\\x00\\x00\\x00\\xF3\\x0F\\x59\\xF6\\x48\\x8B\\x08",
                "xxx????xxxxxxx", 7, addr => _vehiclePoolAddr = addr);

            // Ped Pool
            InitializePool("\\x48\\x8B\\x05\\x00\\x00\\x00\\x00\\x41\\x0F\\xBF\\xC8\\x0F\\xBF\\x40\\x10",
                "xxx????xxxxxxxx", 7, addr => _pedPoolAddr = addr);

            // Object Pool
            InitializePool("\\x48\\x8B\\x05\\x00\\x00\\x00\\x00\\x8B\\x78\\x10\\x85\\xFF",
                "xxx????xxxxx", 7, addr => _objectPoolAddr = addr);

            // AddEntityToPool
            var pattern = new Pattern("\\x48\\xF7\\xF9\\x49\\x8B\\x48\\x08\\x48\\x63\\xD0\\xC1\\xE0\\x08\\x0F\\xB6\\x1C\\x11\\x03\\xD8",
                "xxxxxxxxxxxxxxxxxxx");
            var result = pattern.Get();

            if (result != IntPtr.Zero)
            {
                AddEntityToPool = Marshal.GetDelegateForFunctionPointer<AddEntityToPoolFn>(IntPtr.Subtract(result, 0x68));
            }
            else
            {
                throw new InvalidOperationException("Failed to find AddEntityToPool pattern");
            }
        }

        private static void InitializePool(string pattern, string mask, int offset, Action<IntPtr> setter)
        {
            var pat = new Pattern(pattern, mask);
            var result = pat.Get(offset);

            if (result != IntPtr.Zero)
            {
                var rip = result.ToInt64();
                var value = Marshal.ReadInt32(result - 4);
                setter(Marshal.ReadIntPtr(new IntPtr(rip + value)));
            }
            else
            {
                throw new InvalidOperationException($"Failed to find pattern: {pattern}");
            }
        }

        /// <summary>
        /// Gets all entities currently in the game world.
        /// </summary>
        public static IEnumerable<Entity> GetAllEntities()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("MemoryAccess not initialized");
            }

            var poolItems = Marshal.ReadIntPtr(_scriptEntityPoolAddr);
            var bitMap = Marshal.ReadIntPtr(_scriptEntityPoolAddr + 0x8);
            var count = Marshal.ReadInt32(_scriptEntityPoolAddr + 0x10);

            count = Math.Min(count, MaxPoolSize); // Safety limit

            var entities = new List<Entity>();
            for (int i = 0; i < count; i++)
            {
                var bitset = Marshal.ReadByte(bitMap + i);
                if ((bitset & 0x80) != 0) continue;

                var handle = (i << 8) + bitset;
                var type = Function.Call<int>(Hash.GET_ENTITY_TYPE, handle);

                Entity entity;
                switch (type)
                {
                    case 1:
                        entity = World.GetAllPeds().FirstOrDefault(p => p.Handle == handle);
                        break;
                    case 2:
                        entity = World.GetAllVehicles().FirstOrDefault(v => v.Handle == handle);
                        break;
                    case 3:
                        entity = World.GetAllProps().FirstOrDefault(p => p.Handle == handle);
                        break;
                    default:
                        entity = null;
                        break;
                }

                if (entity != null)
                {
                    entities.Add(entity);
                }
            }
            return entities;
        }

        /// <summary>
        /// Safely reads a value from memory.
        /// </summary>
        public static T ReadMemory<T>(IntPtr address, int offset = 0) where T : unmanaged
        {
            if (address == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(address));
            }

            try
            {
                return Marshal.PtrToStructure<T>(IntPtr.Add(address, offset));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read memory at {address + offset:X}", ex);
            }
        }

        /// <summary>
        /// Safely writes a value to memory.
        /// </summary>
        public static void WriteMemory<T>(IntPtr address, T value, int offset = 0) where T : unmanaged
        {
            if (address == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(address));
            }

            try
            {
                Marshal.StructureToPtr(value, IntPtr.Add(address, offset), false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to write memory at {address + offset:X}", ex);
            }
        }

        /// <summary>
        /// Checks if an address is valid for reading/writing.
        /// </summary>
        public static bool IsAddressValid(IntPtr address)
        {
            if (address == IntPtr.Zero) return false;

            try
            {
                Marshal.ReadByte(address);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #region IDisposable Pattern for Cleanup

        private static class Cleanup
        {
            public static void Release()
            {
                _ptfxRulePtrList.Clear();
                _initialized = false;

                // Release any other resources
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        #endregion
    }
}
