using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BCrypt.Net;
using log4net.Repository.Hierarchy;
using System.IO;
using System.Net.Http;

namespace TCPClient.HelperClasses
{
    public class EncryptionHelper
    {
        private readonly string authAPI_URL = ConfigurationManager.AppSettings["AuthenticationURL_Encrypt"];
        private readonly string getKeyURL = ConfigurationManager.AppSettings["KeyGeneratorAPI"];
        private readonly string destinantionPath = ConfigurationManager.AppSettings["destinationBasePath"];
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public EncryptionHelper() { }

        public async Task<string> GetAuthToken()
        {
            try
            {
                User user = new User();
                user.username = "test";
                user.passwordHash = GenerateHash("password");

                StringContent stringContent = new StringContent(JsonConvert.SerializeObject(user), Encoding.UTF8, "application/json");
                if (authAPI_URL == null)
                {
                    log.Info("Configuration missing for AuthenticationURL_Encrypt");
                    return string.Empty;
                }
                HttpClientHandler clientHandler = new HttpClientHandler();
                clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => { return true; };


                using (HttpClient client = new HttpClient(clientHandler))
                {
                    using (var httpResponse = await client.PostAsync(authAPI_URL, stringContent))
                    {
                        if (httpResponse.IsSuccessStatusCode)
                        {
                            httpResponse.EnsureSuccessStatusCode();
                            string responsContent = await httpResponse.Content.ReadAsStringAsync();
                            return responsContent;
                        }
                        else
                        {
                            log.Info("Token not successful");
                            return string.Empty;
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                log.Error("Exception while getting Auth Token: " + ex.Message.ToString());
                return string.Empty;
                
            }

        }

        public async Task<String> GetEncryptionKey(string token, string callStartTime)
        {
            try
            {
                HttpClientHandler clientHandler = new HttpClientHandler();
                clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => { return true; };

                HttpClient client = new HttpClient(clientHandler);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                // Usage
                using (var httpResponse = await client.GetAsync(getKeyURL + "?reqDateStr=" +  callStartTime))
                {
                    if (httpResponse.IsSuccessStatusCode)
                    {
                        httpResponse.EnsureSuccessStatusCode();
                        string responsContent = await httpResponse.Content.ReadAsStringAsync();
                        return responsContent;
                    }
                    else
                    {
                        log.Info("Encryption key not successful");
                        return string.Empty;
                    }

                }
            }
            catch (Exception ex)
            {
                log.Error(ex.Message.ToString());
                return string.Empty;
            }

        }

        public async void AES_Encrypt(string inputFile, string outputFile, byte[] passwordBytes, string callID, string encryptionKey)
        {
            try
            {
                byte[] saltBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
                string cryptFile = outputFile;
                FileStream fsCrypt = new FileStream(cryptFile, FileMode.Create);

                RijndaelManaged AES = new RijndaelManaged();

                AES.KeySize = 256;
                AES.BlockSize = 128;


                var key = new Rfc2898DeriveBytes(passwordBytes, saltBytes, 1000);
                AES.Key = key.GetBytes(AES.KeySize / 8);
                AES.IV = key.GetBytes(AES.BlockSize / 8);
                AES.Padding = PaddingMode.Zeros;

                AES.Mode = CipherMode.CFB;

                CryptoStream cs = new CryptoStream(fsCrypt,
                     AES.CreateEncryptor(),
                    CryptoStreamMode.Write);

                FileStream fsIn = new FileStream(inputFile, FileMode.Open);

                int data;
                while ((data = fsIn.ReadByte()) != -1)
                    cs.WriteByte((byte)data);


                fsIn.Close();
                cs.Close();
                fsCrypt.Close();
            }
            catch (Exception ex)
            {
                //List<string> failedCallFiles = Directory.EnumerateFiles(destinantionPath)
                //.Where(p => Path.GetFileName(p).ToLower().Contains(callID.ToLower()))
                //.ToList();

                //foreach (string file in failedCallFiles)
                //{
                //    System.IO.File.Delete(file);

                //}
                log.Error(ex.Message.ToString() + "  Failed to Encrypt Call");
            }
        }
        public static void Encrypt(string inputFile, string outputFile, byte[] passwordBytes, string callID)
        {
            //
            // Encrypt a small sample of data
            //
            String Plain = "The quick brown fox";
            byte[] plainBytes = Encoding.UTF8.GetBytes(Plain);

            FileStream fsIn = new FileStream(inputFile, FileMode.Open);
            byte[] bytearrayinput = new byte[fsIn.Length - 1];

            byte[] savedKey = new byte[16];
            byte[] savedIV = new byte[16];
            byte[] cipherBytes;
            using (RijndaelManaged Aes128 = new RijndaelManaged())
            {
                //
                // Specify a blocksize of 128, and a key size of 128, which make this
                // instance of RijndaelManaged an instance of AES 128.
                //
                Aes128.BlockSize = 128;
                Aes128.KeySize = 256;

                //
                // Specify CFB8 mode
                //
                Aes128.Mode = CipherMode.CFB;
                Aes128.FeedbackSize = 8;
                Aes128.Padding = PaddingMode.None;
                //
                // Generate and save random key and IV.
                //
                Aes128.GenerateKey();
                Aes128.GenerateIV();

                Aes128.Key.CopyTo(savedKey, 0);
                Aes128.IV.CopyTo(savedIV, 0);

                using (var encryptor = Aes128.CreateEncryptor())
                using (var msEncrypt = new MemoryStream())
                using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                using (var bw = new BinaryWriter(csEncrypt, Encoding.UTF8))
                {
                    bw.Write(plainBytes);
                    bw.Close();

                    cipherBytes = msEncrypt.ToArray();
                    Console.WriteLine("Cipher length is " + cipherBytes.Length);
                    Console.WriteLine("Cipher text is " + BitConverter.ToString(cipherBytes));
                }
            }
        }
        public static void AES_Decrypt(string inputFile, string outputFile, byte[] passwordBytes)
        {
            byte[] saltBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            FileStream fsCrypt = new FileStream(inputFile, FileMode.Open);

            RijndaelManaged AES = new RijndaelManaged();

            AES.KeySize = 256;
            AES.BlockSize = 128;


            var key = new Rfc2898DeriveBytes(passwordBytes, saltBytes, 1000);
            AES.Key = key.GetBytes(AES.KeySize / 8);
            AES.IV = key.GetBytes(AES.BlockSize / 8);
            AES.Padding = PaddingMode.Zeros;

            AES.Mode = CipherMode.CFB;

            CryptoStream cs = new CryptoStream(fsCrypt,
                AES.CreateDecryptor(),
                CryptoStreamMode.Read);

            FileStream fsOut = new FileStream(outputFile, FileMode.Create);

            int data;
            while ((data = cs.ReadByte()) != -1)
                fsOut.WriteByte((byte)data);

            fsOut.Close();
            cs.Close();
            fsCrypt.Close();

        }
        private static string GenerateHash(string password)
        {
            string hashedPass = BCrypt.Net.BCrypt.HashPassword(password);
            return hashedPass;
        }
    }

}
