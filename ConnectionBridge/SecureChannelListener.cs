using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using System.Net;
using System.Net.Sockets;

using System.Security.Cryptography.X509Certificates;

namespace ConnectionBridge
{
	delegate void OnConnectionReceived(SecureChannel client);

	class SecureChannelListener
	{
		public OnConnectionReceived OnConnectionReceived;

		readonly TcpListener _Listener;
		readonly int _Port;

		readonly X509Certificate _ServerCertificate;

		const int _BufferSize = 4096;

		public SecureChannelListener(int port, X509Certificate serverCertificate)
		{
			if (port < 1)
				throw new ArgumentException("Port number cant be less than 1");

			if (serverCertificate == null)
				throw new ArgumentNullException(nameof(serverCertificate));

			if (serverCertificate.Handle == IntPtr.Zero)
				throw new ArgumentException("Invalid certificate provided");

			_Port = port;
			_ServerCertificate = serverCertificate;
			_Listener = new TcpListener(IPAddress.Any, _Port);
			_Listener.Start();
		}

		public Task AcceptConnection()
		{
			return Task.Factory.StartNew(async () =>
			{
				while (true)
				{
					var client = await _Listener.AcceptTcpClientAsync();

					var secureChannel = new SecureChannel(client, _ServerCertificate, _BufferSize);
					await secureChannel.Authenticate();

					OnConnectionReceived?.Invoke(secureChannel);

				}
			}, TaskCreationOptions.LongRunning);
		}
	}
}
