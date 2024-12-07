using GTA.Native;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using TornadoScript.ScriptCore;
using TornadoScript.ScriptMain.Utility;

namespace TornadoScript.ScriptMain.Memory
{
    public sealed unsafe class Pattern
    {
        private readonly string _mask;
        private readonly byte[] _bytes;
        private const int CHUNK_SIZE = 0x100; // Smaller chunks for more thorough scanning
        private const int SCAN_YIELD_INTERVAL = 64;
        private const int SCAN_REGION_SIZE = 0x1000; // Smaller regions
        private const int MEMORY_ACCESS_TIMEOUT = 2000; // Longer timeout
        private const int MAX_PATTERN_LENGTH = 256;

        private static readonly ConcurrentDictionary<string, IntPtr> _patternCache = new ConcurrentDictionary<string, IntPtr>();

        public Pattern(string pattern, string mask)
        {
            if (!ValidatePattern(pattern, mask))
            {
                throw new ArgumentException("Invalid pattern or mask");
            }

            _mask = mask;
            _bytes = new byte[pattern.Length / 2];

            // Convert hex string to bytes
            for (int i = 0; i < pattern.Length; i += 2)
            {
                int high = GetHexVal(pattern[i]);
                int low = GetHexVal(pattern[i + 1]);
                _bytes[i / 2] = (byte)((high << 4) | low);
            }
        }

        public IntPtr Get(int offset = 0)
        {
            try
            {
                // Check cache first
                string cacheKey = $"{BitConverter.ToString(_bytes)}_{_mask}_{offset}";
                if (_patternCache.TryGetValue(cacheKey, out IntPtr cached) && IsAddressReadable(cached))
                {
                    Logger.Log($"Found pattern in cache at {cached.ToInt64():X}");
                    return cached;
                }

                var process = Win32Native.GetCurrentProcess();
                var module = Win32Native.GetModuleHandle(null);

                if (process == IntPtr.Zero || module == IntPtr.Zero)
                {
                    Logger.Error("Failed to get process or module handle");
                    return IntPtr.Zero;
                }

                if (!Win32Native.GetModuleInformation(process, module, out MODULEINFO moduleInfo, (uint)sizeof(MODULEINFO)))
                {
                    Logger.Error("Failed to get module information");
                    return IntPtr.Zero;
                }

                var baseAddress = moduleInfo.LpBaseOfDll.ToInt64();
                var size = moduleInfo.SizeOfImage;

                Logger.Log($"Scanning memory range: {baseAddress:X} - {baseAddress + size:X}");

                // First byte optimization
                byte firstByte = _bytes[0];
                bool firstByteWildcard = _mask[0] != 'x';

                // Scan in chunks with improved safety checks
                for (long currentAddress = baseAddress; currentAddress < baseAddress + size - _bytes.Length; currentAddress += CHUNK_SIZE)
                {
                    try
                    {
                        // Enhanced memory region validation
                        if (!IsAddressReadable(new IntPtr(currentAddress)) || !IsAddressInRange(new IntPtr(currentAddress), baseAddress, size))
                        {
                            currentAddress += SCAN_REGION_SIZE - (currentAddress % SCAN_REGION_SIZE);
                            continue;
                        }

                        // Add additional safety check
                        if (currentAddress + CHUNK_SIZE > baseAddress + size)
                        {
                            break;
                        }

                        byte* ptr = (byte*)currentAddress;

                        // Scan current chunk with improved bounds checking
                        for (int i = 0; i < CHUNK_SIZE && (currentAddress + i) < (baseAddress + size - _bytes.Length); i++)
                        {
                            if (!IsAddressReadable(new IntPtr(currentAddress + i)))
                            {
                                break;
                            }

                            // First byte check with safety
                            if (!firstByteWildcard)
                            {
                                try
                                {
                                    if (ptr[i] != firstByte)
                                        continue;
                                }
                                catch
                                {
                                    break;
                                }
                            }

                            // Full pattern check with additional validation
                            bool[] maskArray = _mask.Select(c => c == 'x').ToArray();
                            if (ByteCompare(ptr + i, _bytes, maskArray))
                            {
                                var found = new IntPtr(currentAddress + i + offset);

                                // Enhanced validation of found address
                                if (ValidateFoundPattern(found, baseAddress, size))
                                {
                                    _patternCache.TryAdd(cacheKey, found);
                                    return found;
                                }
                            }
                        }

                        // More frequent yields to prevent freezing
                        if (currentAddress % (SCAN_YIELD_INTERVAL * CHUNK_SIZE / 2) == 0)
                        {
                            Function.Call(Hash.WAIT, 0);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Error scanning at {currentAddress:X}: {ex.Message}");
                        currentAddress += SCAN_REGION_SIZE - (currentAddress % SCAN_REGION_SIZE);
                        continue;
                    }
                }

                Logger.Log("Pattern not found in memory");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Logger.Error($"Pattern scanning failed: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        private bool ValidateFoundPattern(IntPtr address, long baseAddress, long size)
        {
            try
            {
                if (!IsAddressInRange(address, baseAddress, size) || !IsAddressReadable(address))
                {
                    return false;
                }

                var value = Marshal.ReadInt64(address);
                return value != 0 && IsAddressReadable(new IntPtr(value));
            }
            catch
            {
                return false;
            }
        }

        public static List<IntPtr> FindPattern(byte[] pattern, string mask)
        {
            var results = new List<IntPtr>();
            var moduleBase = Process.GetCurrentProcess().MainModule.BaseAddress;
            var moduleSize = Process.GetCurrentProcess().MainModule.ModuleMemorySize;

            // Use smaller chunks to scan memory
            const int CHUNK_SIZE = 4096;
            byte[] buffer = new byte[CHUNK_SIZE];

            for (var i = 0L; i < moduleSize; i += CHUNK_SIZE)
            {
                try
                {
                    // Calculate current address
                    var currentBase = new IntPtr(moduleBase.ToInt64() + i);

                    // Enhanced address validation
                    if (!ValidateAddress(currentBase, moduleBase, moduleSize))
                        continue;

                    // Read memory in chunks
                    int bytesRead = 0;
                    try
                    {
                        Marshal.Copy(currentBase, buffer, 0, Math.Min(CHUNK_SIZE, (int)(moduleSize - i)));
                        bytesRead = Math.Min(CHUNK_SIZE, (int)(moduleSize - i));
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Memory read failed at {currentBase.ToInt64():X}: {ex.Message}");
                        continue;
                    }

                    // Scan the chunk
                    for (var j = 0; j < bytesRead - pattern.Length; j++)
                    {
                        bool found = true;
                        for (var k = 0; k < pattern.Length; k++)
                        {
                            if (mask[k] == '?')
                                continue;

                            if (buffer[j + k] != pattern[k])
                            {
                                found = false;
                                break;
                            }
                        }

                        if (found)
                        {
                            var resultAddress = new IntPtr(currentBase.ToInt64() + j);

                            // Additional validation for found address
                            if (ValidateFoundPattern(resultAddress, pattern, mask))
                            {
                                results.Add(resultAddress);
                                Logger.Log($"Found pattern at: {resultAddress.ToInt64():X}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error during pattern scan at {moduleBase.ToInt64() + i:X}: {ex.Message}");
                    continue;
                }
            }

            return results;
        }

        private static bool ValidateAddress(IntPtr address, IntPtr moduleBase, int moduleSize)
        {
            try
            {
                if (address == IntPtr.Zero) return false;

                // Basic range check
                var addressLong = address.ToInt64();
                var moduleEndLong = moduleBase.ToInt64() + moduleSize;

                if (addressLong < moduleBase.ToInt64() || addressLong >= moduleEndLong)
                    return false;

                if (addressLong < 0x10000 || addressLong > 0x7FFFFFFFFFFF)
                    return false;

                return IsAddressReadable(address);
            }
            catch
            {
                return false;
            }
        }

        private static bool ValidateFoundPattern(IntPtr address, byte[] pattern, string mask)
        {
            try
            {
                // Verify we can read the full pattern length
                byte[] verification = new byte[pattern.Length];
                Marshal.Copy(address, verification, 0, pattern.Length);

                // Verify pattern matches at the address
                for (int i = 0; i < pattern.Length; i++)
                {
                    if (mask[i] == '?') continue;
                    if (verification[i] != pattern[i]) return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Pattern validation failed at {address.ToInt64():X}: {ex.Message}");
                return false;
            }
        }

        private static bool CheckPattern(byte[] pattern, string mask, IntPtr address)
        {
            try
            {
                for (var i = 0; i < pattern.Length; i++)
                {
                    if (mask[i] == '?')
                        continue;

                    if (!IsAddressReadable(address + i))
                        return false;

                    if (Marshal.ReadByte(address + i) != pattern[i])
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ByteCompare(byte* address, byte[] pattern, bool[] mask)
        {
            try
            {
                if (address == null || pattern == null || mask == null || pattern.Length != mask.Length)
                {
                    return false;
                }

                // Compare bytes one at a time for better reliability
                for (int i = 0; i < pattern.Length; i++)
                {
                    try
                    {
                        byte currentByte = address[i];
                        if (!mask[i]) continue; // Skip wildcard bytes
                        if (currentByte != pattern[i]) return false;
                    }
                    catch
                    {
                        return false; // Memory access failed
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAddressInRange(IntPtr address, long baseAddress, long size)
        {
            var addr = address.ToInt64();
            return addr >= baseAddress && addr < (baseAddress + size);
        }

        private static bool IsAddressReadable(IntPtr address)
        {
            if (address == IntPtr.Zero) return false;

            try
            {
                if (Win32Native.VirtualQuery(address, out Win32Native.MEMORY_BASIC_INFORMATION mbi, new UIntPtr((uint)Marshal.SizeOf<Win32Native.MEMORY_BASIC_INFORMATION>())) == UIntPtr.Zero)
                    return false;

                return mbi.State == Win32Native.MEM_COMMIT &&
                       (mbi.Protect & Win32Native.PAGE_NOACCESS) == 0 &&
                       (mbi.Protect & Win32Native.PAGE_GUARD) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool ValidatePointer(IntPtr address, long baseAddress, long size)
        {
            var addr = address.ToInt64();
            return addr > baseAddress && addr < (baseAddress + size) && IsAddressReadable(address);
        }

        private static bool ValidatePattern(string pattern, string mask)
        {
            try
            {
                if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(mask))
                {
                    Logger.Error("Pattern or mask is null or empty");
                    return false;
                }

                // Check if pattern length is even (each byte is 2 hex chars)
                if (pattern.Length % 2 != 0)
                {
                    Logger.Error($"Pattern length must be even (got {pattern.Length})");
                    return false;
                }

                // Check if pattern and mask lengths match
                if (pattern.Length / 2 != mask.Length)
                {
                    Logger.Error($"Pattern length ({pattern.Length / 2} bytes) does not match mask length ({mask.Length})");
                    return false;
                }

                // Validate hex characters
                for (int i = 0; i < pattern.Length; i++)
                {
                    if (!Uri.IsHexDigit(pattern[i]))
                    {
                        Logger.Error($"Invalid hex character in pattern at position {i}: {pattern[i]}");
                        return false;
                    }
                }

                // Validate mask characters
                for (int i = 0; i < mask.Length; i++)
                {
                    if (mask[i] != 'x' && mask[i] != '?')
                    {
                        Logger.Error($"Invalid mask character at position {i}: {mask[i]}");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error validating pattern: {ex.Message}");
                return false;
            }
        }

        private static int GetHexVal(char hex)
        {
            hex = char.ToUpper(hex);
            return hex >= '0' && hex <= '9' ? hex - '0' :
                   hex >= 'A' && hex <= 'F' ? hex - 'A' + 10 :
                   throw new ArgumentException($"Invalid hex character: {hex}");
        }
    }
}
