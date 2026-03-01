using System;

namespace LicenseGenerator
{
    public class GeneratorLogic
    {
        private static readonly GeneratorLogic _instance = new GeneratorLogic();
        public static GeneratorLogic Shared => _instance;

        private GeneratorLogic() { }

        // Secret keys (MUST MATCH MAIN APP and Swift App)
        private const string KeyASecret = "DersDagitimRequestCodeKey2024";
        private const string KeyBSecret = "DersDagitimLicenseFileKey2024";

        public string? DecryptRequestCode(string code)
        {
            var key = CryptoHelper.Shared.GetKeyFromSecret(KeyASecret);
            return CryptoHelper.Shared.Decrypt(code, key);
        }

        public string? GenerateLicenseContent(string mac, string school, int year)
        {
            string content = $"{mac}|{school}|{year}";
            var key = CryptoHelper.Shared.GetKeyFromSecret(KeyBSecret);
            return CryptoHelper.Shared.Encrypt(content, key);
        }

        public string? GenerateDemoContent()
        {
            string content = $"DEMO:{DateTime.Now:yyyy-MM-dd}:0";
            var key = CryptoHelper.Shared.GetKeyFromSecret(KeyBSecret);
            return CryptoHelper.Shared.Encrypt(content, key);
        }
    }
}
