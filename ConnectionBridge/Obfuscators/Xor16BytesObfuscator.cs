using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Obfuscators
{
	class Xor16BytesObfuscator : IObfucator
	{
		public MessageReceivedArgs Deobfuscate(MessageReceivedArgs message, byte[] key, bool serverMode)
		{
			return Obfuscate(message, key, serverMode);
		}

		public MessageReceivedArgs Obfuscate(MessageReceivedArgs message, byte[] key, bool serverMode)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));

			if (message.Buffer == null)
				throw new ArgumentNullException("Buffer");

			if (key == null)
				throw new ArgumentNullException(nameof(key));
			
			if (key.Length != 16)
				throw new InvalidOperationException("Key must be exact 16 bytes");

			for (long i = 0, bufferLength = message.Size, bufferOffset = message.Offset; 
				i + 16 < bufferLength;
				bufferOffset += 16, i += 16)
			{
				message.Buffer[bufferOffset + 0] ^= key[0];
				message.Buffer[bufferOffset + 1] ^= key[1];
				message.Buffer[bufferOffset + 2] ^= key[2];
				message.Buffer[bufferOffset + 3] ^= key[3];
				message.Buffer[bufferOffset + 4] ^= key[4];
				message.Buffer[bufferOffset + 5] ^= key[5];
				message.Buffer[bufferOffset + 6] ^= key[6];
				message.Buffer[bufferOffset + 7] ^= key[7];
				message.Buffer[bufferOffset + 8] ^= key[8];
				message.Buffer[bufferOffset + 9] ^= key[9];
				message.Buffer[bufferOffset + 10] ^= key[10];
				message.Buffer[bufferOffset + 11] ^= key[11];
				message.Buffer[bufferOffset + 12] ^= key[12];
				message.Buffer[bufferOffset + 13] ^= key[13];
				message.Buffer[bufferOffset + 14] ^= key[14];
				message.Buffer[bufferOffset + 15] ^= key[15];
			}

			return message;
		}
	}
}
