using log4net.Repository.Hierarchy;
using Newtonsoft.Json;
using NuGet;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using TCPClient.HelperClasses;
using TCPClient.Request;
using TCPClient.Response;

namespace TCPClient
{
    public class TCPClient
    {
        private readonly System.Timers.Timer _timer;
        private readonly System.Timers.Timer _timer2;
        private string sourcePath = "";//ConfigurationManager.AppSettings["SourcePath"];
        private string destinantionPath = "";// ConfigurationManager.AppSettings["DestPath"];
        private readonly double timer = Convert.ToDouble(ConfigurationManager.AppSettings["ScheduledTime"]);
        private readonly double TokenValidity_Enc = Convert.ToDouble(ConfigurationManager.AppSettings["TokenValidityDuration"]);
        private readonly int port = Convert.ToInt32(ConfigurationManager.AppSettings["Port"]);
        private static readonly double TimeoutLimit = Convert.ToDouble(ConfigurationManager.AppSettings["TimeoutLimit"]);
        private static string encryptionKey = "";
        private static string encryptionToken = "";
        private static string postingToken = "";
        private TcpClient client = null;
        private NetworkStream stream = null;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);


        //private static int tries = 0;

        public TCPClient()
        {
            //_timer = new System.Timers.Timer(1000 * 30) { AutoReset = true };
            _timer = new System.Timers.Timer();
            _timer.AutoReset = false;
            _timer.Interval = (timer) * 1000;
            _timer.Elapsed += OnElapsedTimeBase;
            //////////////////////////////////// 

            _timer2 = new System.Timers.Timer();
            _timer2.AutoReset = true;
            _timer2.Interval = (TokenValidity_Enc - 1) * 60 * 60 * 1000;
            _timer2.Elapsed += UpdateEncryptionToken;

        }

        public void OnStart()
        {
            _timer.Start();
            _timer2.Start();
            
            log.Info("Service is started");
        }

        public void OnStop()
        {
            _timer.Stop();
            _timer2.Stop();
            if (client != null)
            {
                if (stream != null)
                {
                    Disconnect(stream);
                    stream.Close();
                }
                client.Close();
            }
            log.Info("Service is stopped");
        }

        private void OnElapsedTimeBase(object source, ElapsedEventArgs e)
        {

            int encRetryCount = 2;

            while (true)
            {
            Retry:
                if (TokenOnStart())
                {
                    break;
                }
                else
                {
                    encRetryCount--;
                    if (encRetryCount > 0)
                    {
                        log.Info("Retrying for tokens");
                        goto Retry;
                    }
                    else
                    {
                        log.Info("Retry attempts failed. Exiting execution since cannot proceed without API tokens.");
                        System.Environment.Exit(0);
                    }
                }
            }

            bool isSuccess = false;
            int retryCounter = 3;

        Connection:
            try
            {
                JobStatus jobStatus = new JobStatus();
                string message = "";
                client = new TcpClient("127.0.0.1", port);

                if (RequestRegistration(client))
                {
                    bool disconnectFlag = false;
                    //Start Communication
                    while (client.Connected)
                    {
                        bool noCallFound = false;
                        int processingStatus = 0;
                        try
                        {
                            stream = client.GetStream();
                            string callID = string.Empty;
                            StreamReader sr = new StreamReader(stream);

                            callID = sr.ReadLine();
                            //bool Completed = ExecuteWithTimeLimit(TimeSpan.FromMilliseconds(TimeoutLimit * 1000), () =>
                            //{
                            //    callID = sr.ReadLine();
                            //});

                            //if (!Completed)
                            //{
                            //    disconnectFlag = true;
                            //    break;
                            //}
                            //If communication is in process and call ID is null/empty, not valid system behaviour so try for new call ID
                            if (string.IsNullOrEmpty(callID))
                            {
                                Console.WriteLine(callID + " " + DateTime.Now.ToString()  + "Call ID is empty");
                                jobStatus.Status = 0;
                                jobStatus.StatusDsc = "EMPTY_CALL_ID";
                                message = SerializeMessage(jobStatus);
                                SendMessage(stream, message);
                            }
                            else
                            {
                                Console.WriteLine(callID + " " + DateTime.Now.ToString());

                                if (!string.IsNullOrEmpty(callID))
                                {
                                    List<string> files = Directory.EnumerateFiles(sourcePath)
                                    .Where(p => Path.GetFileName(p).ToLower().Contains(callID.ToLower()))
                                    .ToList();

                                    //If call ID is incorrect due to any reason or file is already processed by another client
                                    if (files.Count > 0)
                                    {
                                        processingStatus = StartProcessingCall(callID);
                                        if (processingStatus == 0)
                                        {
                                            isSuccess = true;
                                        }
                                    }
                                    else
                                    {
                                        noCallFound = true;
                                    }

                                }
                                if (isSuccess)
                                {
                                    log.Info("Call processed successfully: " + callID);
                                    jobStatus.Status = 1;
                                    jobStatus.StatusDsc = "PROCESS_CALL_SUCCESS";
                                    message = SerializeMessage(jobStatus);
                                    SendMessage(stream, message);
                                }
                                else if (noCallFound)
                                {
                                    jobStatus.Status = 2;
                                    jobStatus.StatusDsc = "CALL_NOT_FOUND";
                                    message = SerializeMessage(jobStatus);
                                    SendMessage(stream, message);
                                }
                                else
                                {
                                    switch (processingStatus)
                                    {
                                        case 1:
                                            jobStatus.Status = 3;
                                            jobStatus.StatusDsc = "PROCESS_FAILED_ENC_KEY";
                                            message = SerializeMessage(jobStatus);
                                            SendMessage(stream, message);
                                            break;
                                        case 2:
                                            jobStatus.Status = 3;
                                            jobStatus.StatusDsc = "PROCESS_CALL_FAILED_ENCRYPTION";
                                            message = SerializeMessage(jobStatus);
                                            SendMessage(stream, message);
                                            break;
                                        case 3:
                                            jobStatus.Status = 3;
                                            jobStatus.StatusDsc = "PROCESS_CALL_FAILED_POSTING";
                                            message = SerializeMessage(jobStatus);
                                            SendMessage(stream, message);
                                            break;
                                        case 4:
                                            jobStatus.Status = 5;
                                            jobStatus.StatusDsc = "PROCESS_ENCRYPT_DEST_DUPLICATE";
                                            message = SerializeMessage(jobStatus);
                                            SendMessage(stream, message);
                                            break;
                                        case 5:
                                            jobStatus.Status = 3;
                                            jobStatus.StatusDsc = "PROCESS_FAILED_COPY_WITHOUT_ENCRYPTION";
                                            message = SerializeMessage(jobStatus);
                                            SendMessage(stream, message);
                                            break;
                                        default:
                                            log.Info("Inconsistent Behavior");
                                            break;
                                    }

                                }
                            }
                        }
                        catch
                        {
                            Disconnect(stream);
                            stream.Close();
                            client.Close();
                            //OnStop();
                            break;
                            //jobStatus.Status = 3;
                            //jobStatus.StatusDsc = "PROCESS_CALL_FAILED_SOME_EXP";
                            //message = SerializeMessage(jobStatus);
                            //SendMessage(stream, message);
                            //break;
                        }
                    }
                    if (disconnectFlag)
                    {
                        //Process current callID before disconnecting
                        Disconnect(stream);
                        //Close connection
                        stream.Close();
                        client.Close();
                    }
                }
                else
                {
                    Disconnect(stream);
                    stream.Close();
                    client.Close();
                }

            }
            catch (System.Net.Sockets.SocketException ex)
            {
                log.Error("Connection not successful at port " + port);
                if (--retryCounter == 0)
                {
                    log.Error(ex.Message.ToString());
                    stream.Close();
                    client.Close();
                }
                log.Info("Failed to connect. Attempting Rtery");
                log.Error(ex.Message.ToString());
                goto Connection;
            }
            catch (Exception ex)
            {
                log.Error(ex.Message.ToString());
                stream.Close();
                client.Close();
            }

        }

        private bool RequestRegistration(TcpClient myClient)
        {
            string listenerResponseMessage = string.Empty;
            try
            {
                //string processID = "";
                //processID = Guid.NewGuid().ToString();
                NetworkStream myStream = myClient.GetStream();

                //SendMessage(myStream, processID);

                //Sending registration request to Listener/Server
                RegisterRequest request = new RegisterRequest();
                string requestMessage = string.Empty;

                request.ProcessName = Guid.NewGuid().ToString();
                StringContent sc = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                requestMessage = sc.ReadAsStringAsync().Result;
                SendMessage(myStream, requestMessage);

                //Reading server's response for registration request
                StreamReader mySr = new StreamReader(myStream);

                bool Completed = ExecuteWithTimeLimit(TimeSpan.FromMilliseconds(TimeoutLimit * 1000), () =>
                {
                    listenerResponseMessage = ReadServerMessage(myStream);
                });

                
                //Deserializing server's response to check if registeration was successfull or not
                RegisterResponse response = new RegisterResponse();
                response = JsonConvert.DeserializeObject<RegisterResponse>(listenerResponseMessage);
                if (response.Status == 1 && response.AlreadyRegistered == false)
                {
                    sourcePath = response.SourcePathForRawFiles;
                    destinantionPath = response.DestinationPathForRawFiles;
                    log.Info("Client successfully registered to the Listener");

                    SendMessage(myStream, "Acknowledged");

                    return true;
                }
                else
                {
                    log.Info("Client already connected - Signing Off");

                    SendMessage(myStream, "Acknowledged");

                    return false;
                }

                
            }
            catch(Exception ex)
            {
                log.Error(ex.Message.ToString() );
            }
            return false;

        }

        private static string ReadServerMessage(NetworkStream stream)
        {
            string message = string.Empty;
            try
            {
                byte[] buffer = new byte[1024];
                stream.Read(buffer, 0, buffer.Length);
                int recv = 0;
                foreach (byte b in buffer)
                {
                    if (b != 0)
                    {
                        recv++;
                    }
                }
                message = Encoding.UTF8.GetString(buffer, 0, recv);
            }
            catch (Exception e)
            {
                log.Error(e.Message.ToString());
            }
            return message;
        }


        private string SerializeMessage(JobStatus jobStatus)
        {
            StringContent sc = new StringContent(JsonConvert.SerializeObject(jobStatus), Encoding.UTF8, "application/json");
            string myContent = sc.ReadAsStringAsync().Result;
            return myContent;
        }
        private void SendMessage(NetworkStream stream, string message)
        {
            string messageToSend = message;

            int byteCount = Encoding.UTF8.GetByteCount(messageToSend + 1);
            byte[] sendData = new byte[byteCount];
            sendData = Encoding.UTF8.GetBytes(messageToSend);
            stream.Write(sendData, 0, sendData.Length);
        }

        private void Disconnect(NetworkStream stream)
        {
            //bool isSuccess = false;
            //StreamReader sr = new StreamReader(stream);
            //string callID = sr.ReadLine();
            //if (!string.IsNullOrEmpty(callID))
            //{
            //    //Before disconnecting, process the current call ID
            //    isSuccess = StartProcessingCall(callID);
            //}
            //if (isSuccess)
            //{
            //    SendMessage(stream, "Disconnect");
            //}
            //else
            //{
            //    SendMessage(stream, "Fail");
            //}
            string message = "";
            JobStatus jobStatus = new JobStatus();
            jobStatus.Status = 4;
            jobStatus.StatusDsc = "DISCONNECT_CLIENT_RQST";
            message = SerializeMessage(jobStatus);
            SendMessage(stream, message);

        }


        private string RegisterToListener()
        {
            int retryCounter = 3;
        Connection:
            while (true)
            {
                try
                {
                    TcpClient client = new TcpClient("127.0.0.1", 8089);
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    NetworkStream stream = client.GetStream();
                    StreamReader sr = new StreamReader(stream);
                    string response = sr.ReadLine();
                    Console.WriteLine(response);

                    stream.Close();
                    client.Close();

                    return response;
                }
                catch (Exception e)
                {
                    if (--retryCounter == 0)
                    {
                        return string.Empty;
                    }
                    log.Info("Failed to connect. Attempting Retry");
                    goto Connection;
                }
            }

        }



        private int StartProcessingCall(string callID)
        {
            int result = 0;
            bool isSuccess = false;
            try
            {
                EncryptionHelper encryptionHelper = new EncryptionHelper();

                string fName = callID + ".ini";
                INIFile MyIni = new INIFile(Path.Combine(sourcePath, fName));
                string callStartTime = MyIni.Read("CALL", "StartTime");
                int enableEncryption = Convert.ToInt32(MyIni.Read("DBDATA", "EnableEncryption"));

                if(enableEncryption == 0)
                {
                    if(!MoveWithoutEncryption(callID))
                    {
                        return 5;
                    }
                    isSuccess = true;
                }
                else
                {
                    encryptionKey = encryptionHelper.GetEncryptionKey(encryptionToken, callStartTime).Result.ToString();
                    if (string.IsNullOrEmpty(encryptionKey))
                    {
                        encryptionToken = RetryEncryptionToken();
                        if (!string.IsNullOrEmpty(encryptionToken))
                        {
                            encryptionKey = encryptionHelper.GetEncryptionKey(encryptionToken, callStartTime).Result.ToString();
                        }
                        if (string.IsNullOrEmpty(encryptionKey))
                        {
                            log.Info("Unable to fetch encryption key, Token: " + encryptionToken);
                            result = 1;
                            return result;
                        }
                    }
                    int encStatus = EncryptFiles(callID);
                    if (encStatus >= 0)
                    {
                        if (encStatus == 0)
                        {
                            isSuccess = true;
                        }
                        else if (encStatus == 1)
                        {
                            result = 4;
                            return result;
                        }
                    }
                    else
                    {
                        log.Info("Some unexpected behaviour while encryption");
                        return -1;
                    }

                }

                if (isSuccess)
                {
                    isSuccess = StartPosting(callID);
                    if (!isSuccess)
                    {
                        result = 3;
                    }
                }
                else
                {
                    result = 2;
                }

            }
            catch (Exception ex)
            {
                log.Error(ex.Message.ToString());
            }
            return result;
        }

        private bool MoveWithoutEncryption(string callID)
        {
            try
            {
                List<string> files = Directory.EnumerateFiles(sourcePath)
                .Where(p => Path.GetFileName(p).ToLower().Contains(callID.ToLower()))
                .ToList();
                string fName = "";
                foreach (string file in files)
                {
                    fName = file.Substring(sourcePath.Length + 1);
                    System.IO.File.Copy(Path.Combine(sourcePath, fName), Path.Combine(destinantionPath, fName));
                }
                return true;
            }
            catch(Exception ex)
            {
                log.Error(ex.Message.ToString());
            }
            return false;
        }

        private void HandleSuccess(string callID)
        {
            List<string> failedCallFiles_Dest = Directory.EnumerateFiles(destinantionPath)
                        .Where(p => Path.GetFileName(p).ToLower().Contains(callID.ToLower()))
                        .ToList();

            List<string> failedCallFiles_Source = Directory.EnumerateFiles(sourcePath)
            .Where(p => Path.GetFileName(p).ToLower().Contains(callID.ToLower()))
            .ToList();

            foreach (string file in failedCallFiles_Dest)
            {
                System.IO.File.Delete(file);
            }
            foreach (string file in failedCallFiles_Source)
            {
                System.IO.File.Delete(file);
            }
        }

        private void HandleFail(string callID)
        {
            List<string> failedCallFiles_Dest = Directory.EnumerateFiles(destinantionPath)
                        .Where(p => Path.GetFileName(p).ToLower().Contains(callID.ToLower()))
                        .ToList();


            foreach (string file in failedCallFiles_Dest)
            {
                System.IO.File.Delete(file);
            }
        }

        private int EncryptFiles(string callID)
        {
            int encStatus = -1;
            try
            {
                byte[] passwordBytes = Encoding.Unicode.GetBytes(encryptionKey);
                //byte[] passwordBytes = Encoding.Unicode.GetBytes("xOKoQ0MvQL7TxYt3spYNYDjbYTTJ2v8=");

                encStatus = StartEncryption(passwordBytes, callID, encryptionKey);


            }
            catch (Exception ex)
            {
                log.Error(ex.Message.ToString());
            }
            return encStatus;
        }

        private int StartEncryption(byte[] passwordBytes, string callID, string encryptionKey)
        {
            try
            {
                EncryptionHelper encryptionHelper = new EncryptionHelper();
                List<string> files = Directory.EnumerateFiles(sourcePath)
                .Where(p => Path.GetFileName(p).ToLower().Contains(callID.ToLower()))
                .ToList();

                if (files.Count == 3)
                {
                    foreach (string f in files)
                    {
                        string fName = f.Substring(sourcePath.Length + 1);

                        if (System.IO.File.Exists(Path.Combine(destinantionPath, fName)))
                        {
                            //log.Info("File already exists at the destination: " + fName);
                            return 1;
                            //continue;
                        }
                        if (f.Substring(f.Length - 3) == "ini")
                        {
                            System.IO.File.Copy(Path.Combine(sourcePath, fName), Path.Combine(destinantionPath, fName));
                            continue;
                        }

                        encryptionHelper.AES_Encrypt(f, Path.Combine(destinantionPath, fName), passwordBytes, callID, encryptionKey);


                    }
                    return 0;
                }
                else
                {
                    return 1;
                }
                


            }
            catch (Exception ex)
            {
                log.Error(ex.ToString());
                return 2;
            }
            return 2;

        }


        private bool StartPosting(string callID)
        {
            FilePostingHelper filePosting = new FilePostingHelper();
            bool isSuccess = false;
            if (string.IsNullOrEmpty(postingToken))
            {
                log.Info("Unable to fetch Posting API token");
                return false;
            }

            isSuccess = filePosting.Post(Path.Combine(destinantionPath, callID + ".ini"), destinantionPath, postingToken).Result;
            if(!isSuccess)
            {
                postingToken = RetryPostingToken();
                if(!string.IsNullOrEmpty(postingToken))
                {
                    isSuccess = filePosting.Post(Path.Combine(destinantionPath, callID + ".ini"), destinantionPath, postingToken).Result;
                }
                else
                {
                    isSuccess = false;
                    log.Error("Token to call File Posting API is not valid");
                }
            }
            return isSuccess;

        }

        private bool FreeToUse(string filePath)
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

        private void UpdateEncryptionToken(object source, ElapsedEventArgs e)
        {
            FilePostingHelper filePostingHelper = new FilePostingHelper();
            EncryptionHelper encryptionHelper = new EncryptionHelper();
            encryptionToken = encryptionHelper.GetAuthToken().Result.ToString();
            postingToken = filePostingHelper.GetAuthToken().Result.ToString();
        }

        private static bool ExecuteWithTimeLimit(TimeSpan timeSpan, Action codeBlock)
        {
            //Scheduler s = new Scheduler();
            try
            {
                Task task = Task.Factory.StartNew(() => codeBlock());
                task.Wait(timeSpan);
                return task.IsCompleted;
            }
            catch (AggregateException ex)
            {
                log.Error(ex.Message.ToString());
                throw ex.InnerExceptions[0];
            }
        }

        private bool TokenOnStart()
        {
            EncryptionHelper EncHelper = new EncryptionHelper();
            FilePostingHelper filePostingHelper = new FilePostingHelper();
            encryptionToken = EncHelper.GetAuthToken().Result.ToString();
            if (string.IsNullOrEmpty(encryptionToken))
            {
                log.Info("Unable to fetch Auth Token for Encryption Key API");
                return false;
            }

            postingToken = filePostingHelper.GetAuthToken().Result.ToString();
            if (string.IsNullOrEmpty(postingToken))
            {
                log.Info("Unable to fetch Auth Token for File Posting API");
                return false;
            }
            return true;

        }

        private string RetryEncryptionToken()
        {
            string token = "";
            EncryptionHelper EncHelper = new EncryptionHelper();
            token = EncHelper.GetAuthToken().Result.ToString();
            return token;
        }

        private string RetryPostingToken()
        {
            string token = "";
            FilePostingHelper PostingHelper = new FilePostingHelper();
            token = PostingHelper.GetAuthToken().Result.ToString();
            return token;
        }
    }
}
    