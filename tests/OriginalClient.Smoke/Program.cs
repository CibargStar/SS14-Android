using SS14.Loader;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: OriginalClient.Smoke <engine.zip> <signature.hex> <public-key.pem> <SS14.Client.zip>");
    return 1;
}
var gameData = Path.GetFullPath("artifacts/client-smoke-data");
Directory.CreateDirectory(gameData);
Environment.SetEnvironmentVariable("ROBUST_ANDROID_DATA", gameData);
// This runs the same original loader and server content with graphics disabled
// for host diagnostics. It does not establish that Android graphics/audio work.
return MobileLoader.Run([Path.GetFullPath(args[0]), args[1], Path.GetFullPath(args[2]),
        "--headless", "--mount-zip", Path.GetFullPath(args[3]), "+hardquit"],
    AppContext.BaseDirectory, (_, _) => { });
