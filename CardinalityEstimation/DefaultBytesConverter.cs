using System;
using System.Text;

namespace CardinalityEstimation
{
    public class DefaultBytesConverter : IBytesConverter
    {
        #region Implementation of IBytesConverter

        public byte[] GetBytes(object obj)
        {
            if (obj is string)
                return Encoding.UTF8.GetBytes(obj as string);

            if (obj is int)
                return BitConverter.GetBytes((int)obj);
            if (obj is uint)
                return BitConverter.GetBytes((uint)obj);
            if (obj is long)
                return BitConverter.GetBytes((long)obj);
            if (obj is ulong)
                return BitConverter.GetBytes((ulong)obj);
            if (obj is float)
                return BitConverter.GetBytes((float)obj);
            if (obj is double)
                return BitConverter.GetBytes((double)obj);
            if (obj is byte[])
                return obj as byte[];

            throw new NotSupportedException("Element is of an unknown type. Please implement a custom IBytesConverter");
        }

        #endregion
    }
}