using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Net;
using System.Net.Sockets;

using System.Runtime.InteropServices;

namespace ConnectionBridge
{
	delegate void OnUdpMessageReceived(UdpMessageReceivedArgs args);
	delegate void OnUdpDisconnected();

	class UdpServer : IDisposable
	{
		public IPEndPoint RemoteEndPoint { get; private set; }

		public OnUdpDisconnected OnDisconnected;

		readonly UdpClient _Client;
		readonly AsyncCallback _EndReceiveCallback;
		readonly AsyncCallback _EndSendCallback;

		readonly UdpMessageReceivedArgs _MessageReceivedArgs;

		readonly Dictionary<IPAddress, OnUdpMessageReceived> _Listeners;

		readonly int _BindingPort;

		bool Disposed;

		const int SIO_UDP_CONNRESET = -1744830452;

		public UdpServer(int incomingPort)
		{
			_Client = new UdpClient(_BindingPort = incomingPort)
			{
				DontFragment = true,
			};

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				_Client.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);

			_Client.Client.ReceiveBufferSize = _Client.Client.SendBufferSize = 130000;

			_EndReceiveCallback = new AsyncCallback(EndReceieve);
			_EndSendCallback = new AsyncCallback(EndSend);
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

			try
			{
				_Client.BeginSend(buffer, length, _EndSendCallback, length);
			}
			catch (Exception ex)
			{
				Logger.Error($"An exception occured in SendBack UdpBridge, \r\n{ex}");
			}
		}

		public void Send(byte[] buffer, int length, IPEndPoint target)
		{
			try
			{
				_Client.BeginSend(buffer, length, target, _EndSendCallback, length);
			}
			catch (Exception ex)
			{
				Logger.Error($"An exception occured in Send UdpBridge, \r\n{ex}");
			}
		}

		private void EndSend(IAsyncResult asyncResult)
		{
			try
			{
				var bytesSent = _Client.EndSend(asyncResult);
				var expectedBytesSent = (int)asyncResult.AsyncState;

				if (bytesSent != expectedBytesSent)
					Logger.Warning($"Udp send message didnt send whole buffer expected:{expectedBytesSent}, sent:{bytesSent}");
			}
			catch (Exception ex)
			{
				if (Disposed)
				{
					Logger.Debug($"Udp server is alredy disposed, ignoring exception: \r\n{ex}");
					return;
				}

				var message = $"An exception occured in Send UdpBridge, \r\n{ex}";

				if (ex is ObjectDisposedException || ex is SocketException)
					Logger.Warning(message);
				else
					Logger.Error(message);

				if (!(_Client.Client?.Connected ?? false))
					OnDisconnected?.Invoke();
			}
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

				if (_Client.Available > _Client.Client.ReceiveBufferSize / 2)
					Logger.Warning($"Too many packets are being received, can't keep up, Available packets(size) to process:{_Client.Available}, BufferSize:{_Client.Client.ReceiveBufferSize}");

				if (_Listeners.TryGetValue(_MessageReceivedArgs.EndPoint.Address, out OnUdpMessageReceived del))
					del(_MessageReceivedArgs);
				else if (_Listeners.TryGetValue(IPAddress.Any, out OnUdpMessageReceived del2))
					del2(_MessageReceivedArgs);
				else
					Logger.Warning($"Packet received from {_MessageReceivedArgs.EndPoint} which doesnt have any listener registered for");

				_Client.BeginReceive(_EndReceiveCallback, null);
			}
			catch (Exception ex)
			{
				if (Disposed)
				{
					Logger.Debug($"Udp server is alredy disposed, ignoring exception: \r\n{ex}");
					return;
				}

				var message = $"An exception occured in EndReceive UdpBridge, \r\n{ex}";

				if (ex is ObjectDisposedException || ex is SocketException || ex is InvalidOperationException)
					Logger.Warning(message);
				else
					Logger.Error(message);

				if ((_Client.Client?.SafeHandle?.IsInvalid ?? true) ||
					(_Client.Client?.SafeHandle?.IsClosed ?? true))
					OnDisconnected?.Invoke();
				else
					_Client.BeginReceive(_EndReceiveCallback, null);
			}
		}

		public void Dispose()
		{
			if (Disposed)
				return;

			Disposed = true;

			_Client.Dispose();
			_Listeners.Clear();
		}
	}
}
