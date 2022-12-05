using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Adapters.Udp
{
	class UdpServerAdapter : IServerAdapter
	{
		public OnMessageReceived OnMessageReceived
		{
			get => _OnUdpMessageReceived;
			set
			{
				_OnUdpMessageReceived = value;

				if (_UdpServer != null)
					_UdpServer.OnMessageReceived = value;
			}
		}

		OnMessageReceived _OnUdpMessageReceived;
		UdpServer _UdpServer;

		public void Initialize(string address, ushort port)
		{
			_UdpServer = new UdpServer(address, port);
		}

		public void Start()
		{
			if (_UdpServer == null)
				throw new InvalidOperationException("Udp server hasnt been initialized yet");

			_UdpServer.Start();
		}

		public void Send(byte[] buffer, long offset, long length)
		{
			if (_UdpServer == null)
				throw new InvalidOperationException("Udp server hasnt been initialized yet");

			_UdpServer.Send(buffer, offset, length);
		}

		public void Send(IPEndPoint endpoint, byte[] buffer, long offset, long length)
		{
			if (_UdpServer == null)
				throw new InvalidOperationException("Udp server hasnt been initialized yet");

			_UdpServer.SendAsync(endpoint, buffer, offset, length);
		}

		public void ReceiveAsync()
		{
			if (_UdpServer == null)
				throw new InvalidOperationException("Udp server hasnt been initialized yet");

			_UdpServer.ReceiveAsync();
		}

		public void Dispose()
		{
			_UdpServer?.Dispose();
		}
	}
}
