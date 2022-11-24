using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge
{
	internal class UdpServer : NetCoreServer.UdpServer
	{
		public OnUdpMessageReceived OnUdpMessageReceived;

		readonly UdpMessageReceivedArgs _Args = new();

		const int SIO_UDP_CONNRESET = -1744830452;

		public UdpServer(IPEndPoint endpoint) : base(endpoint)
		{
		}

		public UdpServer(IPAddress address, int port) : base(address, port)
		{
		}

		public UdpServer(string address, int port) : base(address, port)
		{
		}

		protected override Socket CreateSocket()
		{
			var socket = base.CreateSocket();

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				socket.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);

			socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);

			socket.SendBufferSize = socket.ReceiveBufferSize = 135000;
			return socket;
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

		protected override void OnStarting()
		{
			Logger.Info(() =>$"Udp server Starting...");
		}

		protected override void OnStarted()
		{
			Logger.Info(() => $"Udp server Started...");
		}
	}
}
