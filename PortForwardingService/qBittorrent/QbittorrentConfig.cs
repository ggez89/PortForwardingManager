#nullable enable

using IniParser;
using IniParser.Model;
using IniParser.Model.Configuration;
using IniParser.Parser;
using NLog;
using System;
using System.IO;
using System.Text;

namespace PortForwardingService.qBittorrent;

public sealed class QbittorrentConfig {

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(QbittorrentConfig).FullName!);
    public static readonly string CONFIGURATION_FILE_PATH = Environment.ExpandEnvironmentVariables(@"%appdata%\PortForwardingService\config.ini");

    private const string SECTION_NAME = "qBittorrent";
    private const string USERNAME_KEY = "Username";
    private const string PASSWORD_KEY = "Password";
    private const string URL_KEY      = "Url";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Url      { get; set; } = "http://localhost:8080";

    private readonly FileIniDataParser iniFileEditor = new(new IniDataParser(new IniParserConfiguration { AssigmentSpacer = " " }));

    public static QbittorrentConfig Load() {
        QbittorrentConfig config = new();
        try {
            string directory = Path.GetDirectoryName(CONFIGURATION_FILE_PATH)!;
            if (!Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(CONFIGURATION_FILE_PATH)) {
                LOGGER.Info("Configuration file not found at {path}. Creating default config file.", CONFIGURATION_FILE_PATH);
                config.Save();
            } else {
                IniData data = config.iniFileEditor.ReadFile(CONFIGURATION_FILE_PATH, Encoding.UTF8);
                if (data.Sections.ContainsSection(SECTION_NAME)) {
                    KeyDataCollection section = data[SECTION_NAME];
                    config.Username = section[USERNAME_KEY] ?? string.Empty;
                    config.Password = section[PASSWORD_KEY] ?? string.Empty;
                    string? url = section[URL_KEY];
                    if (!string.IsNullOrWhiteSpace(url)) {
                        config.Url = url;
                    }
                }
                LOGGER.Info("Loaded qBittorrent configuration from {path}.", CONFIGURATION_FILE_PATH);
            }
        } catch (Exception ex) {
            LOGGER.Error(ex, "Failed to load qBittorrent configuration file from {path}. Using defaults.", CONFIGURATION_FILE_PATH);
        }
        return config;
    }

    public void Save() {
        try {
            IniData data = new();
            data.Sections.AddSection(SECTION_NAME);
            data[SECTION_NAME][USERNAME_KEY] = Username;
            data[SECTION_NAME][PASSWORD_KEY] = Password;
            data[SECTION_NAME][URL_KEY]      = Url;

            iniFileEditor.WriteFile(CONFIGURATION_FILE_PATH, data, Encoding.UTF8);
            LOGGER.Debug("Saved qBittorrent configuration to {path}.", CONFIGURATION_FILE_PATH);
        } catch (Exception ex) {
            LOGGER.Error(ex, "Failed to save qBittorrent configuration to {path}.", CONFIGURATION_FILE_PATH);
        }
    }

}
