using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Messages
{
	abstract class BaseMessage
	{
		public short Length;
		public byte MessageType;
	}
}
