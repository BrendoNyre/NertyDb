using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NertyDb.Services
{
    public static class SeniorCryptoService
    {
        public const int DbKey = 4318;
        public const int DefaultKey = 3574;
        public const int TokenKey = 3950;
        public const int LdapKey = 38541;
        public const int EmailKey = 4685;

        static SeniorCryptoService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static string? Decrypt(string? encryptedText, int key = DbKey)
        {
            if (string.IsNullOrWhiteSpace(encryptedText)) return encryptedText;

            try
            {
                byte[] decodedBase64 = Convert.FromBase64String(encryptedText.Trim());
                int currentKey = key;
                byte[] decryptedBytes = new byte[decodedBase64.Length];

                for (int i = 0; i < decodedBase64.Length; i++)
                {
                    currentKey %= 65535;
                    sbyte b = (sbyte)decodedBase64[i];
                    decryptedBytes[i] = (byte)(b ^ (currentKey >> 8));
                    currentKey = (currentKey + b) * 2845;
                }

                string intermediateBase64 = Encoding.ASCII.GetString(decryptedBytes);
                byte[] rawBytes = Convert.FromBase64String(intermediateBase64);
                return Encoding.GetEncoding("Windows-1252").GetString(rawBytes);
            }
            catch
            {
                return null;
            }
        }

        public static string? Encrypt(string? plainText, int key = DbKey)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return plainText;

            try
            {
                byte[] plainBytes = Encoding.GetEncoding("Windows-1252").GetBytes(plainText);
                string firstBase64 = Convert.ToBase64String(plainBytes);
                char[] chars = firstBase64.ToCharArray();
                byte[] encryptedBytes = new byte[chars.Length];
                int currentKey = key;

                for (int i = 0; i < chars.Length; i++)
                {
                    currentKey %= 65535;
                    byte b = (byte)((byte)chars[i] ^ (currentKey >> 8));
                    encryptedBytes[i] = b;
                    sbyte sb = (sbyte)b;
                    currentKey = (currentKey + sb) * 2845;
                }

                return Convert.ToBase64String(encryptedBytes);
            }
            catch
            {
                return null;
            }
        }

        #region Senior SGU User Password Encryption & Verification (G5 Architecture)

        private static int CheckSumPassword(byte[] usernameBytes)
        {
            int len = (usernameBytes.Length / 4) + 1;
            int[] array = new int[len];
            int targetIdx = 0;
            for (int i = 0; i < usernameBytes.Length; i += 4)
            {
                byte b0 = usernameBytes[i];
                byte b1 = (usernameBytes.Length > i + 1) ? usernameBytes[i + 1] : (byte)0;
                byte b2 = (usernameBytes.Length > i + 2) ? usernameBytes[i + 2] : (byte)0;
                byte b3 = (usernameBytes.Length > i + 3) ? usernameBytes[i + 3] : (byte)0;
                array[targetIdx++] = ((b3 & 0xFF) << 24) | ((b2 & 0xFF) << 16) | ((b1 & 0xFF) << 8) | (b0 & 0xFF);
            }

            double sum = 0.0;
            for (int i = 0; i < len; i++)
            {
                int val = array[i];
                if (val < 0)
                {
                    sum += (4294967296.0 + val);
                }
                else
                {
                    sum += val;
                }
            }

            while (sum > 4294967295.0)
            {
                sum -= 4294967296.0;
            }

            if (sum > 2147483647.0)
            {
                sum -= 4294967296.0;
                return (int)Math.Truncate(sum);
            }

            return (int)Math.Truncate(sum);
        }

        public static byte[] EncryptUserPassword(string username, string password)
        {
            byte[] userBytes = Encoding.GetEncoding("Windows-1252").GetBytes(username.Trim().ToUpperInvariant());
            int checksum = CheckSumPassword(userBytes);

            byte[] pwdBytes = Encoding.GetEncoding("Windows-1252").GetBytes(password ?? string.Empty);
            int[] buffer = new int[4 + pwdBytes.Length];
            buffer[0] = checksum & 0xFF;
            buffer[1] = (checksum >> 8) & 0xFF;
            buffer[2] = (checksum >> 16) & 0xFF;
            buffer[3] = (checksum >> 24) & 0xFF;

            for (int i = 0; i < pwdBytes.Length; i++)
            {
                buffer[4 + i] = pwdBytes[i];
            }

            for (int i = 4; i < buffer.Length; i++)
            {
                int inverted = buffer[i] ^ -1;
                if (inverted < 0)
                {
                    buffer[i] = 255 - buffer[i];
                }
                else
                {
                    buffer[i] = buffer[i] ^ -1;
                }
            }

            var list = new List<int>();
            for (int i = 0; i < buffer.Length; i++)
            {
                if ((checksum & 1) == 0)
                {
                    list.Add(buffer[i]);
                }
                else
                {
                    list.Insert(0, buffer[i]);
                }
                checksum = (checksum << 1) | (int)((uint)checksum >> 31);
            }

            byte[] result = new byte[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                result[i] = (byte)list[i];
            }
            return result;
        }

        public static byte[] DecodeUserData(IEnumerable<string> dataStrings)
        {
            var sb = new StringBuilder();
            foreach (var str in dataStrings)
            {
                if (!string.IsNullOrEmpty(str))
                {
                    sb.Append(str.Trim());
                }
            }

            int len = sb.Length;
            int byteCount = (len * 6) / 8;
            if (byteCount <= 0) return Array.Empty<byte>();

            using var ms = new MemoryStream(byteCount);
            int charIdx = 0;
            int remaining = byteCount;

            while (remaining > 0 && charIdx < len)
            {
                byte c0 = (byte)(sb[charIdx] - 33);
                byte c1 = (charIdx + 1 < len) ? (byte)(sb[charIdx + 1] - 33) : (byte)0;
                byte b = (byte)((c0 << 2) | (c1 >> 4));
                ms.WriteByte(b);
                remaining--;
                if (remaining == 0) break;

                charIdx++;
                byte c2 = (charIdx + 1 < len) ? (byte)(sb[charIdx + 1] - 33) : (byte)0;
                b = (byte)((c1 << 4) | (c2 >> 2));
                ms.WriteByte(b);
                remaining--;
                if (remaining == 0) break;

                charIdx++;
                byte c3 = (charIdx + 1 < len) ? (byte)(sb[charIdx + 1] - 33) : (byte)0;
                b = (byte)((c2 << 6) | c3);
                ms.WriteByte(b);
                remaining--;
                if (remaining == 0) break;

                charIdx += 2;
            }
            return ms.ToArray();
        }

        public static byte[]? ExtractEncryptedPasswordFromUserStream(byte[] userStream)
        {
            if (userStream == null || userStream.Length < 10) return null;

            try
            {
                using var ms = new MemoryStream(userStream);
                using var br = new BinaryReader(ms);

                // 1. Version: 4 bytes (little endian)
                uint version = br.ReadUInt32();

                // 2. Name: 1 byte length + ASCII bytes
                byte nameLen = br.ReadByte();
                if (nameLen > 0) ms.Seek(nameLen, SeekOrigin.Current);

                // 3. Description: 1 byte length + ASCII bytes
                byte descLen = br.ReadByte();
                if (descLen > 0) ms.Seek(descLen, SeekOrigin.Current);

                // 4. Creation Time: If version >= 5, double (8 bytes)
                if (version >= 5)
                {
                    ms.Seek(8, SeekOrigin.Current);
                }

                // 5. Full Name: 1 byte length + ASCII bytes
                byte fullNameLen = br.ReadByte();
                if (fullNameLen > 0) ms.Seek(fullNameLen, SeekOrigin.Current);

                // 6. Old Password Mode: If version >= 3, boolean (1 byte)
                if (version >= 3)
                {
                    ms.Seek(1, SeekOrigin.Current);
                }

                // 7. Password: 1 byte length + encrypted password bytes
                byte pwdLen = br.ReadByte();
                if (pwdLen == 0) return Array.Empty<byte>();

                return br.ReadBytes(pwdLen);
            }
            catch
            {
                return null;
            }
        }

        public static bool ValidateSguPassword(string username, string enteredPassword, byte[] storedUserStream)
        {
            byte[]? storedEncryptedPwd = ExtractEncryptedPasswordFromUserStream(storedUserStream);
            if (storedEncryptedPwd == null) return false;

            byte[] enteredEncryptedPwd = EncryptUserPassword(username, enteredPassword);
            return enteredEncryptedPwd.SequenceEqual(storedEncryptedPwd);
        }

        #endregion
    }
}
