using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge
{
	class UdpMessageReceivedArgs 
	{
		public byte[] Buffer;
		public long Offset;
		public long Size;
		public IPEndPoint EndPoint;
	}
}
