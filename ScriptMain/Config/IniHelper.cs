﻿using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using TornadoScript.ScriptMain.Utility;

namespace TornadoScript.ScriptMain.Config
{
    public static class IniHelper
    {
        public static readonly string IniPath;
        public static readonly IniFile IniFile;

        static IniHelper()
        {
            IniPath = string.Format("scripts\\{0}.ini",
            Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location));
            if (!File.Exists(IniPath)) Create();
            IniFile = new IniFile(IniPath);
        }

        /// <summary>
        /// Write a string value to the config file at the specified section and key
        /// </summary>
        /// <param name="section">The section in the config file</param>
        /// <param name="key">The key of the config string</param>
        /// <param name="value">The value of the config string</param>
        public static void WriteValue(string section, string key, string value)
        {
            IniFile.IniWriteValue(section, key, value);
        }

        /// <summary>
        /// Gets a config setting
        /// </summary>
        /// <param name="section">The section of the config file</param>
        /// <param name="key">The config setting</param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        public static T GetValue<T>(string section, string key, T defaultValue = default(T))
        {
            Type type = typeof(T);
            if (!type.IsValueType && type != typeof(string))
                throw new ArgumentException("Not a known type.");

            var keyValue = IniFile.IniReadValue(section, key);
            var tConverter = TypeDescriptor.GetConverter(type);

            if (keyValue.Length > 0 && tConverter.CanConvertFrom(typeof(string)))
            {
                return (T)tConverter.ConvertFromString(null, CultureInfo.InvariantCulture, keyValue);
            }

            return defaultValue;
        }

        public static void Create()
        {
            try
            {
                if (File.Exists(IniPath)) File.Delete(IniPath);
                var list = Helpers.ReadEmbeddedResource(Properties.Resources.TornadoScript);
                Helpers.WriteListToFile(list, IniPath);
            }

            catch (AccessViolationException)
            {
                GTA.UI.Notification.PostTicker("TornadoScript failed to write a new INI file. Access to the path " + IniPath + " was denied", false, false);
            }
            catch (Exception e)
            {
                GTA.UI.Notification.PostTicker("TornadoScript failed to write a new INI file. " + e.Message, false, false);
            }
        }
    }
}
