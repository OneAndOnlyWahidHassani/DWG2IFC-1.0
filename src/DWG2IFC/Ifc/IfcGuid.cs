using System;
using System.Security.Cryptography;
using System.Text;

namespace Plant3D.IfcExporter.Ifc
{
    /// <summary>
    /// Implements standard IFC 22-character base64 compressed GUID according to buildingSMART specification.
    /// </summary>
    public static class IfcGuid
    {
        private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        public static string NewGuid() => ToIfcGuid(Guid.NewGuid());

        /// <summary>
        /// Generates a stable, reproducible IFC GUID based on entity identifiers (handle, drawing, rowId).
        /// Re-exporting unchanged components preserves their identity across BIM revisions.
        /// </summary>
        public static string CreateDeterministicGuid(string key)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
            return ToIfcGuid(new Guid(hash));
        }

        public static string ToIfcGuid(Guid guid)
        {
            byte[] bytes = guid.ToByteArray();

            // .NET stores first 3 GUID fields in little-endian. IFC expects big-endian.
            Array.Reverse(bytes, 0, 4);
            Array.Reverse(bytes, 4, 2);
            Array.Reverse(bytes, 6, 2);

            var chars = new char[22];
            int position = 22;

            for (int block = 5; block >= 0; block--)
            {
                uint value = ReadBlock(bytes, block);
                int digits = block == 0 ? 2 : 4;

                for (int i = 0; i < digits; i++)
                {
                    chars[--position] = Alphabet[(int)(value % 64)];
                    value /= 64;
                }
            }

            return new string(chars);
        }

        private static uint ReadBlock(byte[] bytes, int block)
        {
            switch (block)
            {
                case 0:
                    return (uint)(bytes[0] >> 2);
                case 1:
                    return ((uint)(bytes[0] & 0x03) << 22) |
                           ((uint)bytes[1] << 14) |
                           ((uint)bytes[2] << 6) |
                           ((uint)bytes[3] >> 2);
                case 2:
                    return ((uint)(bytes[3] & 0x03) << 22) |
                           ((uint)bytes[4] << 14) |
                           ((uint)bytes[5] << 6) |
                           ((uint)bytes[6] >> 2);
                case 3:
                    return ((uint)(bytes[6] & 0x03) << 22) |
                           ((uint)bytes[7] << 14) |
                           ((uint)bytes[8] << 6) |
                           ((uint)bytes[9] >> 2);
                case 4:
                    return ((uint)(bytes[9] & 0x03) << 22) |
                           ((uint)bytes[10] << 14) |
                           ((uint)bytes[11] << 6) |
                           ((uint)bytes[12] >> 2);
                case 5:
                    return ((uint)(bytes[12] & 0x03) << 22) |
                           ((uint)bytes[13] << 14) |
                           ((uint)bytes[14] << 6) |
                           (uint)bytes[15];
                default:
                    return 0;
            }
        }
    }
}
