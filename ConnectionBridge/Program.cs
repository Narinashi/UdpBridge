using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using System.Net;
using System.Net.Sockets;

using System.Security.Cryptography.X509Certificates;

using System.Reflection;

namespace ConnectionBridge
{
	class Program
	{
		static ConnectionBridge _ConnectionBridge;
		static bool _ServerMode;
		static async Task Main(string[] args)
		{
			if (args?.Any() ?? false)
			{
				_ServerMode = bool.Parse(args[0]);

				if (_ServerMode)
				{
					await StartServerMode(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]), args[5], int.Parse(args[6]));
				}
				else
				{
					await StartClientMode(args[1], int.Parse(args[2]), args[3], int.Parse(args[4]), args[5], int.Parse(args[6]));
					while (Console.ReadKey().Key != ConsoleKey.Q) ;
				}
			}
			else if (args?.Length == 0)
			{
				Logger.Info("In order to run the program as server mode use these params");
				Logger.Info(GetHowToRunMessage(true));

				Logger.Info("In order to run the program as client mode use these params");
				Logger.Info(GetHowToRunMessage(false));
			}
			else
			{
				Console.WriteLine("Server mode ? (Y/N)");
				_ServerMode = Console.ReadKey().Key == ConsoleKey.Y;

				if (_ServerMode)
				{
					await StartServerMode(PromptStringParameter("sslCertificateFileAddress"),
											PromptStringParameter("sslCertificatePassword"),
											PromptIntParameter("sslServerListeningPort"),
											PromptIntParameter("udpServerLocalPort"),
											PromptStringParameter("udpServerRemoteAddress"),
											PromptIntParameter("udpServerRemotePort")
											);
				}
				else
				{
					await StartClientMode(PromptStringParameter("sslServerAddress"),
											PromptIntParameter("sslServerPort"),
											PromptStringParameter("trustedHostName"),
											PromptIntParameter("udpServerLocalPort"),
											PromptStringParameter("udpServerRemoteAddress"),
											PromptIntParameter("udpServerRemotePort")
											);

					while (Console.ReadKey().Key != ConsoleKey.Q) ;
				}
			}

#if DEBUG
			var testUdp = new UdpClient();
			var testudp2 = new UdpClient(9999);

			var buffer = Encoding.ASCII.GetBytes("010203040506070809101112131415161718192021222324252627282930");
			var ep = new IPEndPoint(IPAddress.Any, 0);

			while (true)
			{
				Console.WriteLine("local app on client sends to server");
				testUdp.Send(buffer, buffer.Length, new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1111));

				var recevingBuffer = testudp2.Receive(ref ep);

				Console.WriteLine("local app on server receives");
				Console.WriteLine("local app on server wants to send to client");

				testudp2.Send(recevingBuffer, recevingBuffer.Length, ep);

				var sendingBuffer = testUdp.Receive(ref ep);
				Console.WriteLine("local app on client receives from server");

				Console.ReadLine();
			}
#endif
		}


		static async Task StartClientMode(string	sslServerAddress,
											int		sslServerPort,
											string	trustedHostName,
											int		udpServerLocalPort,
											string	udpServerRemoteAddress,
											int		udpServerRemotePort)
		{
			var secureChannel = await SecureChannel.ConnectTo(sslServerAddress, sslServerPort, trustedHostName, 4096);
			secureChannel.StartReceiving();
			await secureChannel.Authenticate();

			_ConnectionBridge = new ConnectionBridge(secureChannel,
													string.Empty,
													udpServerLocalPort,
													udpServerRemoteAddress,
													udpServerRemotePort);

			await _ConnectionBridge.Handshake();
		}




		static Task StartServerMode(string		sslCertificateFileAddress,
										string	sslCertificatePassword,
										int		sslServerListeningPort,
										int		udpServerLocalPort,
										string	udpServerRemoteAddress,
										int		udpServerRemotePort)
		{
			var cert = new X509Certificate2(sslCertificateFileAddress, sslCertificatePassword);
			var listener = new SecureChannelListener(sslServerListeningPort, cert);

			var listenerTask = listener.AcceptConnection();

			listener.OnConnectionReceived = (client) =>
			{
				client.StartReceiving();

				if (_ConnectionBridge != null)
					client.Dispose(); //shoosh whoever wants to connect unless the previous one disconnects

				client.OnClientDisconnected = () =>
				{
					_ConnectionBridge.Dispose();
					_ConnectionBridge = null;
				};

				_ConnectionBridge = new ConnectionBridge(client,
														string.Empty,
														udpServerLocalPort,
														udpServerRemoteAddress,
														udpServerRemotePort, true);
			};


			return listenerTask;
		}


		static string GetHowToRunMessage(bool serverMode)
		{
			MethodInfo method = typeof(Program).GetMethod(serverMode ? nameof(StartServerMode) : nameof(StartClientMode),
																BindingFlags.NonPublic | BindingFlags.Static);

			string howToRunMessage = string.Join(" ", method.GetParameters()
																	.Select(x => $"{x.Name} (Type:{x.ParameterType.Name})"));

			return $"ConnectionBridge.exe {serverMode} {howToRunMessage}";
		}

		static string PromptStringParameter(string paramName)
		{
			while (true)
			{
				Console.WriteLine($"{paramName}: ");

				string input = Console.ReadLine();

				if (!string.IsNullOrWhiteSpace(input))
					return input;

				Console.WriteLine("Invalid input");
			}
		}

		static int PromptIntParameter(string paramName)
		{
			while (true)
			{
				Console.WriteLine($"{paramName}: ");

				string input = Console.ReadLine();
				if (int.TryParse(input, out int result))
					return result;

				Console.WriteLine("Invalid input");
			}
		}
	}
}
