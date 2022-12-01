using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Obfuscators
{
	class DnsObfuscator : IObfucator
	{
		readonly byte[] _ObfuscatingBuffer = new byte[4096];
		ushort _ObfuscationCounter;


		public DnsObfuscator()
		{
			_ObfuscationCounter = (ushort)(DateTime.Now.Ticks % 65000);
			//0-1: TransactionID
			//2-3: Flags
			_ObfuscatingBuffer[2] = 0b00000001;
			_ObfuscatingBuffer[3] = 0b00000000;
		}

		public MessageReceivedArgs Deobfuscate(MessageReceivedArgs message, byte[] key, bool serverMode)
		{
			//just throw away the first 4 bytes (transaction id & flags)
			message.Offset += 12;
			message.Size -= 12;

			return message;
		}

		public MessageReceivedArgs Obfuscate(MessageReceivedArgs message, byte[] key, bool serverMode)
		{
			//shift it 4 bytes forward
			Buffer.BlockCopy(message.Buffer, (int)message.Offset, _ObfuscatingBuffer, 12, (int)message.Size);
			var counterBuffer = BitConverter.GetBytes(_ObfuscationCounter);

			_ObfuscatingBuffer[0] = counterBuffer[0];
			_ObfuscatingBuffer[1] = counterBuffer[1];
			
			if (serverMode)
			{
				_ObfuscatingBuffer[2] = 0b10000001;
				_ObfuscatingBuffer[3] = 0b10000000;
			}
			else
			{
				_ObfuscatingBuffer[2] = 0b00000001;
				_ObfuscatingBuffer[3] = 0b00000000;
			}

			//Questions
			_ObfuscatingBuffer[4] = 0;
			_ObfuscatingBuffer[5] = 1;

			//answers
			_ObfuscatingBuffer[6] = 1;		
			_ObfuscatingBuffer[7] = (byte)(serverMode ? 1 : 0);

			//Auth RR
			_ObfuscatingBuffer[8] = 0;
			_ObfuscatingBuffer[9] = 0;

			//Add RR
			_ObfuscatingBuffer[10] = 0;
			_ObfuscatingBuffer[11] = 0;

			_ObfuscationCounter++;

			message.Size += 12;
			message.Buffer = _ObfuscatingBuffer;
			message.Offset = 0;

			return message;
		}
	}
}
