using System.Reflection.Emit;

namespace ExternalPayload;

public static class Entry
{
    public static string RunSerializer()
    {
        var serializer = new NetSerializer.Serializer([typeof(Sample)]);
        var expected = new Sample { Count = 42, Label = "SS14 / Привет", Values = [3, 1, 4] };
        using var stream = new MemoryStream();
        serializer.Serialize(stream, expected);
        stream.Position = 0;
        var actual = (Sample)serializer.Deserialize(stream);
        if (actual.Count != expected.Count || actual.Label != expected.Label ||
            !actual.Values.SequenceEqual(expected.Values))
            throw new InvalidOperationException("NetSerializer round-trip changed the payload.");
        return "NetSerializer:42";
    }

    public static string Run()
    {
        var method = new DynamicMethod("ExternalAnswer", typeof(int), Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, 42);
        il.Emit(OpCodes.Ret);
        var answer = (Func<int>)method.CreateDelegate(typeof(Func<int>));
#if PAYLOAD_V2
        return "v2:" + answer();
#else
        return "v1:" + answer();
#endif
    }

    [Serializable]
    public sealed class Sample
    {
        public int Count;
        public string Label = "";
        public List<int> Values = [];
    }
}
