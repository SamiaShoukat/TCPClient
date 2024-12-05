using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TCPClient.Response
{
    internal class RegisterResponse
    {
        public bool AlreadyRegistered { get; set; }
        public int KeepAliveIntervalSeconds { get; set; }
        public string SourcePathForRawFiles { get; set; }
        public string DestinationPathForRawFiles { get; set; }
        public int Status { get; set; }
        public int ProcessingActionType { get; set; }
        public int DataType { get; set; }
    }
}
