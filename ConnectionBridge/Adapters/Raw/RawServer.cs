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
	internal class RawServer : UdpServer
	{
		public OnMessageReceived OnMessageReceived;

		readonly MessageReceivedArgs _Args = new();

		const int SIO_UDP_CONNRESET = -1744830452;


		public RawServer(IPEndPoint endpoint) : base(endpoint)
		{
		}

		public RawServer(IPAddress address, int port) : base(address, port)
		{
		}

		public RawServer(string address, int port) : base(address, port)
		{
		}

		protected override Socket CreateSocket()
		{
			var socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Raw);

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				socket.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);

			socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);

			socket.SendBufferSize = socket.ReceiveBufferSize = 13500000;
			return socket;
		}

		protected void OnReceived(EndPoint endpoint, IPPacketInformation packetInfo, byte[] buffer, long offset, long size)
		{
			_Args.PacketInformation = packetInfo;
			_Args.EndPoint = endpoint as IPEndPoint;
			_Args.Buffer = buffer;
			_Args.Size = size;
			_Args.Offset = offset;

			OnMessageReceived?.Invoke(_Args);
		}

		public override void ReceiveAsync()
		{
			TryLowerLevelReceive();
		}

		private void TryLowerLevelReceive()
		{
			if (_receiving)
				return;

			if (!IsStarted)
				return;

			try
			{
				// Async receive with the receive handler
				_receiving = true;
				_receiveEventArg.RemoteEndPoint = _receiveEndpoint;
				_receiveEventArg.SetBuffer(_receiveBuffer.Data, 0, (int)_receiveBuffer.Capacity);
				if (!Socket.ReceiveMessageFromAsync(_receiveEventArg))
					ProcessReceiveFrom(_receiveEventArg);
			}
			catch (ObjectDisposedException) { }
		}

		protected override void ProcessReceiveFrom(SocketAsyncEventArgs e)
		{
			_receiving = false;

			if (!IsStarted)
				return;

			// Check for error
			if (e.SocketError != SocketError.Success)
			{
				SendError(e.SocketError);

				// Call the datagram received zero handler
				OnReceived(e.RemoteEndPoint, e.ReceiveMessageFromPacketInfo, _receiveBuffer.Data, 0, 0);

				return;
			}

			// Received some data from the client
			long size = e.BytesTransferred;

			// Update statistic
			DatagramsReceived++;
			BytesReceived += size;

			// Call the datagram received handler
			OnReceived(e.RemoteEndPoint, _receiveBuffer.Data, 0, size);

			// If the receive buffer is full increase its size
			if (_receiveBuffer.Capacity == size)
			{
				// Check the receive buffer limit
				if (((2 * size) > OptionReceiveBufferLimit) && (OptionReceiveBufferLimit > 0))
				{
					SendError(SocketError.NoBufferSpaceAvailable);

					// Call the datagram received zero handler
					OnReceived(e.RemoteEndPoint, _receiveBuffer.Data, 0, 0);

					return;
				}

				_receiveBuffer.Reserve(2 * size);
			}
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
