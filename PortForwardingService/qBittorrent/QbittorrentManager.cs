#nullable enable

using Microsoft.Win32;
using NLog;
using PortForwardingService.PrivateInternetAccess;
using PortForwardingService.qBittorrent.ListeningPortEditors;
using qBittorrent.Client;
using qBittorrent.Client.Data;
using System.Diagnostics;
using System.Net;
using Unfucked.HTTP;

namespace PortForwardingService.qBittorrent;

public sealed class QbittorrentManager: IDisposable {

    private static readonly Logger   LOGGER                      = LogManager.GetLogger(typeof(QbittorrentManager).FullName!);
    private static readonly TimeSpan SOCKET_ERROR_CHECK_INTERVAL = TimeSpan.FromMinutes(3);

    private readonly QbittorrentConfig                    config;
    private readonly CookieContainer                      cookieContainer;
    private readonly HttpClientHandler                    httpClientHandler;
    private readonly HttpClient                            httpClient;
    private readonly qBittorrentHttpTransport              transport;
    private readonly qBittorrentClient                    qBittorrentClient;
    private readonly ConfigurationFileListeningPortEditor configurationFileListeningPortEditor = new();
    private readonly WebApiListeningPortEditor            webApiListeningPortEditor;
    private readonly PiaForwardedPortMonitor              piaForwardedPortMonitor;

    private Timer? timer;

    public QbittorrentManager(PiaForwardedPortMonitor piaForwardedPortMonitor) {
        this.piaForwardedPortMonitor = piaForwardedPortMonitor;
        config = QbittorrentConfig.Load();

        cookieContainer = new CookieContainer();
        httpClientHandler = new HttpClientHandler { CookieContainer = cookieContainer };
        Uri baseUri = new Uri(config.Url.EndsWith("/") ? config.Url : config.Url + "/");
        httpClient = new UnfuckedHttpClient(httpClientHandler) { BaseAddress = baseUri };

        transport = new qBittorrentHttpTransport(baseUri) { httpClient = httpClient };
        qBittorrentClient = new qBittorrentApiClient(transport);

        webApiListeningPortEditor = new WebApiListeningPortEditor(qBittorrentClient, ensureLoggedIn);
    }

    public async Task<bool> ensureLoggedIn() {
        if (string.IsNullOrEmpty(config.Username) && string.IsNullOrEmpty(config.Password)) {
            return true;
        }

        try {
            LOGGER.Info("Authenticating with qBittorrent Web API at {url}...", config.Url);
            var content = new FormUrlEncodedContent(new[] {
                new KeyValuePair<string, string>("username", config.Username ?? string.Empty),
                new KeyValuePair<string, string>("password", config.Password ?? string.Empty)
            });

            HttpResponseMessage response = await httpClient.PostAsync("api/v2/auth/login", content);
            string responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode && responseText.Trim().Equals("Ok.", StringComparison.OrdinalIgnoreCase)) {
                LOGGER.Info("Successfully authenticated with qBittorrent Web API.");
                return true;
            } else {
                LOGGER.Error("Failed to authenticate with qBittorrent Web API. Status: {status}, Response: {response}", response.StatusCode, responseText);
                return false;
            }
        } catch (Exception e) {
            LOGGER.Error(e, "Exception while attempting to authenticate with qBittorrent Web API.");
            return false;
        }
    }

    private ListeningPortEditor activeListeningPortEditor => isQbittorrentRunning()
        ? webApiListeningPortEditor
        : configurationFileListeningPortEditor;

    public Task<ushort?> getQbittorrentConfigurationListeningPort() => activeListeningPortEditor.getListeningPort();

    public async Task setQbittorrentListeningPort(ushort listeningPort) => await activeListeningPortEditor.setListeningPort(listeningPort);

    public static string? findExecutableAbsoluteFilename() {
        if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\qBittorrent", "DisplayIcon", null) is not string displayIcon) return null;
        string filename = Path.GetFullPath(displayIcon.TrimEnd("1234567890".AsEnumerable()).TrimEnd('-').TrimEnd(',').Trim('"'));
        return File.Exists(filename) ? filename : null;
    }

    private static bool isQbittorrentRunning() {
        Process[] qBittorrentProcesses = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(findExecutableAbsoluteFilename()));
        foreach (Process process in qBittorrentProcesses) {
            process.Dispose();
        }

        bool isRunning = qBittorrentProcesses.Length > 0;
        LOGGER.Debug("qBittorrent is currently {running}", isRunning ? "running" : "not running");
        return isRunning;
    }

    public void listenForSocketErrors() {
        timer = new Timer(async void (_) => {
            if (isQbittorrentRunning()) {
                try {
                    await ensureLoggedIn();
                    TransferInfo transferInfo = await qBittorrentClient.getTransferInfo();

                    if (transferInfo.connectionStatus != TransferInfo.ConnectionStatus.CONNECTED && piaForwardedPortMonitor.forwardedPort.Value is {} correctListeningPort) {
                        LOGGER.Info("qBittorrent connection state is {actual} instead of {expected}, which means it likely failed to listen on the given IP address and port.",
                            transferInfo.connectionStatus, TransferInfo.ConnectionStatus.CONNECTED);
                        ushort temporaryListeningPort = (ushort) (correctListeningPort < ushort.MaxValue ? correctListeningPort + 1 : correctListeningPort - 1);
                        LOGGER.Info("Setting qBittorrent listening port to {temp} and then to {real} in order to try to fix the socket listening error.", temporaryListeningPort, correctListeningPort);

                        await webApiListeningPortEditor.setListeningPort(temporaryListeningPort);
                        await Task.Delay(TimeSpan.FromSeconds(3));
                        await webApiListeningPortEditor.setListeningPort(correctListeningPort);
                    } else {
                        LOGGER.Debug("qBittorrent connection status is {status}, which does not indicate a socket error, ignoring.", transferInfo?.connectionStatus);
                    }
                } catch (HttpRequestException e) {
                    LOGGER.Warn(e, "Failed to check qBittorrent connection state due to error.");
                }
            } else {
                LOGGER.Debug("qBittorrent is not running, not checking for socket errors.");
            }
        }, null, SOCKET_ERROR_CHECK_INTERVAL, SOCKET_ERROR_CHECK_INTERVAL);
    }

    public void Dispose() {
        timer?.Dispose();
        qBittorrentClient.Dispose();
        httpClient.Dispose();
        httpClientHandler.Dispose();
    }

}