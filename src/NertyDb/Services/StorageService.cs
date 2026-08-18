using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NertyDb.Models;

namespace NertyDb.Services
{
    public class UserSettings
    {
        public string Theme { get; set; } = "Light"; // "Light" or "Dark"
        public int DefaultPageSize { get; set; } = 100;
        public int QueryTimeoutSeconds { get; set; } = 30;
        public bool ConfirmBeforeCommit { get; set; } = true;
        public string LastSelectedConnectionId { get; set; } = string.Empty;
        public string LastSelectedDatabase { get; set; } = string.Empty;
    }

    public class StorageService
    {
        private static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string DataDir = Path.Combine(AppDir, "data");

        private static readonly string ConnectionsFile = Path.Combine(DataDir, "connections.json");
        private static readonly string HistoryFile = Path.Combine(DataDir, "history.json");
        private static readonly string SnippetsFile = Path.Combine(DataDir, "snippets.json");
        private static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public StorageService()
        {
            EnsureDirectories();
        }

        private void EnsureDirectories()
        {
            try
            {
                if (!Directory.Exists(DataDir))
                {
                    Directory.CreateDirectory(DataDir);
                }
            }
            catch { }
        }

        public List<ConnectionProfile> LoadConnections()
        {
            try
            {
                if (File.Exists(ConnectionsFile))
                {
                    var json = File.ReadAllText(ConnectionsFile);
                    var list = JsonSerializer.Deserialize<List<ConnectionProfile>>(json, JsonOpts);
                    return list ?? GetDefaultConnections();
                }
            }
            catch { }
            return GetDefaultConnections();
        }

        public void SaveConnections(List<ConnectionProfile> connections)
        {
            try
            {
                EnsureDirectories();
                var json = JsonSerializer.Serialize(connections, JsonOpts);
                File.WriteAllText(ConnectionsFile, json);
            }
            catch { }
        }

        public List<QueryHistoryItem> LoadHistory()
        {
            try
            {
                if (File.Exists(HistoryFile))
                {
                    var json = File.ReadAllText(HistoryFile);
                    return JsonSerializer.Deserialize<List<QueryHistoryItem>>(json, JsonOpts) ?? new();
                }
            }
            catch { }
            return new List<QueryHistoryItem>();
        }

        public void SaveHistory(List<QueryHistoryItem> history)
        {
            try
            {
                EnsureDirectories();
                // Keep last 300 items
                if (history.Count > 300)
                {
                    history = history.GetRange(history.Count - 300, 300);
                }
                var json = JsonSerializer.Serialize(history, JsonOpts);
                File.WriteAllText(HistoryFile, json);
            }
            catch { }
        }

        public List<SeniorTemplate> LoadCustomSnippets()
        {
            try
            {
                if (File.Exists(SnippetsFile))
                {
                    var json = File.ReadAllText(SnippetsFile);
                    return JsonSerializer.Deserialize<List<SeniorTemplate>>(json, JsonOpts) ?? new();
                }
            }
            catch { }
            return new List<SeniorTemplate>();
        }

        public void SaveCustomSnippets(List<SeniorTemplate> snippets)
        {
            try
            {
                EnsureDirectories();
                var json = JsonSerializer.Serialize(snippets, JsonOpts);
                File.WriteAllText(SnippetsFile, json);
            }
            catch { }
        }

        public UserSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    return JsonSerializer.Deserialize<UserSettings>(json, JsonOpts) ?? new();
                }
            }
            catch { }
            return new UserSettings();
        }

        public void SaveSettings(UserSettings settings)
        {
            try
            {
                EnsureDirectories();
                var json = JsonSerializer.Serialize(settings, JsonOpts);
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }

        private static List<ConnectionProfile> GetDefaultConnections()
        {
            return new List<ConnectionProfile>
            {
                new ConnectionProfile
                {
                    Name = "SQL Server Local (Senior)",
                    Server = "localhost",
                    Port = 1433,
                    Database = "Vetorh",
                    Username = "sa",
                    ColorTag = "#10B981"
                }
            };
        }
    }
}
