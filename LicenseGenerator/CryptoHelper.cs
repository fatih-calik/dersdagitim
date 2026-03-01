using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LicenseGenerator
{
    public class CryptoHelper
    {
        private static readonly CryptoHelper _instance = new CryptoHelper();
        public static CryptoHelper Shared => _instance;

        private CryptoHelper() { }

        // Swift's CryptoKit AES.GCM uses 12-byte nonce and 16-byte tag by default.
        private const int NonceSize = 12;
        private const int TagSize = 16;

        public byte[] GetKeyFromSecret(string secret)
        {
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(secret));
            }
        }

        public string? Encrypt(string text, byte[] key)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(text);
                byte[] nonce = new byte[NonceSize];
                RandomNumberGenerator.Fill(nonce);

                byte[] ciphertext = new byte[data.Length];
                byte[] tag = new byte[TagSize];

                using (var aes = new AesGcm(key, TagSize))
                {
                    aes.Encrypt(nonce, data, ciphertext, tag);
                }

                // Combine: Nonce + Ciphertext + Tag (Standard "combined" format often used with GCM)
                // Note: Check Swift implementation if it puts Tag before or after. 
                // Swift CryptoKit `combined` property = nonce + ciphertext + tag.

                byte[] combined = new byte[NonceSize + ciphertext.Length + TagSize];
                Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
                Buffer.BlockCopy(ciphertext, 0, combined, NonceSize, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, combined, NonceSize + ciphertext.Length, TagSize);

                return Convert.ToBase64String(combined);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Encryption error: {ex.Message}");
                return null;
            }
        }

        public string? Decrypt(string base64, byte[] key)
        {
            try
            {
                byte[] combined = Convert.FromBase64String(base64);

                if (combined.Length < NonceSize + TagSize)
                    return null;

                byte[] nonce = new byte[NonceSize];
                byte[] tag = new byte[TagSize];
                byte[] ciphertext = new byte[combined.Length - NonceSize - TagSize];

                Buffer.BlockCopy(combined, 0, nonce, 0, NonceSize);
                Buffer.BlockCopy(combined, NonceSize, ciphertext, 0, ciphertext.Length);
                Buffer.BlockCopy(combined, NonceSize + ciphertext.Length, tag, 0, TagSize);

                byte[] decryptedData = new byte[ciphertext.Length];

                using (var aes = new AesGcm(key, TagSize))
                {
                    aes.Decrypt(nonce, ciphertext, tag, decryptedData);
                }

                return Encoding.UTF8.GetString(decryptedData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Decryption error: {ex.Message}");
                return null;
            }
        }
    }
}
