using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Messages
{
	class KeyExchangeMessage : BaseMessage
	{
		public KeyExchangeMessage()
		{
			MessageType = (byte)Messages.MessageType.KeyExchange;
		}

		public Guid Identifier;
		public Guid Key;
		public Guid Dummy;
	}
}
