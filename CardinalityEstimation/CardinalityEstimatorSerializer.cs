// /*  
//     See https://github.com/saguiitay/CardinalityEstimation.
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
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Text;

    using Hash;

    /// <summary>
    /// Provides efficient binary serialization and deserialization for <see cref="CardinalityEstimator" /> instances.
    /// This serializer supports version compatibility and can read data from multiple format versions.
    /// </summary>
    /// <remarks>
    /// <para>The serializer uses a compact binary format that preserves the complete state of CardinalityEstimators,
    /// including their accuracy parameters, internal data structures, and metadata.</para>
    /// <para>The format includes versioning information to ensure compatibility across different library versions.</para>
    /// <para>Supports all three counting modes: direct counting, sparse representation, and dense representation.</para>
    /// </remarks>
    public class CardinalityEstimatorSerializer
    {
        /// <summary>
        /// Highest major version of the serialization format which this serializer can deserialize. 
        /// A breaking change in the format requires a bump in major version, i.e. version 2.X cannot read 3.Y
        /// </summary>
        /// <value>The current major version supported by this serializer</value>
        public const ushort DataFormatMajorVersion = 3;

        /// <summary>
        /// Minor version of the serialization format. A non-breaking change should be marked by a bump in minor version, 
        /// i.e. version 2.2 should be able to read version 2.3
        /// </summary>
        /// <value>The current minor version of the serialization format</value>
        public const ushort DataFormatMinorVersion = 1;

        /// <summary>
        /// Serializes a <see cref="CardinalityEstimator"/> to the specified stream using the default settings.
        /// </summary>
        /// <param name="stream">The stream to write the serialized data to</param>
        /// <param name="cardinalityEstimator">The cardinality estimator to serialize</param>
        /// <remarks>
        /// The stream will be closed after serialization completes. Use the overload with leaveOpen parameter
        /// if you need to keep the stream open.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="stream"/> or <paramref name="cardinalityEstimator"/> is null
        /// </exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs during serialization</exception>
        public void Serialize(Stream stream, CardinalityEstimator cardinalityEstimator)
        {
            Serialize(stream, cardinalityEstimator, false);
        }

        /// <summary>
        /// Serializes a <see cref="CardinalityEstimator"/> to the specified stream with control over stream disposal.
        /// </summary>
        /// <param name="stream">The stream to write the serialized data to</param>
        /// <param name="cardinalityEstimator">The cardinality estimator to serialize</param>
        /// <param name="leaveOpen">
        /// If <c>true</c>, leaves the stream open after serialization; 
        /// if <c>false</c>, the stream will be closed
        /// </param>
        /// <remarks>
        /// The serialized format includes version information, accuracy parameters, and the complete
        /// internal state of the estimator, allowing for exact reconstruction upon deserialization.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="stream"/> or <paramref name="cardinalityEstimator"/> is null
        /// </exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs during serialization</exception>
        public void Serialize(Stream stream, CardinalityEstimator cardinalityEstimator, bool leaveOpen)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (cardinalityEstimator == null)
                throw new ArgumentNullException(nameof(cardinalityEstimator));

            using (var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen))
            {
                Write(bw, cardinalityEstimator);
            }
        }

        /// <summary>
        /// Writes the binary representation of a <see cref="CardinalityEstimator"/> using the specified writer.
        /// </summary>
        /// <param name="writer">The binary writer to use for serialization</param>
        /// <param name="cardinalityEstimator">The cardinality estimator to serialize</param>
        /// <remarks>
        /// <para>This method provides low-level control over the serialization process and can be used
        /// when you need to embed the estimator data within a larger serialization context.</para>
        /// <para>The method writes version information, configuration parameters, and internal state
        /// in a compact binary format.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="writer"/> or <paramref name="cardinalityEstimator"/> is null
        /// </exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs during writing</exception>
        public void Write(BinaryWriter writer, CardinalityEstimator cardinalityEstimator)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (cardinalityEstimator == null)
                throw new ArgumentNullException(nameof(cardinalityEstimator));

            writer.Write(DataFormatMajorVersion);
            writer.Write(DataFormatMinorVersion);

            CardinalityEstimatorState data = cardinalityEstimator.GetState();

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
        /// Deserializes a <see cref="CardinalityEstimator" /> from the specified stream.
        /// </summary>
        /// <param name="stream">The stream containing the serialized estimator data</param>
        /// <param name="hashFunction">
        /// Optional hash function to use for the deserialized estimator. If null, the hash function
        /// will be determined from the serialized data or default to XxHash128 for newer format versions.
        /// </param>
        /// <param name="leaveOpen">
        /// If <c>true</c>, leaves the stream open after deserialization; 
        /// if <c>false</c>, the stream will be closed
        /// </param>
        /// <returns>A new <see cref="CardinalityEstimator"/> restored from the serialized data</returns>
        /// <remarks>
        /// <para>This method automatically handles version compatibility and can read data serialized
        /// by older versions of the library, with appropriate fallback behavior for missing features.</para>
        /// <para>For format versions that don't include hash function information, appropriate defaults
        /// are used to maintain compatibility.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null</exception>
        /// <exception cref="SerializationException">
        /// Thrown when the data format version is not supported by this serializer
        /// </exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs during deserialization</exception>
        /// <exception cref="InvalidDataException">Thrown when the stream contains invalid or corrupted data</exception>
        public CardinalityEstimator Deserialize(Stream stream, GetHashCodeDelegate hashFunction = null, bool leaveOpen = false)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using (var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen))
            {
                return Read(br, hashFunction);
            }
        }

        /// <summary>
        /// Reads a <see cref="CardinalityEstimator" /> using the specified binary reader.
        /// </summary>
        /// <param name="reader">The binary reader containing the serialized estimator data</param>
        /// <param name="hashFunction">
        /// Optional hash function to use for the deserialized estimator. If null, the hash function
        /// will be determined from the serialized data or use appropriate defaults.
        /// </param>
        /// <returns>A new <see cref="CardinalityEstimator"/> instance restored from the serialized data</returns>
        /// <remarks>
        /// <para>This method provides low-level control over the deserialization process and handles
        /// format version compatibility automatically.</para>
        /// <para>Legacy format versions are supported with appropriate fallback behavior for features
        /// that didn't exist in older versions.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> is null</exception>
        /// <exception cref="SerializationException">
        /// Thrown when the data format version is not supported by this serializer
        /// </exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs during reading</exception>
        /// <exception cref="InvalidDataException">Thrown when the reader contains invalid or corrupted data</exception>
        public CardinalityEstimator Read(BinaryReader reader, GetHashCodeDelegate hashFunction = null)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            int dataFormatMajorVersion = reader.ReadUInt16();
            int dataFormatMinorVersion = reader.ReadUInt16();

            AssertDataVersionCanBeRead(dataFormatMajorVersion, dataFormatMinorVersion);

            byte hashFunctionId;
            if (dataFormatMajorVersion >= 3)
            {
            }
            else if (dataFormatMajorVersion >= 2)
            {
                // Starting with version 2.0, the serializer writes the hash function ID
                hashFunctionId = reader.ReadByte();
                if (hashFunction == null)
                {
                    hashFunction = (hashFunctionId == 1) ? (GetHashCodeDelegate)Murmur3.GetHashCode : (GetHashCodeDelegate)Fnv1A.GetHashCode;
                }
            }
            else
            {
                // Versions before 2.0 all used FNV-1a
                hashFunctionId = 0;
                hashFunction = Fnv1A.GetHashCode;
            }

            int bitsPerIndex = reader.ReadInt32();
            byte flags = reader.ReadByte();
            bool isSparse = (flags & 2) == 2;
            bool isDirectCount = (flags & 1) == 1;

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
                BitsPerIndex = bitsPerIndex,
                DirectCount = directCount,
                IsSparse = isSparse,
                LookupDense = lookupDense,
                LookupSparse = lookupSparse,
                CountAdditions = countAdditions,
            };

            var result = new CardinalityEstimator(hashFunction, data);

            return result;
        }

        /// <summary>
        /// Validates that this serializer can deserialize data with the given major and minor version numbers.
        /// </summary>
        /// <param name="dataFormatMajorVersion">The major version of the data format</param>
        /// <param name="dataFormatMinorVersion">The minor version of the data format</param>
        /// <exception cref="SerializationException">
        /// Thrown when this serializer cannot read data with the specified version numbers.
        /// This typically occurs when trying to read data from a newer major version.
        /// </exception>
        /// <remarks>
        /// The version compatibility rules are:
        /// - Same major version: Always compatible regardless of minor version
        /// - Different major version: Not compatible (breaking changes)
        /// - Higher minor version within same major: Compatible (backward compatibility)
        /// </remarks>
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