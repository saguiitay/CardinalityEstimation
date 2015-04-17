using System.Collections.Generic;
using System.IO;

namespace CardinalityEstimation
{
    class CardinalityEstimatorSerializer
    {
        public void Serialize(Stream stream, CardinalityEstimator cardinalityEstimator)
        {
            using (var bw = new BinaryWriter(stream))
            {
                var data = cardinalityEstimator.GetData();

                bw.Write(data.bitsPerIndex);
                bw.Write((byte)(((data.isSparse ? 1 : 0) << 1) + (data.directCount != null ? 1 : 0)));
                if (data.directCount != null)
                {
                    bw.Write(data.directCount.Count);
                    foreach (var element in data.directCount)
                        bw.Write(element);
                }
                else if (data.isSparse)
                {
                    bw.Write(data.lookupSparse.Count);
                    foreach (var element in data.lookupSparse)
                    {
                        bw.Write(element.Key);
                        bw.Write(element.Value);
                    }
                }
                else
                {
                    bw.Write(data.lookupDense.Length);
                    foreach (var element in data.lookupDense)
                        bw.Write(element);
                }
                bw.Flush();
            }
        }

        public CardinalityEstimator Deserialize(Stream stream)
        {
            using (var br = new BinaryReader(stream))
            {
                var bitsPerIndex = br.ReadInt32();
                var flags = br.ReadByte();
                var isSparse = ((flags & 2) == 2);
                var isDirectCount = ((flags & 1) == 1);

                HashSet<ulong> directCount = null;
                IDictionary<ushort, byte> lookupSparse = null;
                byte[] lookupDense = null;

                if (isDirectCount)
                {
                    var count = br.ReadInt32();
                    directCount = new HashSet<ulong>();

                    for (int i = 0; i < count; i++)
                    {
                        var element = br.ReadUInt64();
                        directCount.Add(element);
                    }
                }
                else if (isSparse)
                {
                    var count = br.ReadInt32();
                    lookupSparse = new Dictionary<ushort, byte>();

                    for (int i = 0; i < count; i++)
                    {
                        var elementKey = br.ReadUInt16();
                        var elementValue = br.ReadByte();
                        lookupSparse.Add(elementKey, elementValue);
                    }
                }
                else
                {
                    var count = br.ReadInt32();
                    lookupDense = br.ReadBytes(count);
                }

                var data = new CardinalityEstimator.CardinalityEstimatorData
                    {
                        bitsPerIndex = bitsPerIndex,
                        directCount = directCount,
                        isSparse = isSparse,
                        lookupDense = lookupDense,
                        lookupSparse = lookupSparse
                    };

                var result = new CardinalityEstimator(data);

                return result;
            }
        }

    }
}
