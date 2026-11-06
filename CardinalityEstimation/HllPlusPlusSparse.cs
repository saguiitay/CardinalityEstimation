namespace CardinalityEstimation
{
    using System;
    using System.Collections.Generic;

    /// <summary>Sorted 25-bit indices plus 6-bit ranks, with a bounded update buffer.</summary>
    [Serializable]
    internal sealed class HllPlusPlusSparse
    {
        private const int BufferSize = 32;
        internal const int BufferBudgetBytes = 1024;
        private uint[] entries;
        private readonly Dictionary<uint, byte> pending = new Dictionary<uint, byte>();
        private int count;

        internal HllPlusPlusSparse(uint[] entries)
        {
            this.entries = (uint[])entries.Clone();
            count = entries.Length;
        }

        internal int Count => count;

        internal bool Add(uint index, byte rank)
        {
            byte previous;
            if (!pending.TryGetValue(index, out previous))
            {
                int position = Find(index);
                previous = position >= 0 ? (byte)(entries[position] & 63) : (byte)0;
            }
            if (rank <= previous)
                return false;
            if (previous == 0)
                count++;
            pending[index] = rank;
            if (pending.Count >= BufferSize)
            {
                entries = GetEntries();
                pending.Clear();
            }
            return true;
        }

        private int Find(uint index)
        {
            int low = 0, high = entries.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                uint candidate = entries[middle] >> 6;
                if (candidate == index)
                    return middle;
                if (candidate < index)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            return -1;
        }

        internal uint[] GetEntries()
        {
            if (pending.Count == 0)
                return (uint[])entries.Clone();

            var updates = new uint[pending.Count];
            int position = 0;
            foreach (var update in pending)
                updates[position++] = (update.Key << 6) | update.Value;
            Array.Sort(updates);
            var result = new uint[count];
            int existing = 0, updated = 0, output = 0;
            while (existing < entries.Length || updated < updates.Length)
            {
                if (updated == updates.Length ||
                    (existing < entries.Length && (entries[existing] >> 6) < (updates[updated] >> 6)))
                    result[output++] = entries[existing++];
                else if (existing == entries.Length || (updates[updated] >> 6) < (entries[existing] >> 6))
                    result[output++] = updates[updated++];
                else
                {
                    result[output++] = Math.Max(entries[existing++], updates[updated++]);
                }
            }
            return result;
        }
    }
}
