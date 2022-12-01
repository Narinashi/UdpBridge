using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge
{
	interface IObfucator
	{
		MessageReceivedArgs Obfuscate(MessageReceivedArgs message, byte[] key, bool serverMode);

		MessageReceivedArgs Deobfuscate(MessageReceivedArgs message, byte[] key, bool serverMode);
	}
}
