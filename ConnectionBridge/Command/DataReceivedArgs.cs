using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge
{
	class DataReceivedArgs
	{
		public DataReceivedArgs(byte[] initialBuffer)
		{
			Buffer = initialBuffer;
		}

		public readonly byte[] Buffer;
		public int Length;
	}
}
