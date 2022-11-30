using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Adapters.Raw
{
	class RawServerAdapter : IServerAdapter
	{
		public OnMessageReceived OnMessageReceived
		{
			get => _OnUdpMessageReceived;
			set
			{
				_OnUdpMessageReceived = value;

				if (_RawServer != null)
					_RawServer.OnMessageReceived = value;
			}
		}

		OnMessageReceived _OnUdpMessageReceived;
		RawServer _RawServer;

		public void Initialize(string address, int port)
		{
			_RawServer = new RawServer(address, port);
		}

		public void Start()
		{
			if (_RawServer == null)
				throw new InvalidOperationException("Raw server hasnt been initialized yet");

			_RawServer.Start();
		}

		public void Send(byte[] buffer, long offset, long length)
		{
			if (_RawServer == null)
				throw new InvalidOperationException("Raw server hasnt been initialized yet");

			_RawServer.Send(buffer, offset, length);
		}

		public void Send(EndPoint endpoint, byte[] buffer, long offset, long length)
		{
			if (_RawServer == null)
				throw new InvalidOperationException("Raw server hasnt been initialized yet");

			_RawServer.SendAsync(endpoint, buffer, offset, length);
		}

		public void ReceiveAsync()
		{
			if (_RawServer == null)
				throw new InvalidOperationException("Raw server hasnt been initialized yet");

			_RawServer.ReceiveAsync();
		}

		public void Dispose()
		{
			_RawServer?.Dispose();
		}
	}
}
