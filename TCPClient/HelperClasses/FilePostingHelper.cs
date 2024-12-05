using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace TCPClient
{
    public class FilePostingHelper
    {
        private readonly string postURL = ConfigurationManager.AppSettings["FilePostingAPI"];
        private static readonly string authAPI_URL = ConfigurationManager.AppSettings["AuthenticationURL_Post"];
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public FilePostingHelper() { }

        public async Task<string> GetAuthToken()
        {
            try
            {
                User user = new User();
                user.username = "test";
                user.passwordHash = GenerateHash("password");

                StringContent stringContent = new StringContent(JsonConvert.SerializeObject(user), Encoding.UTF8, "application/json");

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
                            //log.Info("Auth Token created successfully");
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
                log.Error("Exception while getting Auth Token: " + ex.Message.ToString());
            }
            return string.Empty;

        }
        public async Task<bool> Post(string f, string sourceBasePath, string token)
        {
            try
            {
                // Remove path from the file name.
                string fName = f.Substring(sourceBasePath.Length + 1);
                string commonFileName = fName.Substring(0, fName.Length - 4);

                List<string> files = Directory.EnumerateFiles(sourceBasePath)
                .Where(p => Path.GetFileName(p).ToLower().Contains(commonFileName.ToLower()))
                .ToList();
                bool inUse = false;

                if (files.Count <= 0)
                {
                    log.Info("No data found for provided call ID at the specified location");
                    return false;
                }
                foreach (string file in files)
                {
                    if (!FreeToUse(file))
                    {
                        inUse = true;
                        break;
                    }
                }

                if (inUse)
                {
                    Thread.Sleep(1000);
                }

                if (inUse)
                {
                    //log.Info("TX or RX file is in process of encryption");
                    return false;
                }
                if(files.Count != 3)
                {
                    log.Info("TX or RX file for the call is missing");
                    return false;
                }

                INIFile MyIni = new INIFile(Path.Combine(sourceBasePath, fName));
                string FileDestinationPath = MyIni.Read("DBDATA", "FileDestinationPath");
                string FileDestinationName = MyIni.Read("DBDATA", "FileDestinationName");
                bool success = false;
                int tries = 3;

                using (var multipartFormContent = new MultipartFormDataContent())
                {
                    StringContent destPath = new StringContent(FileDestinationPath);
                    multipartFormContent.Add(destPath, "DestinationPath");
                    StringContent destName = new StringContent(FileDestinationName);
                    multipartFormContent.Add(destName, "DestinationFileName");

                    //var fileStreamContent1 = new StreamContent(System.IO.File.OpenRead(f));

                    //multipartFormContent.Add(fileStreamContent1, name: "file0", fileName: f.Substring(sourceBasePath.Length + 1));

                    int counter = 1;
                    foreach (string file in files)
                    {
                        StreamContent fileStreamContent = new StreamContent(System.IO.File.OpenRead(file));
                        multipartFormContent.Add(fileStreamContent, name: "file" + counter, fileName: file.Substring(sourceBasePath.Length + 1));
                        counter++;
                    }

                    HttpClientHandler clientHandler = new HttpClientHandler();
                    clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => { return true; };


                    using (HttpClient client = new HttpClient(clientHandler))
                    {
                        HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, postURL);
                        requestMessage.Content = multipartFormContent;
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        using (HttpResponseMessage response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                log.Error("Error while posting call: " + commonFileName + Environment.NewLine + response.Content.ToString());

                            }
                            else if (response.IsSuccessStatusCode)
                            {
                                success = true;
                            }
                            else
                            {
                                log.Info(response.Content.ToString());
                            }
                        }
                    }

                }
                if (success)
                {
                    return true;
                }

            }
            catch (Exception ex)
            {
                log.Error(ex.Message.ToString());
                return false;
            }
            return false;
        }

        private static bool FreeToUse(string filePath)
        {
            FileInfo fi = new FileInfo(filePath);
            FileStream stream = null;
            try
            {
                stream = fi.Open(FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException ex)
            {
                return false;
            }
            finally
            {
                if (stream != null)
                {
                    stream.Close();
                }
            }
            return true;
        }

        private static string GenerateHash(string password)
        { 
            string hashedPass = BCrypt.Net.BCrypt.HashPassword(password);
            return hashedPass;
        }

    }
}
