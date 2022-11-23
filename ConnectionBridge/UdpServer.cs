﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge
{
	internal class UdpServer : NetCoreServer.UdpServer
	{
		public OnUdpMessageReceived OnUdpMessageReceived;

		readonly UdpMessageReceivedArgs _Args = new();

		public UdpServer(IPEndPoint endpoint) : base(endpoint)
		{
		}

		public UdpServer(IPAddress address, int port) : base(address, port)
		{
		}

		public UdpServer(string address, int port) : base(address, port)
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
	}
}
