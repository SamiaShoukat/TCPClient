using System;
using Topshelf;
using System.Configuration;

namespace TCPClient
{
    internal class Program
    {
        private static readonly string serviceName = ConfigurationManager.AppSettings["ServiceName"];
        private static readonly string displayName = ConfigurationManager.AppSettings["ServiceDisplayName"];

        static void Main(string[] args)
        {

            var exitCode = HostFactory.Run(x =>
            {
                x.Service<TCPClient>(s =>
                {
                    s.ConstructUsing(scheduler => new TCPClient());
                    s.WhenStarted(scheduler => scheduler.OnStart());
                    s.WhenStopped(scheduler => scheduler.OnStop());
                });

                x.RunAsLocalSystem();
                x.SetServiceName(serviceName);
                x.SetDisplayName(displayName);
                x.SetDescription("This is the scheduler to tag call recordings");
            });

            int exitCodeValue = (int)Convert.ChangeType(exitCode, exitCode.GetTypeCode());
            Environment.ExitCode = exitCodeValue;

        }
    }
}
