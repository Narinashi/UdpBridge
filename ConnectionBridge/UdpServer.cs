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
	class UdpServer : IDisposable
	{
		public IPEndPoint RemoteEndPoint { get; private set; }

		readonly UdpClient _Client;
		readonly AsyncCallback _EndReceiveCallback;

		readonly UdpMessageReceivedArgs _MessageReceivedArgs;

		readonly Dictionary<IPAddress, OnUdpMessageReceived> _Listeners;

		readonly int _BindingPort;

		public UdpServer(int incomingPort)
		{
			_Client = new UdpClient(_BindingPort = incomingPort)
			{
				DontFragment = true,
			};

			_EndReceiveCallback = new AsyncCallback(EndReceieve);
			_Listeners = new Dictionary<IPAddress, OnUdpMessageReceived>();
			_MessageReceivedArgs = new UdpMessageReceivedArgs();
		}

		public UdpServer(string remoteAddress, int remotePort) : this(0)
		{
			if (string.IsNullOrWhiteSpace(remoteAddress))
				throw new ArgumentException("remoteAddress cant be empty");

			if (remotePort < 1)
				throw new ArgumentException("Remote port number cant be less than 1");

			_Client.Connect(remoteAddress, remotePort);

			RemoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);
		}

		public void AddListener(IPAddress address, OnUdpMessageReceived @delegate)
		{
			if (_Listeners.ContainsKey(address))
				_Listeners[address] = @delegate;
			else
				_Listeners.Add(address, @delegate);
		}
		
		public void RemoveListener(IPAddress address)
		{
			_Listeners.Remove(address);
		}
		
		public void StartReceiving()
		{
			_Client.BeginReceive(_EndReceiveCallback, null);
		}

		public void SendBack(byte[] buffer, int length)
		{
			if (RemoteEndPoint == null)
				throw new InvalidOperationException("No target endpoint has been set (Or use a overload that accepts a IPEndPoint)");
			
			var sentBytes = _Client.Send(buffer, length);

			if (sentBytes != length)
				Logger.Warning($"UdpClient didnt sent whole buffer, expected:{length}, sent:{sentBytes}");
		}

		public void Send(byte[] buffer, int length, IPEndPoint target)
		{
			var sentBytes = _Client.Send(buffer, length, target);

			if (sentBytes != length)
				Logger.Warning($"UdpClient didnt sent whole buffer, expected:{length}, sent:{sentBytes}");
		}

		public void ResetPeer()
		{
			if (_BindingPort == 0)
				throw new InvalidOperationException("Can't reset peer while in remote mode");

			RemoteEndPoint = null;
		}

		private void EndReceieve(IAsyncResult result)
		{
			try
			{
				_MessageReceivedArgs.Buffer = _Client.EndReceive(result, ref _MessageReceivedArgs.EndPoint);

				if (RemoteEndPoint == null)
				{
					RemoteEndPoint = _MessageReceivedArgs.EndPoint;
					_Client.Connect(RemoteEndPoint);
				}

				if (_Listeners.TryGetValue(_MessageReceivedArgs.EndPoint.Address, out OnUdpMessageReceived del))
					del(_MessageReceivedArgs);
				else
					Logger.Warning($"Packet received from {_MessageReceivedArgs.EndPoint} which doesnt have any listener registered for");
			}
			catch (Exception ex)
			{
				Logger.Error($"An exception occured in EndReceive UdpBridge, \r\n{ex}");
			}

			_Client.BeginReceive(_EndReceiveCallback, null);
		}

		public void Dispose()
		{
			_Client.Dispose();
			_Listeners.Clear();
		}
	}
}
