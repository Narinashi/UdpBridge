using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge.Adapters
{
	delegate void OnMessageReceived(MessageReceivedArgs args);

	interface IAdapter : IDisposable
	{
		void Initialize(string address, ushort port);
		OnMessageReceived OnMessageReceived { get; set; }

		void ReceiveAsync();
	}

	interface IClientAdapter : IAdapter
	{
		void Connect();
		void Send(byte[] buffer, long offset, long length);
		void Send(IPEndPoint endpoint, byte[] buffer, long offset, long length);
	}

	interface IServerAdapter : IAdapter
	{
		void Start();
		void Send(byte[] buffer, long offset, long length);
		void Send(IPEndPoint endpoint, byte[] buffer, long offset, long length);
	}
}
