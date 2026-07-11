namespace EmployeeManager.Tools.DemoRoster;

/// <summary>
/// SplitMix64-based PRNG. Used instead of <see cref="Random"/> so the committed dataset is
/// bit-for-bit reproducible regardless of .NET version or platform.
/// </summary>
public sealed class DeterministicRandom(ulong seed)
{
    private ulong _state = seed;

    /// <summary>Stable way to derive independent per-employee streams from one roster seed.</summary>
    public static DeterministicRandom ForSubStream(int seed, int index) =>
        new(Mix(((ulong)(uint)seed << 32) | (uint)index));

    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform int in [0, maxExclusive). Modulo bias is negligible at our range sizes.</summary>
    public int Next(int maxExclusive) => (int)(NextUInt64() % (ulong)maxExclusive);

    /// <summary>Uniform int in [minInclusive, maxInclusive].</summary>
    public int Next(int minInclusive, int maxInclusive) =>
        minInclusive + Next(maxInclusive - minInclusive + 1);

    public bool Chance(double probability) =>
        NextUInt64() < (ulong)(probability * ulong.MaxValue);

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    /// <summary>Up to <paramref name="count"/> distinct items, order randomized (Fisher–Yates on a copy).</summary>
    public List<T> Sample<T>(IReadOnlyList<T> items, int count)
    {
        var copy = items.ToList();
        for (var i = copy.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy.Take(Math.Min(count, copy.Count)).ToList();
    }
}
