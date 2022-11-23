using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge
{
	delegate void OnUdpMessageReceived(UdpMessageReceivedArgs args);

	internal class UdpClient : NetCoreServer.UdpClient
	{
		public OnUdpMessageReceived OnUdpMessageReceived;

		readonly UdpMessageReceivedArgs _Args = new();

		public UdpClient(IPEndPoint endpoint) : base(endpoint)
		{
		}

		public UdpClient(IPAddress address, int port) : base(address, port)
		{
		}

		public UdpClient(string address, int port) : base(address, port)
		{
		}

		protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
		{
			_Args.EndPoint = Endpoint;
			_Args.Buffer = buffer;
			_Args.Size = size;
			_Args.Offset = offset;

			OnUdpMessageReceived?.Invoke(_Args);
		}

		protected override void OnError(SocketError error)
		{
			Logger.Error(() => $"Socket Error:{error}");
		}

		protected override void OnConnecting()
		{
			Logger.Info(() => $"Udp client connecting to {Endpoint}");
		}

		protected override void OnConnected()
		{
			Logger.Info(() => $"Udp client connected to {Endpoint}");
		}
	}
}
