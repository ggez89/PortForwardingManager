#nullable enable

using NLog;
using Plugins;
using PortForwardingService.Plugins;
using PortForwardingService.PrivateInternetAccess;
using PortForwardingService.qBittorrent;
using System.ServiceProcess;

namespace PortForwardingService;

public sealed partial class Service: ServiceBase {

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(Service).FullName!);

    private readonly PiaForwardedPortMonitor                      piaForwardedPortMonitor = new();
    private readonly QbittorrentManager                           qBittorrentManager;
    private readonly IPluginManager<IPortForwardingServicePlugin> pluginManager = new PluginManager<IPortForwardingServicePlugin>("plugins", true);

    public Service() {
        qBittorrentManager = new QbittorrentManager(piaForwardedPortMonitor);
        InitializeComponent();
    }

    protected override void OnStart(string[] args) {
        pluginManager.LoadAll();

        piaForwardedPortMonitor.forwardedPort.PropertyChanged += async (_, eventArgs) => {
            try {
                LOGGER.Info("PIA forwarded port changed to {newPort}", eventArgs.NewValue?.ToString() ?? "null");

                ushort? qBittorrentListeningPort = await qBittorrentManager.getQbittorrentConfigurationListeningPort();

                if (eventArgs.NewValue is {} piaForwardedPort && piaForwardedPort != qBittorrentListeningPort) {
                    LOGGER.Debug("Changing qBittorrent listening port from {oldPort} to {newPort}", qBittorrentListeningPort, piaForwardedPort);
                    await qBittorrentManager.setQbittorrentListeningPort(piaForwardedPort);
                } else {
                    LOGGER.Debug("qBittorrent listening port was already set to {port}", eventArgs.NewValue);
                }

                foreach (IPortForwardingServicePlugin plugin in pluginManager.Plugins) {
                    plugin.OnForwardedPortChanged(eventArgs.NewValue, eventArgs.OldValue);
                }
            } catch (Exception e) when (e is not OutOfMemoryException) {
                LOGGER.Error(e, "Uncaught exception handling PIA forwarded port change");
            }
        };

        piaForwardedPortMonitor.listenForPiaPortForwardChanges();
        LOGGER.Info("Listening for forwarded port changes from PIA...");

        qBittorrentManager.listenForSocketErrors();
        LOGGER.Info("Listening for socket errors from qBittorrent...");
    }

    protected override void OnStop() {
        pluginManager.Dispose();
        piaForwardedPortMonitor.Dispose();
        qBittorrentManager.Dispose();
        LogManager.Shutdown();
    }

    internal void onStart(string[] args) => OnStart(args);

    internal void onStop() => OnStop();

}