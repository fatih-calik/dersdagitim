using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DersDagitim.Persistence
{
    public static class SettingsManager
    {
        // Redirect to ConfigManager (sabit.sqlite)
        
        public static void Set(string key, string value)
        {
            DersDagitim.Services.ConfigManager.Shared.SetSetting(key, value);
        }
        
        public static void SetBool(string key, bool value)
        {
            DersDagitim.Services.ConfigManager.Shared.SetBool(key, value);
        }

        public static string Get(string key, string defaultValue = "")
        {
             var val = DersDagitim.Services.ConfigManager.Shared.GetSetting(key);
             return val ?? defaultValue;
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            return DersDagitim.Services.ConfigManager.Shared.GetBool(key, defaultValue);
        }
    }
}
