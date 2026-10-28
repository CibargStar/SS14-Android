using System.Text.Json;
using RuntimeProbe.Core;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: RuntimeProbe.Cli <fixture-directory>");
    return 2;
}
var report = ProbeRunner.Run(Path.GetFullPath(args[0]));
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return report.Passed ? 0 : 1;
