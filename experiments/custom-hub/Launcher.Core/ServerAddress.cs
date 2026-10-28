namespace SS14.Launcher.Core;

public static class ServerAddress
{
    public static Uri Parse(string address)
    {
        address = address.Trim();
        if (!address.Contains("://", StringComparison.Ordinal)) address = "ss14://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("ss14" or "ss14s") || string.IsNullOrWhiteSpace(uri.Host) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new FormatException("Укажите адрес ss14://сервер[:порт] или ss14s://сервер/путь.");
        return uri;
    }

    public static Uri Api(Uri address, string resource)
    {
        var builder = new UriBuilder(address)
        {
            Scheme = address.Scheme == "ss14s" ? "https" : "http",
            Port = address.IsDefaultPort ? (address.Scheme == "ss14s" ? 443 : 1212) : address.Port,
            Path = address.AbsolutePath.TrimEnd('/') + "/"
        };
        return new Uri(builder.Uri, resource);
    }
}
