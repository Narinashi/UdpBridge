using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using System.Net;
using System.Net.Sockets;

namespace ConnectionBridge
{
	delegate void OnConnectionReceived(SecureChannel client);

	class SecureChannelListener
	{
		readonly TcpListener _Listener;
		readonly int _Port;

		public SecureChannelListener(int port)
		{
			if (port < 1)
				throw new ArgumentException("Port number cant be less than 1");

			_Port = port;
			_Listener = new TcpListener(IPAddress.Any, _Port);
			_Listener.Start();
		}

		public TcpClient AcceptConnection()
		{
			return _Listener.AcceptTcpClient();
		}
	}
}
