using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConnectionBridge
{
	delegate void OnDataReceived(DataReceivedArgs args);
	delegate void OnClientDisconnected();

	class SecureChannel : IDisposable
	{
		public OnDataReceived OnDataReceived;
		public OnClientDisconnected OnClientDisconnected;

		public IPEndPoint PeerEndPoint { get; private set; }

		readonly SslStream _SecureStream;
		readonly TcpClient _Client;
		readonly X509Certificate _Certificate;
		readonly CancellationTokenSource _CancelationToken;

		readonly DataReceivedArgs _DataReceivedArgs;

		readonly bool _Server;
		readonly string _TargetHostMachineName;
		readonly byte[] _Buffer;

		readonly AsyncCallback _ReadAsyncCallback;

		bool Disposed = false;

		public static async Task<SecureChannel> ConnectTo(string address, int port, string targetHostMachineName, int bufferSize)
		{
			if (string.IsNullOrWhiteSpace(address))
				throw new ArgumentNullException(nameof(address));

			if (port < 1)
				throw new ArgumentException("Port number cant be less than 1");

			var client = new TcpClient();
			await client.ConnectAsync(address, port);

			var channel = new SecureChannel(client, targetHostMachineName, bufferSize);
			await channel.Authenticate();

			return channel;
		}

		private SecureChannel(TcpClient tcpClient, int bufferSize)
		{
			if (!tcpClient.Connected)
				throw new InvalidOperationException("Client is disconnected");

			if (bufferSize < 1)
				throw new ArgumentException("Buffer size cannot be less than 1");

			_Client = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
			PeerEndPoint = _Client.Client.RemoteEndPoint as IPEndPoint;
			_CancelationToken = new CancellationTokenSource();
			_Buffer = new byte[bufferSize];
			_DataReceivedArgs = new DataReceivedArgs(_Buffer);
			_ReadAsyncCallback = new AsyncCallback(EndSecureStreamRead);
		}

		public SecureChannel(TcpClient tcpClient, X509Certificate certificate, int bufferSize) : this(tcpClient, bufferSize)
		{
			if (certificate == null)
				throw new ArgumentNullException(nameof(certificate));

			if (certificate.Handle == IntPtr.Zero)
				throw new ArgumentException("Invalid certificate provided");

			_Certificate = certificate;
			_SecureStream = new SslStream(_Client.GetStream(), false);
			_Server = true;
		}

		public SecureChannel(TcpClient tcpClient, string targetHostMachineName, int bufferSize) : this(tcpClient, bufferSize)
		{
			if (string.IsNullOrWhiteSpace(targetHostMachineName))
				throw new ArgumentException("Invalid HostManchine name");

			_TargetHostMachineName = targetHostMachineName;
			_Server = false;
			_SecureStream = new SslStream(tcpClient.GetStream(),
											false,
											new RemoteCertificateValidationCallback(ValidateServerCertificate)
											);
		}

		public async Task<bool> Authenticate()
		{
			if (!_Client.Connected)
				throw new InvalidOperationException("Client is disconnected");

			try
			{
				if (_Server)
					await _SecureStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
					{
						ServerCertificate = _Certificate,
						EncryptionPolicy = EncryptionPolicy.AllowNoEncryption,
						EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls11,
					}, (new CancellationTokenSource(10000).Token));
				else
					await _SecureStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
					{
						EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls11,
						TargetHost = _TargetHostMachineName,
						RemoteCertificateValidationCallback = ValidateServerCertificate,
						EncryptionPolicy = EncryptionPolicy.AllowNoEncryption,
					}, (new CancellationTokenSource(10000).Token));

				return true;
			}
			catch (OperationCanceledException)
			{
				Dispose();
				OnClientDisconnected?.Invoke();
				return false;
			}
		}

		public async Task Send(byte[] buffer)
		{
			if (!_Client.Connected)
				throw new InvalidOperationException("Client is disconnected");
			try
			{
				await _SecureStream.WriteAsync(buffer, 0, buffer.Length, _CancelationToken.Token);
			}
			catch (ObjectDisposedException)
			{
				OnClientDisconnected?.Invoke();
				throw;
			}
			catch (IOException)
			{
				OnClientDisconnected?.Invoke();
				throw;
			}
		}

		public void StartReceiving()
		{
			_SecureStream.BeginRead(_Buffer, 0, _Buffer.Length, _ReadAsyncCallback, null);
		}

		private void EndSecureStreamRead(IAsyncResult asyncResult)
		{
			int readBytes;
			try
			{
				readBytes = _SecureStream.EndRead(asyncResult);
			}
			catch (Exception ex)
			{
				Logger.Error(() => $"An exception occured in {nameof(EndSecureStreamRead)}, {ex}");
				Dispose();
				OnClientDisconnected?.Invoke();
				return;
			}

			if (Disposed)
				return;

			_DataReceivedArgs.Length = readBytes;

			if (_CancelationToken.Token.IsCancellationRequested || !_Client.Connected || readBytes == 0)
			{
				Dispose();
				OnClientDisconnected?.Invoke();
				return;
			}

			OnDataReceived?.Invoke(_DataReceivedArgs);

			_SecureStream.BeginRead(_Buffer, 0, _Buffer.Length, _ReadAsyncCallback, null);
		}

		static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
		{
			if (sslPolicyErrors == SslPolicyErrors.None)
				return true;

			if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors && chain.ChainStatus.FirstOrDefault().Status == X509ChainStatusFlags.UntrustedRoot)
				return true;

			Logger.Error(() => $"Certificate error: {sslPolicyErrors}, " +
				$"chain statuses:{string.Join("\r\n", chain?.ChainStatus?.Select(x => $"{x.Status}:{x.StatusInformation}") ?? Array.Empty<string>())}");

			return false;
		}

		public void Dispose()
		{
			if (Disposed)
				return;

			_SecureStream.Dispose();
			_Client.Dispose();
			Disposed = true;
		}
	}
}
