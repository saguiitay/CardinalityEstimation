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

namespace CardinalityEstimation.Hash
{
    using System;
    using System.Collections.Concurrent;
    using Murmur;

    internal class Murmur3 : IHashFunction
    {
        private static readonly ConcurrentStack<Murmur128> pool = new ConcurrentStack<Murmur128>();

        public ulong GetHashCode(byte[] bytes)
        {
            Murmur128 murmurHash;
            if (!pool.TryPop(out murmurHash))
            {
                murmurHash = MurmurHash.Create128(managed: true, preference: AlgorithmPreference.X64);
            }

            byte[] result = murmurHash.ComputeHash(bytes);
            pool.Push(murmurHash);
            return BitConverter.ToUInt64(result, 0);
        }

        public HashFunctionId HashFunctionId
        {
            get { return HashFunctionId.Murmur3; }
        }
    }
}