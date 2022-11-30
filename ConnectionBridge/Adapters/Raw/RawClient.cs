using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Adapters.Raw
{
	internal class RawClient : NetCoreServer.UdpClient
	{
		public OnMessageReceived OnMessageReceived { set; get; }

		readonly MessageReceivedArgs _Args = new();

		const int SIO_UDP_CONNRESET = -1744830452;

		public RawClient(IPEndPoint endpoint) : base(endpoint)
		{
		}

		public RawClient(IPAddress address, int port) : base(address, port)
		{
		}

		public RawClient(string address, int port) : base(address, port)
		{
		}

		protected override Socket CreateSocket()
		{
			var socket = base.CreateSocket();

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				socket.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);

			socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);

			socket.SendBufferSize = socket.ReceiveBufferSize = 13500000;
			return socket;
		}

		protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
		{
			_Args.EndPoint = endpoint as IPEndPoint;
			_Args.Buffer = buffer;
			_Args.Size = size;
			_Args.Offset = offset;

			OnMessageReceived?.Invoke(_Args);
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
