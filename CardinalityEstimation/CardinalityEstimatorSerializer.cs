// /*  
//     See https://github.com/Microsoft/CardinalityEstimation.
//     The MIT License (MIT)
// 
//     Copyright (c) 2015 Microsoft
// 
//     Permission is hereby granted, free of charge, to any person obtaining a copy
//     of this software and associated documentation files (the "Software"), to deal
//     in the Software without restriction, including without limitation the rights
//     to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//     copies of the Software, and to permit persons to whom the Software is
//     furnished to do so, subject to the following conditions:
// 
//     The above copyright notice and this permission notice shall be included in all
//     copies or substantial portions of the Software.
// 
//     THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//     IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//     FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//     AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//     LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//     OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//     SOFTWARE.
// */

namespace CardinalityEstimation
{
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Text;

    using Hash;

    /// <summary>
    ///     Efficient serializer for <see cref="CardinalityEstimator" />
    /// </summary>
    public class CardinalityEstimatorSerializer
    {
        /// <summary>
        ///     Highest major version of the serialization format which this serializer can deserialize. A breaking change in the format requires a
        ///     bump in major version, i.e. version 2.X cannot read 3.Y
        /// </summary>
        public const ushort DataFormatMajorVersion = 2;

        /// <summary>
        ///     Minor version of the serialization format. A non-breaking change should be marked by a bump in minor version, i.e. version 2.2
        ///     should be able to read version 2.3
        /// </summary>
        public const ushort DataFormatMinorVersion = 1;

        /// <summary>
        /// Serializes <paramref name="cardinalityEstimator"/> to <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="cardinalityEstimator">The cardinality estimator.</param>
        public void Serialize(Stream stream, CardinalityEstimator cardinalityEstimator)
        {
            Serialize(stream, cardinalityEstimator, false);
        }

        /// <summary>
        /// Serializes <paramref name="cardinalityEstimator"/> to <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="cardinalityEstimator">The cardinality estimator.</param>
        /// <param name="leaveOpen">if set to <see langword="true" /> leave the stream open after serialization.</param>
        public void Serialize(Stream stream, CardinalityEstimator cardinalityEstimator, bool leaveOpen)
        {
            using (var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen))
            {
                Write(bw, cardinalityEstimator);
            }
        }

        /// <summary>
        /// Writer binary representation of <paramref name="cardinalityEstimator"/> using <paramref name="writer"/>
        /// </summary>
        /// <param name="writer">The writer</param>
        /// <param name="cardinalityEstimator">The cardinality estimator.</param>
        public void Write(BinaryWriter writer, CardinalityEstimator cardinalityEstimator)
        {
            writer.Write(DataFormatMajorVersion);
            writer.Write(DataFormatMinorVersion);

            CardinalityEstimatorState data = cardinalityEstimator.GetState();

            writer.Write((byte)data.HashFunctionId);
            writer.Write(data.BitsPerIndex);
            writer.Write((byte)(((data.IsSparse ? 1 : 0) << 1) + (data.DirectCount != null ? 1 : 0)));
            if (data.DirectCount != null)
            {
                writer.Write(data.DirectCount.Count);
                foreach (ulong element in data.DirectCount)
                {
                    writer.Write(element);
                }
            }
            else if (data.IsSparse)
            {
                writer.Write(data.LookupSparse.Count);
                foreach (KeyValuePair<ushort, byte> element in data.LookupSparse)
                {
                    writer.Write(element.Key);
                    writer.Write(element.Value);
                }
            }
            else
            {
                writer.Write(data.LookupDense.Length);
                foreach (byte element in data.LookupDense)
                {
                    writer.Write(element);
                }
            }

            writer.Write(data.CountAdditions);
            writer.Flush();
        }

        /// <summary>
        /// Deserialize a <see cref="CardinalityEstimator" /> from the given <paramref name="stream" />
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns>A new CardinalityEstimator.</returns>
        public CardinalityEstimator Deserialize(Stream stream)
        {
            return Deserialize(stream, false);
        }

        /// <summary>
        /// Deserialize a <see cref="CardinalityEstimator" /> from the given <paramref name="stream" />
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="leaveOpen">if set to <see langword="true" /> leave the stream open after deserialization.</param>
        /// <returns>A new CardinalityEstimator.</returns>
        public CardinalityEstimator Deserialize(Stream stream, bool leaveOpen)
        {
            using (var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen))
            {
                return Read(br);
            }
        }

        /// <summary>
        /// Reads a <see cref="CardinalityEstimator" /> using the given <paramref name="reader" />
        /// </summary>
        /// <param name="reader">The reader</param>
        /// <returns>An instance of <see cref="CardinalityEstimator" /></returns>
        public CardinalityEstimator Read(BinaryReader reader)
        {
            int dataFormatMajorVersion = reader.ReadUInt16();
            int dataFormatMinorVersion = reader.ReadUInt16();

            AssertDataVersionCanBeRead(dataFormatMajorVersion, dataFormatMinorVersion);

            HashFunctionId hashFunctionId;
            if (dataFormatMajorVersion >= 2)
            {
                // Starting with version 2.0, the serializer writes the hash function ID
                hashFunctionId = (HashFunctionId)reader.ReadByte();
            }
            else
            {
                // Versions before 2.0 all used FNV-1a
                hashFunctionId = HashFunctionId.Fnv1A;
            }

            int bitsPerIndex = reader.ReadInt32();
            byte flags = reader.ReadByte();
            bool isSparse = ((flags & 2) == 2);
            bool isDirectCount = ((flags & 1) == 1);

            HashSet<ulong> directCount = null;
            IDictionary<ushort, byte> lookupSparse = isSparse ? new Dictionary<ushort, byte>() : null;
            byte[] lookupDense = null;

            if (isDirectCount)
            {
                int count = reader.ReadInt32();
                directCount = new HashSet<ulong>();

                for (var i = 0; i < count; i++)
                {
                    ulong element = reader.ReadUInt64();
                    directCount.Add(element);
                }
            }
            else if (isSparse)
            {
                int count = reader.ReadInt32();

                for (var i = 0; i < count; i++)
                {
                    ushort elementKey = reader.ReadUInt16();
                    byte elementValue = reader.ReadByte();
                    lookupSparse.Add(elementKey, elementValue);
                }
            }
            else
            {
                int count = reader.ReadInt32();
                lookupDense = reader.ReadBytes(count);
            }

            // Starting with version 2.1, the serializer writes CountAdditions
            ulong countAdditions = 0UL;
            if (dataFormatMajorVersion >= 2 && dataFormatMinorVersion >= 1)
            {
                countAdditions = reader.ReadUInt64();
            }

            var data = new CardinalityEstimatorState
            {
                HashFunctionId = hashFunctionId,
                BitsPerIndex = bitsPerIndex,
                DirectCount = directCount,
                IsSparse = isSparse,
                LookupDense = lookupDense,
                LookupSparse = lookupSparse,
                CountAdditions = countAdditions,
            };

            var result = new CardinalityEstimator(data);

            return result;
        }

        /// <summary>
        ///     Checks that this serializer can deserialize data with the given major and minor version numbers
        /// </summary>
        /// <exception cref="SerializationException">If this serializer cannot read data with the given version numbers</exception>
        private static void AssertDataVersionCanBeRead(int dataFormatMajorVersion, int dataFormatMinorVersion)
        {
            if (dataFormatMajorVersion > DataFormatMajorVersion)
            {
                throw new SerializationException(
                    string.Format("Incompatible data format, can't deserialize data version {0}.{1} (serializer version: {2}.{3})",
                        dataFormatMajorVersion, dataFormatMinorVersion, DataFormatMajorVersion, DataFormatMinorVersion));
            }
        }
    }
}