using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TCPClient
{
    public static class Logger
    {
        private static readonly string logPath = ConfigurationManager.AppSettings["LoggingPath"];
        public static void Log(string message)
        {
            if (!string.IsNullOrEmpty(logPath))
            {
                //The following constructor will make the sw object to append the text in file instead of over-writing
                StreamWriter sw = new StreamWriter(logPath, true);
                sw.WriteLine($"{DateTime.Now.ToString()} : {message}");
                sw.Flush();
                sw.Close();
            }

        }
    }
}
