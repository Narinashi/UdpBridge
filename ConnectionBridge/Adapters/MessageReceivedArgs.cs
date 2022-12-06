using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge
{
	class MessageReceivedArgs 
	{
		public MessageReceivedArgs(MessageReceivedArgs arg)
		{
			Buffer = arg.Buffer.ToArray();
			Offset = arg.Offset;
			Size = arg.Size;
			EndPoint = arg.EndPoint;
			PacketInformation = arg.PacketInformation;
		}

		public MessageReceivedArgs()
		{

		}

		public byte[] Buffer;
		public long Offset;
		public long Size;
		public IPPacketInformation PacketInformation;
		public IPEndPoint EndPoint;
	}
}
