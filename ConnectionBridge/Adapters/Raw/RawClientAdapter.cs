using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Adapters.Raw
{
	class RawClientAdapter : IClientAdapter
	{
		public OnMessageReceived OnMessageReceived
		{
			get => _OnUdpMessageReceived;
			set
			{
				_OnUdpMessageReceived = value;

				if (_Client != null)
					_Client.OnMessageReceived = value;
			}
		}

		OnMessageReceived _OnUdpMessageReceived;
		RawClient _Client;

		public void Connect()
		{
			if (_Client == null)
				throw new InvalidOperationException("Client hasnt been intialized yet");

			_Client.Connect();
		}

		public void Initialize(string address, int port)
		{
			_Client = new RawClient(address, port)
			{
				OnMessageReceived = _OnUdpMessageReceived
			};
		}

		public void Send(byte[] buffer, long offset, long length)
		{
			if (_Client == null)
				throw new InvalidOperationException("Raw client hasnt been initialized yet");

			_Client.Send(buffer, offset, length);
		}

		public void Send(EndPoint endpoint, byte[] buffer, long offset, long length)
		{
			if (_Client == null)
				throw new InvalidOperationException("Raw client hasnt been initialized yet");

			_Client.Send(endpoint, buffer, offset, length);
		}

		public void ReceiveAsync()
		{
			if (_Client == null)
				throw new InvalidOperationException("Raw client hasnt been initialized yet");

			_Client.ReceiveAsync();
		}

		public void Dispose()
		{
			_Client?.Dispose();
		}
	}
}
