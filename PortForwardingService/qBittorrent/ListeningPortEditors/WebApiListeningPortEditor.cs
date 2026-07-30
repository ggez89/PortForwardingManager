#nullable enable

using NLog;
using qBittorrent.Client;
using qBittorrent.Client.Data;

namespace PortForwardingService.qBittorrent.ListeningPortEditors;

internal sealed class WebApiListeningPortEditor(qBittorrentClient client, Func<Task<bool>>? ensureLoggedIn = null): ListeningPortEditor {

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(WebApiListeningPortEditor).FullName!);

    public async Task setListeningPort(ushort listeningPort) {
        LOGGER.Debug("Setting qBittorrent listening port to {listeningPort} using Web API.", listeningPort);
        if (ensureLoggedIn != null) {
            await ensureLoggedIn();
        }
        try {
            await client.setPreferences(new Preferences { listeningPort = listeningPort });
        } catch (HttpRequestException ex) when (ensureLoggedIn != null) {
            LOGGER.Warn(ex, "setPreferences failed via Web API. Attempting re-authentication and retrying...");
            if (await ensureLoggedIn()) {
                await client.setPreferences(new Preferences { listeningPort = listeningPort });
            } else {
                throw;
            }
        }
        LOGGER.Info("Set qBittorrent listening port to {listeningPort} using Web API.", listeningPort);
    }

    public async Task<ushort?> getListeningPort() {
        if (ensureLoggedIn != null) {
            await ensureLoggedIn();
        }
        try {
            return (await client.getPreferences()).listeningPort;
        } catch (HttpRequestException ex) when (ensureLoggedIn != null) {
            LOGGER.Warn(ex, "getPreferences failed via Web API. Attempting re-authentication and retrying...");
            if (await ensureLoggedIn()) {
                return (await client.getPreferences()).listeningPort;
            }
            throw;
        }
    }

}