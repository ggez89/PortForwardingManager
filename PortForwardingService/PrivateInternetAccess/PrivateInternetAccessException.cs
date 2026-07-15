namespace PortForwardingService.PrivateInternetAccess;

public class PrivateInternetAccessException: Exception {

    private PrivateInternetAccessException() {}

    public sealed class UnknownForwardedPort: PrivateInternetAccessException;

    public sealed class PortForwardingDisabled: PrivateInternetAccessException;

    public sealed class PortForwardingFailed: PrivateInternetAccessException;

}