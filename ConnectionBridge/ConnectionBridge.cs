using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using ConnectionBridge.Messages;

namespace ConnectionBridge
{
	class ConnectionBridge : IDisposable
	{
		UdpServer _LocalUdpServer;
		UdpServer _RemoteUdpServer;
		readonly SecureChannel _SecureChannel;

		readonly MessageDeserializer _Deserializer;
		readonly MessageSerializer _Serializer;

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

		bool Disposed;

		const int _MessageBufferSize = 4096;

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

			InitLocalUdpServer();
			InitRemoteUdpServer();

			_Deserializer.OnMessageReceived = (message) =>
			{
				if (message is HelloMessage hello)
				{
					Logger.Debug($"{(_ServerMode ? "Server: " : "Client: ")}Hello message received from peer with message of {hello.Message}");

					if (_ServerMode)
						_Serializer.Send(new HelloMessage { Message = hello.Message });

					_LocalUdpServer.StartReceiving();
					_RemoteUdpServer.StartReceiving();
				}
				else if (message is KeyExchangeMessage keyExchange)
				{
					Logger.Debug($"{(_ServerMode ? "Server: " : "Client: ")}KeyExchange message Received consisting of " +
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

		private void InitLocalUdpServer()
		{
			if (Disposed)
				return;

			Logger.Debug("Initiating local udp server ...");

			if (_LocalUdpServer != null)
				_LocalUdpServer.OnDisconnected = null;

			_LocalUdpServer = string.IsNullOrWhiteSpace(_LocalUdpServerAddress) ?
								new UdpServer(_LocalUdpServerPort) :
								new UdpServer(_LocalUdpServerAddress, _LocalUdpServerPort);

			_LocalUdpServer.AddListener(_ServerMode ? _SecureChannel.PeerEndPoint.Address.MapToIPv4() : IPAddress.Any, OnLocalUdpServerMessageReceived);
			
			_LocalUdpServer.OnDisconnected= InitLocalUdpServer;
		}

		private void InitRemoteUdpServer()
		{
			if (Disposed)
				return;

			Logger.Debug("Initiating Remote udp server ...");

			if (_RemoteUdpServer != null)
			{
				_RemoteUdpServer.OnDisconnected = null;
				_RemoteUdpServer.Dispose();
			}

			_RemoteUdpServer = string.IsNullOrWhiteSpace(_RemoteUdpServerAddress) ?
								new UdpServer(_RemoteUdpServerPort) :
								new UdpServer(_RemoteUdpServerAddress, _RemoteUdpServerPort);

			_RemoteUdpServer.AddListener(_ServerMode ? IPAddress.Any : _SecureChannel.PeerEndPoint.Address.MapToIPv4(), OnRemoteUdpServerMessageReceived);
			_RemoteUdpServer.OnDisconnected = InitRemoteUdpServer;
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
				Logger.Debug($"Server: Received packet from a remote peer({message.EndPoint}), going to pipe it to {_RemoteUdpServer.RemoteEndPoint}");

				var identifier = message.Buffer.Take(16).ToArray();
				if (!identifier.SequenceEqual(_SessionIdentifierByteArray))
				{
					Logger.Warning($"{(_ServerMode ? "Server: " : "Client: ")}Invalid session identifier from " +
						$"client:{message.EndPoint} with expected identifer of {_SessionIdentifier}, " +
						$"got:{(identifier.Length == 16 ? new Guid(identifier) : Encoding.ASCII.GetString(identifier))}");

					return;
				}

				if(message.EndPoint.Address != _LocalUdpServer.RemoteEndPoint.Address && 
					message.EndPoint.Port != _LocalUdpServer.RemoteEndPoint.Port)
				{
					Logger.Warning($"{(_ServerMode ? "Server: " : "Client: ")}Reseting peer endpoint from {_LocalUdpServer.RemoteEndPoint} to {message.EndPoint} in localUdpServer");
					_LocalUdpServer.ResetPeer();
				}

				//skip the identifier part
				//add deobfuscation and such here (later)
				_RemoteUdpServer.SendBack(message.Buffer.Skip(16).ToArray(), message.Buffer.Length - 16);
			}
			else
			{
				Logger.Debug($"Client: Received packet from a local application({message.EndPoint}), going to pipe it to server at {_RemoteUdpServer.RemoteEndPoint}");

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
				Logger.Debug($"Server: Received packet from a local application({message.EndPoint}), going to send it to {_LocalUdpServer.RemoteEndPoint}");

				//add identifier part here
				//add obfuscatio and such here
				_LocalUdpServer.SendBack(_SessionIdentifierByteArray.Concat(message.Buffer).ToArray(), message.Buffer.Length + 16);
			}
			else
			{
				Logger.Debug($"Client: Received packet from the server({message.EndPoint}), going to send it a local application at {_LocalUdpServer.RemoteEndPoint}");

				var identifier = message.Buffer.Take(16).ToArray();

				if (!identifier.SequenceEqual(_SessionIdentifierByteArray))
				{
					Logger.Warning($"{(_ServerMode ? "Server: " : "Client: ")}Invalid session identifier from " +
						$"client:{message.EndPoint} with expected identifer of {_SessionIdentifier}, " +
						$"got:{(identifier.Length == 16 ? new Guid(identifier) : Encoding.ASCII.GetString(identifier))}");

					return;
				}

				if (message.EndPoint.Address != _RemoteUdpServer.RemoteEndPoint.Address &&
					message.EndPoint.Port != _RemoteUdpServer.RemoteEndPoint.Port)
				{
					Logger.Warning($"{(_ServerMode ? "Server: " : "Client: ")}Reseting peer endpoint from {_LocalUdpServer.RemoteEndPoint} to {message.EndPoint} in localUdpServer");
					_RemoteUdpServer.ResetPeer();
				}

				//skip the identifier part
				//add deobfuscation and such here (later)
				_LocalUdpServer.SendBack(message.Buffer.Skip(16).ToArray(), message.Buffer.Length - 16);
			}
		}

		public void Dispose()
		{
			if (Disposed)
				return;

			_LocalUdpServer.Dispose();
			_RemoteUdpServer.Dispose();
			_SecureChannel.Dispose();

			Disposed = true;
		}
	}
}
