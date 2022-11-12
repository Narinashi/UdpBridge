using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Messages
{
	class HelloMessage : BaseMessage
	{
		public HelloMessage()
		{
			MessageType = (byte)Messages.MessageType.Hello;
		}

		public string Message;
	}
}
