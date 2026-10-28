using System.Security.Cryptography;
using System.Text;
using SS14.Launcher.Models.EngineManager;

if (args.Length != 4) throw new ArgumentException("source.zip delta.ss14patch target.zip version");
var source = await File.ReadAllBytesAsync(args[0]);
var delta = await File.ReadAllBytesAsync(args[1]);
var target = await File.ReadAllBytesAsync(args[2]);
var hash = Convert.ToHexString(SHA256.HashData(target));
var version = args[3];
var checks = 0;
async Task<byte[]> Apply(byte[] input, byte[] patch, string requested, string? expected = null,
    CancellationToken cancel = default)
{
    using var output = new MemoryStream();
    await EngineBinaryPatch.Apply(new MemoryStream(input), new MemoryStream(patch), output,
        requested, expected ?? hash, cancel);
    return output.ToArray();
}
async Task Reject(string name, byte[] input, byte[] patch, string requested, string? expected = null,
    CancellationToken cancel = default)
{
    try { await Apply(input, patch, requested, expected, cancel); }
    catch (Exception error) when (error is InvalidDataException or CryptographicException or
                                 EndOfStreamException or OperationCanceledException)
    {
        Console.WriteLine("PASS " + name);
        checks++;
        return;
    }
    throw new Exception("Accepted invalid patch: " + name);
}
if (!(await Apply(source, delta, version)).AsSpan().SequenceEqual(target))
    throw new Exception("Generated archive differs from the signed Android engine.");
Console.WriteLine("PASS exact original ZIP -> Android ZIP transformation");
checks++;
var wrongSource = (byte[])source.Clone();
wrongSource[0] ^= 1;
await Reject("wrong official engine source", wrongSource, delta, version);
await Reject("different requested version", source, delta, version + "-wrong");
await Reject("different required output digest", source, delta, version, new string('0', 64));
await Reject("truncated patch", source, delta[..^1], version);
await Reject("cancelled patch", source, delta, version, cancel: new CancellationToken(true));
var headerLength = BitConverter.ToInt32(delta, 8);
byte[] WithOperation(byte operation, long a, long? b = null)
{
    using var stream = new MemoryStream();
    stream.Write(delta.AsSpan(0, 12 + headerLength));
    using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
    writer.Write(operation);
    writer.Write(a);
    if (b != null) writer.Write(b.Value);
    writer.Write((byte)255);
    return stream.ToArray();
}
await Reject("copy outside official archive", source, WithOperation(0, source.Length + 1L, 1), version);
await Reject("negative copy offset", source, WithOperation(0, -1, 1), version);
await Reject("excessive generated output", source, WithOperation(1, 513L * 1024 * 1024), version);
var badHeader = (byte[])delta.Clone();
BitConverter.GetBytes(65537).CopyTo(badHeader, 8);
await Reject("unbounded patch metadata", source, badHeader, version);
var badLiteral = (byte[])delta.Clone();
if (badLiteral[12 + headerLength] != 1) throw new Exception("Expected generator to begin with a literal ZIP header.");
badLiteral[12 + headerLength + 9] ^= 1;
await Reject("corrupted generated engine", source, badLiteral, version);
Console.WriteLine($"PASS {checks} engine patch checks");
