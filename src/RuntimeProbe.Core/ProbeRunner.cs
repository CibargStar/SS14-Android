using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace RuntimeProbe.Core;

public sealed record ProbeCheck(string Name, bool Passed, string Detail);
public sealed record ProbeReport(string Runtime, string Rid, bool DynamicCodeSupported,
    bool DynamicCodeCompiled, ProbeCheck[] Checks)
{
    public bool Passed => Checks.All(check => check.Passed);
}

public static class ProbeRunner
{
    // These files are developer-staged fixtures, not a production update format.
    public static ProbeReport Run(string fixtureDirectory)
    {
        var checks = new List<ProbeCheck>();
        Check("DynamicMethod", () =>
        {
            var method = new DynamicMethod("Add", typeof(int), [typeof(int)]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ret);
            var add = (Func<int, int>)method.CreateDelegate(typeof(Func<int, int>));
            Require(add(41) == 42, "Generated IL returned an unexpected result.");
            return "Generated and executed IL: 41 + 1 = 42.";
        });
        Check("Expression.Compile", () =>
        {
            var p = Expression.Parameter(typeof(int));
            var add = Expression.Lambda<Func<int, int>>(
                Expression.Add(p, Expression.Constant(1)), p).Compile();
            Require(add(41) == 42, "Compiled expression returned an unexpected result.");
            return "Compiled expression returned 42.";
        });
        Check("External assemblies and version isolation", () =>
        {
            var first = LoadFixture("v1");
            var second = LoadFixture("v2");
            Require(first.Assembly.GetName().FullName == second.Assembly.GetName().FullName,
                "Fixtures must have identical assembly identities.");
            Require(!ReferenceEquals(first.Assembly, second.Assembly), "Assembly identities were unified.");
            Require(Invoke(first.Assembly) == "v1:42", "Wrong result for payload v1.");
            Require(Invoke(second.Assembly) == "v2:42", "Wrong result for payload v2.");
            Require(Invoke(first.Assembly) == "v1:42", "Loading v2 changed the behavior of v1.");
            return "Two external DLLs with identical identities executed in separate load contexts; " +
                   "both generated and ran IL. This does not prove full RobustToolbox compatibility.";
        });
        Check("Upstream NetSerializer round-trip", () =>
        {
            var fixture = LoadFixture("v1");
            var entry = fixture.Assembly.GetType("ExternalPayload.Entry", throwOnError: true)!;
            var result = entry.GetMethod("RunSerializer", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
            Require((string?)result == "NetSerializer:42", "Unexpected NetSerializer result.");
            return "Loaded upstream Robust.NetSerializer outside APK; serialized and deserialized " +
                   "an object with integer, UTF-8 text and generic list fields. Not a full SS14 wire protocol test.";
        });
        Check("Corrupted payload rejection", () =>
        {
            var path = Path.Combine(fixtureDirectory, "v1", "ExternalPayload.dll");
            var bytes = File.ReadAllBytes(path);
            var expected = ReadExpectedHash(Path.GetDirectoryName(path)!);
            Require(bytes.Length > 0, "Empty fixture.");
            bytes[^1] ^= 1;
            try
            {
                ValidateHash(bytes, expected);
            }
            catch (CryptographicException)
            {
                return "Modified bytes rejected before assembly loading.";
            }
            throw new InvalidOperationException("Corrupted payload was accepted.");
        });

        return new ProbeReport(RuntimeInformation.FrameworkDescription, RuntimeInformation.RuntimeIdentifier,
            RuntimeFeature.IsDynamicCodeSupported, RuntimeFeature.IsDynamicCodeCompiled, checks.ToArray());

        void Check(string name, Func<string> action)
        {
            try { checks.Add(new ProbeCheck(name, true, action())); }
            catch (Exception e) { checks.Add(new ProbeCheck(name, false, e.ToString())); }
        }

        (AssemblyLoadContext Context, Assembly Assembly) LoadFixture(string variant)
        {
            var directory = Path.Combine(fixtureDirectory, variant);
            var bytes = File.ReadAllBytes(Path.Combine(directory, "ExternalPayload.dll"));
            ValidateHash(bytes, ReadExpectedHash(directory));
            // Never use Default: matching assembly identities must remain independent.
            var context = new FixtureLoadContext(directory);
            using var stream = new MemoryStream(bytes, writable: false);
            return (context, context.LoadFromStream(stream));
        }
    }

    private sealed class FixtureLoadContext(string directory)
        : AssemblyLoadContext("probe-" + Guid.NewGuid(), isCollectible: false)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Probe-only dependency contract. Framework assemblies resolve through the runtime.
            if (assemblyName.Name != "Robust.NetSerializer") return null;
            var path = Path.Combine(directory, "Robust.NetSerializer.dll");
            var bytes = File.ReadAllBytes(path);
            var expected = Convert.FromHexString(File.ReadAllText(path + ".sha256").Trim());
            ValidateHash(bytes, expected);
            using var stream = new MemoryStream(bytes, writable: false);
            return LoadFromStream(stream);
        }
    }

    private static string Invoke(Assembly assembly)
    {
        var entry = assembly.GetType("ExternalPayload.Entry", throwOnError: true)!;
        return (string)entry.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
    }

    private static byte[] ReadExpectedHash(string directory) =>
        Convert.FromHexString(File.ReadAllText(Path.Combine(directory, "sha256.txt")).Trim());

    private static void ValidateHash(byte[] bytes, byte[] expected)
    {
        if (expected.Length != 32 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), expected))
            throw new CryptographicException("Fixture SHA-256 mismatch.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
