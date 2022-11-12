using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ConnectionBridge.Messages;

namespace ConnectionBridge
{
	class ConnectionBridge : IDisposable
	{
		readonly UdpServer _LocalUdpServer;
		readonly UdpServer _RemoteUdpServer;
		readonly SecureChannel _SecureChannel;

		readonly MessageDeserializer _Deserializer;
		readonly MessageSerializer _Serializer;

		readonly bool _ServerMode;

		Guid _SessionKey;
		Guid _SessionIdentifier;
		Guid _SessionDummy;

		byte[] _SessionKeyByteArray;
		byte[] _SessionIdentifierByteArray;
		byte[] _SessionDummyByteArray;

		const int _MessageBufferSize = 4096;

		public ConnectionBridge(SecureChannel channel, string localAddress, int localPort, string remoteAddress, int remotePort, bool serverMode = false)
		{
			_SecureChannel = channel ?? throw new ArgumentNullException(nameof(channel));

			if (_SecureChannel.PeerEndPoint is null)
				throw new ArgumentException("TCP Connection hasnt been initiated yet");

			_Deserializer = new MessageDeserializer(_SecureChannel, _MessageBufferSize);
			_Serializer = new MessageSerializer(_SecureChannel);

			_LocalUdpServer = string.IsNullOrWhiteSpace(localAddress) ?
								new UdpServer(localPort) :
								new UdpServer(localAddress, localPort);

			_RemoteUdpServer = string.IsNullOrWhiteSpace(remoteAddress) ?
								new UdpServer(remotePort) :
								new UdpServer(remoteAddress, remotePort);

			_LocalUdpServer.AddListener(_SecureChannel.PeerEndPoint.Address.MapToIPv4(), OnLocalUdpServerMessageReceived);
			_RemoteUdpServer.AddListener(_SecureChannel.PeerEndPoint.Address.MapToIPv4(), OnRemoteUdpServerMessageReceived);

			_Deserializer.OnMessageReceived = (message) =>
			{
				if (message is HelloMessage hello)
				{
					Logger.Info($"{(_ServerMode ? "Server: " : "Client: ")}Hello message received from peer with message of {hello.Message}");

					if (_ServerMode)
						_Serializer.Send(new HelloMessage { Message = hello.Message });

					_LocalUdpServer.StartReceiving();
					_RemoteUdpServer.StartReceiving();
				}
				else if (message is KeyExchangeMessage keyExchange)
				{
					Logger.Info($"{(_ServerMode ? "Server: " : "Client: ")}KeyExchange message Received consisting of " +
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
			};

			_ServerMode = serverMode;
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
				Logger.Error($"{(_ServerMode ? "Server: " : "Client: ")}Received message from local while session identifier is null, EP:{message.EndPoint}");
				return;
			}

			if (_ServerMode)
			{
				Logger.Info($"Server: Received packet from a remote peer, going to pipe it to {_RemoteUdpServer.RemoteEndPoint}");

				var identifier = message.Buffer.Take(16).ToArray();
				if (!identifier.SequenceEqual(_SessionIdentifierByteArray))
				{
					Logger.Warning($"{(_ServerMode ? "Server: " : "Client: ")}Invalid session identifier from " +
						$"client:{message.EndPoint} with expected identifer of {_SessionIdentifier}, " +
						$"got:{(identifier.Length == 16 ? new Guid(identifier) : Encoding.ASCII.GetString(identifier))}");

					return;
				}

				//skip the identifier part
				//add deobfuscation and such here (later)
				_RemoteUdpServer.SendBack(message.Buffer.Skip(16).ToArray(), message.Buffer.Length - 16);
			}
			else
			{
				Logger.Info($"Client: Received packet from a local application, going to pipe it to server at {_RemoteUdpServer.RemoteEndPoint}");

				//add identifier part here
				//add obfuscatio and such here
				_RemoteUdpServer.SendBack(_SessionIdentifierByteArray.Concat(message.Buffer).ToArray(), message.Buffer.Length + 16);
			}
		}
		private void OnRemoteUdpServerMessageReceived(UdpMessageReceivedArgs message)
		{
			if (_SessionIdentifierByteArray == null)
			{
				Logger.Error($"{(_ServerMode ? "Server: " : "Client: ")}Received message from {(_ServerMode ? "local" : "remote")} while session identifier is null, EP:{message.EndPoint}");
				return;
			}

			if (_ServerMode)
			{
				Logger.Info($"Server: Received packet from a local application, going to send it to {_LocalUdpServer.RemoteEndPoint}");

				//add identifier part here
				//add obfuscatio and such here
				_LocalUdpServer.SendBack(_SessionIdentifierByteArray.Concat(message.Buffer).ToArray(), message.Buffer.Length + 16);
			}
			else
			{
				Logger.Info($"Client: Received packet from the server, going to send it an local application at {_LocalUdpServer.RemoteEndPoint}");

				var identifier = message.Buffer.Take(16).ToArray();

				if (!identifier.SequenceEqual(_SessionIdentifierByteArray))
				{
					Logger.Warning($"{(_ServerMode ? "Server: " : "Client: ")}Invalid session identifier from " +
						$"client:{message.EndPoint} with expected identifer of {_SessionIdentifier}, " +
						$"got:{(identifier.Length == 16 ? new Guid(identifier) : Encoding.ASCII.GetString(identifier))}");

					return;
				}

				//skip the identifier part
				//add deobfuscation and such here (later)
				_LocalUdpServer.SendBack(message.Buffer.Skip(16).ToArray(), message.Buffer.Length - 16);
			}
		}

		public void Dispose()
		{
			_LocalUdpServer.Dispose();
			_RemoteUdpServer.Dispose();
			_SecureChannel.Dispose();
		}
	}
}
