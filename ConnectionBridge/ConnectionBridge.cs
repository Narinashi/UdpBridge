using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using ConnectionBridge.Adapters;
using ConnectionBridge.Messages;
using ConnectionBridge.Obfuscators;

namespace ConnectionBridge
{
	class ConnectionBridge : IDisposable
	{
		public bool IsAuthenticated { get; private set; }

		readonly IServerAdapter _LocalAdapter;
		readonly IClientAdapter _RemoteAdapter;

		readonly SecureChannel _SecureChannel;

		readonly MessageDeserializer _Deserializer;
		readonly MessageSerializer _Serializer;

		readonly IObfucator _Obfuscator;

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

			_LocalAdapter = new Adapters.Udp.UdpServerAdapter();
			_LocalAdapter.Initialize(_LocalUdpServerAddress, _LocalUdpServerPort);

			_RemoteAdapter = new Adapters.Udp.UdpClientAdapter();
			_RemoteAdapter.Initialize(_RemoteUdpServerAddress, _RemoteUdpServerPort);

			_LocalAdapter.OnMessageReceived = OnLocalAdapterMessageReceived;
			_RemoteAdapter.OnMessageReceived = OnRemoteAdapterMessageReceived;

			_Obfuscator = new Xor16BytesObfuscator();

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

					_LocalAdapter.Start();
					_RemoteAdapter.Connect();

					_LocalAdapter.ReceiveAsync();
					_RemoteAdapter.ReceiveAsync();
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
				else if (message is PingMessage pingMessage)
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

		private void OnLocalAdapterMessageReceived(MessageReceivedArgs message)
		{
			if (_SessionIdentifierByteArray == null)
			{
				Logger.Error(() => $"{(_ServerMode ? "Server: " : "Client: ")}Received message from local while session identifier is null, EP:{message.EndPoint}");
				return;
			}

			Logger.Debug(() => $"Local Message Received from {message.EndPoint}");

			_SourceEndpoint = message.EndPoint;

			var backupMessage = new MessageReceivedArgs(message);

			message = _ServerMode ? 
				_Obfuscator.Deobfuscate(message, _SessionKeyByteArray) :
				_Obfuscator.Obfuscate(message, _SessionKeyByteArray);

			var secondBackupMessaeg = new MessageReceivedArgs(message);

			secondBackupMessaeg = !_ServerMode ?
				_Obfuscator.Deobfuscate(message, _SessionKeyByteArray) :
				_Obfuscator.Obfuscate(message, _SessionKeyByteArray);

			if (!secondBackupMessaeg.Buffer.SequenceEqual(backupMessage.Buffer))
			{

			}

			_RemoteAdapter.Send(message.Buffer, message.Offset, message.Size);

			_LocalAdapter.ReceiveAsync();
		}

		private void OnRemoteAdapterMessageReceived(MessageReceivedArgs message)
		{
			if (_SessionIdentifierByteArray == null)
			{
				Logger.Error(() => $"{(_ServerMode ? "Server: " : "Client: ")}Received message from {(_ServerMode ? "local" : "remote")} while session identifier is null, EP:{message.EndPoint}");
				return;
			}

			if (_SourceEndpoint == null)
			{
				Logger.Warning(() => $"Source endpoint is null");
				return;
			}

			Logger.Debug(() => $"Remote Message Received from {message.EndPoint}");


			message = _ServerMode ?
				_Obfuscator.Obfuscate(message, _SessionKeyByteArray) :
				_Obfuscator.Deobfuscate(message, _SessionKeyByteArray);

			_LocalAdapter.Send(_SourceEndpoint, message.Buffer, message.Offset, message.Size);

			_RemoteAdapter.ReceiveAsync();
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

			_LocalAdapter.Dispose();
			_RemoteAdapter.Dispose();
			_SecureChannel.Dispose();
			_Timer.Dispose();

			Disposed = true;
		}
	}
}
