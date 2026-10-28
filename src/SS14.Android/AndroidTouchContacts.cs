using System.Threading;

namespace SS14.Android;

// Android owns the physical pointer lifetime; SDL finger IDs are pointer ID + 1.
internal sealed class AndroidTouchContacts
{
    private long[] _fingers = [];

    public void Update(ReadOnlySpan<int> pointers, int liftedIndex = -1)
    {
        var result = new long[pointers.Length - (liftedIndex >= 0 ? 1 : 0)];
        for (int source = 0, target = 0; source < pointers.Length; source++)
            if (source != liftedIndex) result[target++] = (long)pointers[source] + 1;
        Volatile.Write(ref _fingers, result);
    }

    public void Clear() => Volatile.Write(ref _fingers, []);
    public long[] Snapshot() => (long[])Volatile.Read(ref _fingers).Clone();
}
