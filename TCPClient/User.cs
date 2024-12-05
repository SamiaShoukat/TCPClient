using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TCPClient
{
    public class User
    {
        public string username { get; set; }
        public string passwordHash { get; set; }
        //public byte[] passwordSalt { get; set; }
    }
}
