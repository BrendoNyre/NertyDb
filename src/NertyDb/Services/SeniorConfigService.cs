using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using NertyDb.Models;

namespace NertyDb.Services
{
    public class SeniorConfigService
    {
        public static string[] DefaultSeniorPaths => new[]
        {
            @"C:\Senior\senior.cfg",
            @"D:\Senior\senior.cfg",
            @"E:\Senior\senior.cfg",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Senior", "senior.cfg"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Senior", "senior.cfg")
        };

        public string? DetectedConfigPath { get; private set; }
        public bool IsSeniorInstalled => !string.IsNullOrEmpty(DetectedConfigPath) && File.Exists(DetectedConfigPath);

        public SeniorConfigService(string? customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath))
            {
                DetectedConfigPath = File.Exists(customPath) ? customPath : null;
            }
            else
            {
                foreach (var path in DefaultSeniorPaths)
                {
                    if (File.Exists(path))
                    {
                        DetectedConfigPath = path;
                        break;
                    }
                }
            }
        }

        public List<SeniorDatabaseAlias> LoadAliases(string? specificPath = null)
        {
            var targetPath = specificPath ?? DetectedConfigPath;
            var list = new List<SeniorDatabaseAlias>();

            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                return list;
            }

            try
            {
                var doc = new XmlDocument();
                doc.Load(targetPath);

                string defaultAlias = string.Empty;
                var defaultNode = doc.SelectSingleNode("//configuration/com/senior/default_database/id");
                if (defaultNode != null && !string.IsNullOrEmpty(defaultNode.InnerText))
                {
                    defaultAlias = defaultNode.InnerText.Trim();
                }

                var databaseNodes = doc.SelectNodes("//configuration/com/senior/database/*");
                if (databaseNodes != null)
                {
                    foreach (XmlNode node in databaseNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element) continue;

                        var aliasName = node.Name;
                        var dbKind = node.SelectSingleNode("db_kind")?.InnerText?.Trim() ?? string.Empty;
                        var server = node.SelectSingleNode("serverJDBC")?.InnerText?.Trim() ?? "localhost";
                        var portStr = node.SelectSingleNode("portJDBC")?.InnerText?.Trim();
                        var dbName = node.SelectSingleNode("databasename")?.InnerText?.Trim() 
                                     ?? node.SelectSingleNode("database")?.InnerText?.Trim() 
                                     ?? aliasName;
                        var username = node.SelectSingleNode("username")?.InnerText?.Trim() ?? "sa";
                        var password = node.SelectSingleNode("password")?.InnerText?.Trim();
                        var plainPwd = SeniorCryptoService.Decrypt(password, SeniorCryptoService.DbKey) ?? password;
                        var tbs = node.SelectSingleNode("tbs/filename")?.InnerText?.Trim();
                        var desc = node.SelectSingleNode("description")?.InnerText?.Trim() ?? string.Empty;

                        var isOracle = dbKind.IndexOf("oracle", StringComparison.OrdinalIgnoreCase) >= 0;
                        int defaultPort = isOracle ? 1521 : 1433;
                        int port = defaultPort;
                        if (!string.IsNullOrEmpty(portStr) && int.TryParse(portStr, out int parsedPort))
                        {
                            port = parsedPort;
                        }

                        var item = new SeniorDatabaseAlias
                        {
                            Alias = aliasName,
                            Description = desc,
                            DbKind = dbKind,
                            DatabaseType = isOracle ? DatabaseType.Oracle : DatabaseType.SqlServer,
                            Server = server,
                            Port = port,
                            DatabaseName = dbName,
                            Username = username,
                            EncryptedPassword = password,
                            PlainPassword = plainPwd,
                            TbsFile = tbs,
                            IsDefault = string.Equals(aliasName, defaultAlias, StringComparison.OrdinalIgnoreCase)
                        };

                        list.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogException("SeniorConfigService.LoadAliases", ex);
            }

            return list;
        }
    }
}
