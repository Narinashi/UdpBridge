using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using ConnectionBridge.Messages;

namespace ConnectionBridge
{
	class ConnectionBridge : IDisposable
	{
		public bool IsAuthenticated { get; private set; }

		readonly UdpServer _LocalUdpServer;
		readonly UdpClient _RemoteUdpServer;

		readonly SecureChannel _SecureChannel;

		readonly MessageDeserializer _Deserializer;
		readonly MessageSerializer _Serializer;

		readonly Timer _Timer;

		readonly bool _ServerMode;

		Guid _SessionKey;
		Guid _SessionIdentifier;
		Guid _SessionDummy;

		readonly string _LocalUdpServerAddress;
		readonly string _RemoteUdpServerAddress;

		readonly int _LocalUdpServerPort;
		readonly int _RemoteUdpServerPort;

		byte[] _SessionKeyByteArray;
		byte[] _SessionIdentifierByteArray;
		byte[] _SessionDummyByteArray;

		DateTime _LatestHeartbeat;

		EndPoint _SourceEndpoint;

		public bool Disposed { get; private set; }

		const int _MessageBufferSize = 4096;
		const int HeartBeatInterval = 30000;
		const int ConnectionTimeout = HeartBeatInterval * 2;

		public ConnectionBridge(SecureChannel channel, string localAddress, int localPort, string remoteAddress, int remotePort, bool serverMode = false)
		{
			_SecureChannel = channel ?? throw new ArgumentNullException(nameof(channel));

			_ServerMode = serverMode;

			if (_SecureChannel.PeerEndPoint is null)
				throw new ArgumentException("TCP Connection hasnt been initiated yet");

			_Deserializer = new MessageDeserializer(_SecureChannel, _MessageBufferSize);
			_Serializer = new MessageSerializer(_SecureChannel);

			_LocalUdpServerAddress = localAddress;
			_LocalUdpServerPort = localPort;

			_RemoteUdpServerAddress = remoteAddress;
			_RemoteUdpServerPort = remotePort;

			_LocalUdpServer = new UdpServer(_LocalUdpServerAddress, _LocalUdpServerPort);
			_RemoteUdpServer = new UdpClient(_RemoteUdpServerAddress, _RemoteUdpServerPort);

			_LocalUdpServer.OnUdpMessageReceived = OnLocalUdpServerMessageReceived;
			_RemoteUdpServer.OnUdpMessageReceived = OnRemoteUdpServerMessageReceived;

			_LatestHeartbeat = DateTime.Now;

			_Timer = new Timer 
			{ 
				Interval = 10000,
			};

			_Timer.Elapsed += TimerElapsed;
			_Timer.Start();

			_Deserializer.OnMessageReceived = (message) =>
			{
				if (message is HelloMessage hello)
				{
					Logger.Debug(() => $"{(_ServerMode ? "Server: " : "Client: ")}Hello message received from peer with message of {hello.Message}");

					if (_ServerMode)
						_Serializer.Send(new HelloMessage { Message = hello.Message });

					if (Disposed)
						return;

					IsAuthenticated = true;

					_LocalUdpServer.Start();
					_RemoteUdpServer.Connect();

					_LocalUdpServer.ReceiveAsync();
					_RemoteUdpServer.ReceiveAsync();
				}
				else if (message is KeyExchangeMessage keyExchange)
				{
					Logger.Debug(() => $"{(_ServerMode ? "Server: " : "Client: ")}KeyExchange message Received consisting of " +
								$"{nameof(KeyExchangeMessage.Identifier)}:{keyExchange.Identifier}," +
								$" {nameof(KeyExchangeMessage.Key)}:{keyExchange.Key}, " +
								$"{nameof(KeyExchangeMessage.Dummy)}:{keyExchange.Dummy}");


					_SessionDummy = keyExchange.Dummy;
					_SessionIdentifier = keyExchange.Identifier;
					_SessionKey = keyExchange.Key;

					_SessionDummyByteArray = _SessionDummy.ToByteArray();
					_SessionIdentifierByteArray = _SessionIdentifier.ToByteArray();
					_SessionKeyByteArray = _SessionKey.ToByteArray();
				}
				else if(message is PingMessage pingMessage)
				{
					Logger.Info(() => $"Ping message received from {_SecureChannel.PeerEndPoint}");
					_LatestHeartbeat = DateTime.Now;
				}
			};
		}

		public async Task Handshake()
		{
			if (_ServerMode)
				throw new InvalidOperationException("Handshake cant be used in server mode");

			await _Serializer.Send(new HelloMessage { Message = "Hello !" });

			_SessionDummy = Guid.NewGuid();
			_SessionIdentifier = Guid.NewGuid();
			_SessionKey = Guid.NewGuid();

			_SessionDummyByteArray = _SessionDummy.ToByteArray();
			_SessionIdentifierByteArray = _SessionIdentifier.ToByteArray();
			_SessionKeyByteArray = _SessionKey.ToByteArray();


			await _Serializer.Send(new KeyExchangeMessage
			{
				Dummy = _SessionDummy,
				Identifier = _SessionIdentifier,
				Key = _SessionKey,
			});
		}

		private void OnLocalUdpServerMessageReceived(UdpMessageReceivedArgs message)
		{
			if (_SessionIdentifierByteArray == null)
			{
				Logger.Error(() => $"{(_ServerMode ? "Server: " : "Client: ")}Received message from local while session identifier is null, EP:{message.EndPoint}");
				return;
			}
			
			Logger.Debug(() => $"Local Message Received from {message.EndPoint}");

			_SourceEndpoint = message.EndPoint;
			ApplyXoR(message.Buffer, message.Offset, message.Size);
			_RemoteUdpServer.SendAsync(message.Buffer, message.Offset, message.Size);

			_LocalUdpServer.ReceiveAsync();
		}

		private void OnRemoteUdpServerMessageReceived(UdpMessageReceivedArgs message)
		{
			if (_SessionIdentifierByteArray == null)
			{
				Logger.Error(() => $"{(_ServerMode ? "Server: " : "Client: ")}Received message from {(_ServerMode ? "local" : "remote")} while session identifier is null, EP:{message.EndPoint}");
				return;
			}

			if(_SourceEndpoint == null)
			{
				Logger.Warning(() => $"Source endpoint is null");
				return;
			}

			Logger.Debug(() => $"Remote Message Received from {message.EndPoint}");

			ApplyXoR(message.Buffer, message.Offset, message.Size);
			_LocalUdpServer.SendAsync(_SourceEndpoint, message.Buffer, message.Offset, message.Size);

			_RemoteUdpServer.ReceiveAsync();
		}

		private void ApplyXoR(byte[] buffer, long offset, long length)
		{
			for (int i = 0; i + 16 < length; offset +=16, i+=16)
			{
				buffer[offset + 0] ^= _SessionKeyByteArray[0];
				buffer[offset + 1] ^= _SessionKeyByteArray[1];
				buffer[offset + 2] ^= _SessionKeyByteArray[2];
				buffer[offset + 3] ^= _SessionKeyByteArray[3];
				buffer[offset + 4] ^= _SessionKeyByteArray[4];
				buffer[offset + 5] ^= _SessionKeyByteArray[5];
				buffer[offset + 6] ^= _SessionKeyByteArray[6];
				buffer[offset + 7] ^= _SessionKeyByteArray[7];
				buffer[offset + 8] ^= _SessionKeyByteArray[8];
				buffer[offset + 9] ^= _SessionKeyByteArray[9];
				buffer[offset + 10] ^= _SessionKeyByteArray[10];
				buffer[offset + 11] ^= _SessionKeyByteArray[11];
				buffer[offset + 12] ^= _SessionKeyByteArray[12];
				buffer[offset + 13] ^= _SessionKeyByteArray[13];
				buffer[offset + 14] ^= _SessionKeyByteArray[14];
				buffer[offset + 15] ^= _SessionKeyByteArray[15];
			}
		}

		private async void TimerElapsed(object sender, ElapsedEventArgs e)
		{
			_Timer.Stop();
			_Timer.Interval = HeartBeatInterval;
			if (_ServerMode)
			{
				if (_LatestHeartbeat.AddMilliseconds(ConnectionTimeout) < DateTime.Now)
				{
					Logger.Info(() => $"Disconnecting peer {_SecureChannel.PeerEndPoint} since its been more than {ConnectionTimeout / 1000} seconds than the latest ping ({_LatestHeartbeat.TimeOfDay})");
					Dispose();
					return;
				}

				if (!IsAuthenticated)
				{
					Logger.Info(() => $"Disconnecting peer {_SecureChannel.PeerEndPoint} since its not authenticated in time");
					Dispose();
					return;
				}
			}
			else
			{
				await _Serializer.Send(new PingMessage());
			}

			_Timer.Start();
		}

		public void Dispose()
		{
			if (Disposed)
				return;

			_LocalUdpServer.Dispose();
			_RemoteUdpServer.Dispose();
			_SecureChannel.Dispose();
			_Timer.Dispose();

			Disposed = true;
		}
	}
}
