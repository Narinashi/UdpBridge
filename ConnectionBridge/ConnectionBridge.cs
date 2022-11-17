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
		public bool IsAuthenticated { get; private set; }

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

		long _SessionKeyFirstHalf;
		long _SessionKeySecondHalf;

		byte[] _ObfuscatedBuffer;
		byte[] _PlainBuffer;

		int _SendCounts;

		public bool Disposed { get; private set; }

		const int _MessageBufferSize = 4096;
		const int MTUSize = 1420;

		static int[] HandshakePacketSizes = new int[] { 148, 84, 92 };

		const int ObfuscatedPacketSign = 139;

		public ConnectionBridge(SecureChannel channel, string localAddress, int localPort, string remoteAddress, int remotePort, bool serverMode = false)
		{
			_SecureChannel = channel ?? throw new ArgumentNullException(nameof(channel));

			_ServerMode = serverMode;

			if (_SecureChannel.PeerEndPoint is null)
				throw new ArgumentException("TCP Connection hasnt been initiated yet");
			
			InitializeUdpBuffer();

			_Deserializer = new MessageDeserializer(_SecureChannel, _MessageBufferSize);
			_Serializer = new MessageSerializer(_SecureChannel);

			_LocalUdpServerAddress = localAddress;
			_LocalUdpServerPort = localPort;

			_RemoteUdpServerAddress = remoteAddress;
			_RemoteUdpServerPort = remotePort;

			_SendCounts = (int)(DateTime.Now.Ticks % _ObfuscatedBuffer.Length);

			_Deserializer.OnMessageReceived = (message) =>
			{
				if (message is HelloMessage hello)
				{
					Logger.Debug($"{(_ServerMode ? "Server: " : "Client: ")}Hello message received from peer with message of {hello.Message}");

					if (_ServerMode)
						_Serializer.Send(new HelloMessage { Message = hello.Message });
					
					if (Disposed)
						return;

					IsAuthenticated = true;

					InitLocalUdpServer();
					InitRemoteUdpServer();

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

					Buffer.BlockCopy(_SessionIdentifierByteArray, 0, _ObfuscatedBuffer, 1, _SessionIdentifierByteArray.Length);
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

			_SessionKeyFirstHalf = BitConverter.ToInt64(_SessionKeyByteArray, 0);
			_SessionKeySecondHalf = BitConverter.ToInt64(_SessionKeyByteArray, 8);

			Buffer.BlockCopy(_SessionIdentifierByteArray, 0, _ObfuscatedBuffer, 1, _SessionIdentifierByteArray.Length);

			await _Serializer.Send(new KeyExchangeMessage
			{
				Dummy = _SessionDummy,
				Identifier = _SessionIdentifier,
				Key = _SessionKey,
			});
		}

		private void InitializeUdpBuffer()
		{
			_ObfuscatedBuffer = Enumerable
									.Range(0, 1500)
									.Select(x => (byte)(x % 128))
									.ToArray();

			_ObfuscatedBuffer[0] = ObfuscatedPacketSign;

			_PlainBuffer = Enumerable
									.Range(0, 1500)
									.Select(x => (byte)(x % 128))
									.ToArray();
		}

		private void InitLocalUdpServer()
		{
			if (Disposed)
				return;

			Logger.Debug("Initiating local udp server ...");

			if (_LocalUdpServer != null)
			{
				_LocalUdpServer.OnDisconnected = null;
				_LocalUdpServer.Dispose();
			}

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
				DecryptAndRedirect(_LocalUdpServer, _RemoteUdpServer, message);		
			}
			else
			{
				EncryptAndRedirect(_RemoteUdpServer, message);
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
				EncryptAndRedirect(_LocalUdpServer, message);
			}
			else
			{
				DecryptAndRedirect(_RemoteUdpServer, _LocalUdpServer, message);
			}
		}

		private void EncryptAndRedirect(UdpServer sendingServer, UdpMessageReceivedArgs message)
		{
			Logger.Debug($"Server: Received packet from application({message.EndPoint}), going to send it to {sendingServer.RemoteEndPoint}");

			if (_ObfuscatedBuffer == null)
			{
				Logger.Warning($"Udp Send buffer is null, (did we handshake yet?)");
				return;
			}

			var lengthByteArray = BitConverter.GetBytes((short)message.Buffer.Length);

			if (HandshakePacketSizes.Contains(message.Buffer.Length))
			{
				if (message.Buffer.Length + 19 > _ObfuscatedBuffer.Length)
				{
					Logger.Warning($"Application sent a message with a size that is bigger than send buffer, size:{message.Buffer.Length}");
					return;
				}

				Logger.Debug($"Sending obfuscated packet");

				//message.Buffer = ApplyXoR(message.Buffer, message.Buffer.Length);

				Buffer.BlockCopy(lengthByteArray, 0, _ObfuscatedBuffer, 17, lengthByteArray.Length);

				Buffer.BlockCopy(message.Buffer, 0, _ObfuscatedBuffer, 19, message.Buffer.Length);
				
				var remainingBytesCount = MTUSize - message.Buffer.Length - 19;

				sendingServer.SendBack(_ObfuscatedBuffer,
									message.Buffer.Length + (remainingBytesCount == 0 ? 0 : (_SendCounts % remainingBytesCount)) + 19);
			}
			else
			{
				Logger.Debug($"Sending packet as plain");

				_ObfuscatedBuffer[0] = (byte)(_SendCounts % ObfuscatedPacketSign);

				Buffer.BlockCopy(lengthByteArray, 0, _PlainBuffer, 1, lengthByteArray.Length);

				Buffer.BlockCopy(message.Buffer, 0, _PlainBuffer, 3, message.Buffer.Length);

				var remainingBytesCount = MTUSize - message.Buffer.Length - 1;

				sendingServer.SendBack(_PlainBuffer,
							message.Buffer.Length + (remainingBytesCount == 0 ? 0 : (_SendCounts % remainingBytesCount)) + 3);
			}
			
			_SendCounts++;
		}

		private void DecryptAndRedirect(UdpServer fromUdpServer, UdpServer toUdpServer, UdpMessageReceivedArgs message)
		{
			if (message.Buffer.Length < 19)
			{
				Logger.Warning($"{(_ServerMode ? "Server: " : "Client: ")}Invalid packet from " +
					$"client:{message.EndPoint} with size of {message.Buffer.Length}");

				return;
			}

			Logger.Debug($"{(_ServerMode ? "Server: " : "Client: ")}Received packet from remote({message.EndPoint}), going to send it a local application at {toUdpServer.RemoteEndPoint}");
			
			if (message.Buffer[0] == ObfuscatedPacketSign)
			{
				Logger.Debug("Receving obfuscated packet");

				var identifier = message.Buffer.Skip(1).Take(16).ToArray();

				if (!identifier.SequenceEqual(_SessionIdentifierByteArray))
				{
					Logger.Warning($"{(_ServerMode ? "Server: " : "Client: ")}Invalid session identifier from " +
						$"client:{message.EndPoint} with expected identifer of {_SessionIdentifier}, " +
						$"got:{(identifier.Length == 16 ? new Guid(identifier) : Encoding.ASCII.GetString(identifier))}");

					return;
				}

				if (message.EndPoint.Address != fromUdpServer.RemoteEndPoint.Address &&
					message.EndPoint.Port != fromUdpServer.RemoteEndPoint.Port)
				{
					Logger.Warning($"{(_ServerMode ? "Server: " : "Client: ")}Reseting peer endpoint from {_LocalUdpServer.RemoteEndPoint} to {message.EndPoint}");
					fromUdpServer.ResetPeer();
				}

				var actualPacketLength = BitConverter.ToInt16(message.Buffer, 17);

				Logger.Debug($"{(_ServerMode ? "Server: " : "Client: ")}Packet size:{message.Buffer.Length}({message.Buffer.Length - 19}), actual packetSize:{actualPacketLength}");

				if (actualPacketLength > message.Buffer.Length - 19)
				{
					Logger.Warning($"Mismatch between actual packet size and length parameter," +
						$" LengthParameter:{actualPacketLength}, PacketSize(excluding headers):{message.Buffer.Length}");

					return;
				}


				//toUdpServer.SendBack(ApplyXoR(message.Buffer.Skip(19).ToArray(), actualPacketLength), actualPacketLength);
				toUdpServer.SendBack(message.Buffer.Skip(19).ToArray(), actualPacketLength);
			}
			else
			{
				Logger.Debug("Receving plain packet");

				var actualPacketLength = BitConverter.ToInt16(message.Buffer, 1);

				if (actualPacketLength > message.Buffer.Length - 3)
				{
					Logger.Warning($"Mismatch between actual packet size and length parameter," +
						$" LengthParameter:{actualPacketLength}, PacketSize(excluding headers):{message.Buffer.Length}");

					return;
				}

				toUdpServer.SendBack(message.Buffer.Skip(3).ToArray(), actualPacketLength);
			}
		}

		unsafe byte[] ApplyXoR(byte[] buffer, int length)
		{
			// First XOR as many 64-bit blocks as possible, for the sake of speed
			fixed (byte* bufferPointer = buffer)
			{
				long* longBufferPointer = (long*)bufferPointer;

				int chunks = length/ 8;

				for (int p = 0; p < chunks; p++)
				{
					*longBufferPointer ^= _SessionKeyFirstHalf;

					longBufferPointer++;
				}

				// Now cover any remaining bytes one byte at a time. We've
				// already handled chunks * 8 bytes, so start there.
				//for (int index = 0; chunks * 8 + index < buffer.Length; index++)
				//{
				//	buffer[chunks * 8 + index] ^= _SessionKeyByteArray[index];
				//}
			}

			return buffer;
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
