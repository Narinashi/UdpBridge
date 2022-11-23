using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Messages
{
	internal class PingMessage : BaseMessage
	{
		public long Time;

		public PingMessage()
		{
			MessageType = (byte)Messages.MessageType.Ping;
		}
	}
}
