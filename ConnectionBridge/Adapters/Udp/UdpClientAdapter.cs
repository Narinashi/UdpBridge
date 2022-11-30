using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Adapters.Udp
{
	class UdpClientAdapter : IClientAdapter
	{
		public OnMessageReceived OnMessageReceived
		{
			get => _OnMessageReceived;
			set
			{
				_OnMessageReceived = value;

				if (_Client != null)
					_Client.OnMessageReceived = value;
			}
		}

		OnMessageReceived _OnMessageReceived;
		UdpClient _Client;

		public void Connect()
		{
			if (_Client == null)
				throw new InvalidOperationException("Client hasnt been intialized yet");

			_Client.Connect();
		}

		public void Initialize(string address, int port)
		{
			_Client = new UdpClient(address, port)
			{
				OnMessageReceived = _OnMessageReceived
			};
		}

		public void Send(byte[] buffer, long offset, long length)
		{
			if (_Client == null)
				throw new InvalidOperationException("Udp client hasnt been initialized yet");

			_Client.SendAsync(buffer, offset, length);
		}

		public void Send(EndPoint endpoint, byte[] buffer, long offset, long length)
		{
			if (_Client == null)
				throw new InvalidOperationException("Udp client hasnt been initialized yet");

			_Client.SendAsync(endpoint, buffer, offset, length);
		}

		public void ReceiveAsync()
		{
			if (_Client == null)
				throw new InvalidOperationException("Udp server hasnt been initialized yet");

			_Client.ReceiveAsync();
		}

		public void Dispose()
		{
			_Client?.Dispose();
		}
	}
}
