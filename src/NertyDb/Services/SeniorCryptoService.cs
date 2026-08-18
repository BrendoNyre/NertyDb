using System;
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
    }
}
