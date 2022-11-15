using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using System.Diagnostics;

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
			if (args?.Length > 1)
			{
				_ServerMode = bool.Parse(args[0]);

				for (int i = 0; i < args.Length; i++)
					args[i] = args[i].Replace("\"", string.Empty);

				Logger.LogLevel = args.Length > 7 && Enum.TryParse(args[7], true, out LogLevel ll) ? ll : LogLevel.Info;

				if (_ServerMode)
				{
					await StartServerMode(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]), args[5], int.Parse(args[6]));
				}
				else
				{
					await StartClientMode(args[1], int.Parse(args[2]), args[3], int.Parse(args[4]), args[5], int.Parse(args[6]));
				}

				while (Console.ReadKey().Key != ConsoleKey.Q) ;
			}
			else if (args?.Length == 1)
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

				Logger.LogLevel = PromptEnumValue<LogLevel>("Logger log level");

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

				}
				while (Console.ReadKey().Key != ConsoleKey.Q) ;
			}

#if DEBUG
			var localAppOnClient = new UdpClient();
			var localAppOnServer = new UdpClient(9999);

			var buffer = Encoding.ASCII.GetBytes("010203040506070809101112131415161718192021222324252627282930");
			var ep = new IPEndPoint(IPAddress.Any, 0);

			while (true)
			{
				Console.WriteLine("local app on client sends to server");
				localAppOnClient.Send(buffer, buffer.Length, new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1111));

				var recevingBuffer = localAppOnClient.Receive(ref ep);

				Console.WriteLine("local app on server receives");
				Console.WriteLine("local app on server wants to send to client");

				//localAppOnServer.Send(recevingBuffer, recevingBuffer.Length, ep);

				//var sendingBuffer = localAppOnClient.Receive(ref ep);

				//Debug.Assert(recevingBuffer.SequenceEqual(sendingBuffer));

				Console.WriteLine("local app on client receives from server");

				Console.ReadLine();
			}
#endif
		}


		static async Task StartClientMode(string sslServerAddress,
											int sslServerPort,
											string trustedHostName,
											int udpServerLocalPort,
											string udpServerRemoteAddress,
											int udpServerRemotePort)
		{
			try
			{
				Logger.Info($"Initiating in ClientMode ...");

				var secureChannel = await SecureChannel.ConnectTo(sslServerAddress, sslServerPort, trustedHostName, 4096);
				secureChannel.StartReceiving();

				Logger.Info($"Initiating ConnectionBridge ...");

				_ConnectionBridge = new ConnectionBridge(secureChannel,
														string.Empty,
														udpServerLocalPort,
														udpServerRemoteAddress,
														udpServerRemotePort);

				await _ConnectionBridge.Handshake();

				Logger.Info($"ConnectionBridge Handshake complete");
			}
			catch(Exception ex)
			{
				Logger.Error($"An exception occurred while trying to connect to server, \r\n{ex}");
				await StartClientMode(sslServerAddress, 
										sslServerPort,
										trustedHostName, 
										udpServerLocalPort,
										udpServerRemoteAddress, 
										udpServerRemotePort);
			}
		}




		static async Task StartServerMode(string sslCertificateFileAddress,
										string sslCertificatePassword,
										int sslServerListeningPort,
										int udpServerLocalPort,
										string udpServerRemoteAddress,
										int udpServerRemotePort)
		{
			Logger.Info($"Initiating in ServerMode ...");

			var cert = new X509Certificate2(sslCertificateFileAddress, sslCertificatePassword);
			var listener = new SecureChannelListener(sslServerListeningPort);

			while (true)
			{
				try
				{
					Logger.Debug($"Awaiting new connection");

					TcpClient client = listener.AcceptConnection();

					client.ReceiveTimeout = client.SendTimeout = 400000;

					Logger.Info($"New TCP Connection from {client.Client?.RemoteEndPoint}");

					if (_ConnectionBridge != null)
					{
						Logger.Debug("Connection bridge already instatiated, going to dispose the coming connection");
						client.Dispose();

						continue;
					}

					SecureChannel secureChannel = new(client, cert, 4096);
					await secureChannel.Authenticate();
					secureChannel.StartReceiving();

					_ConnectionBridge = new ConnectionBridge(secureChannel,
																string.Empty,
																udpServerLocalPort,
																udpServerRemoteAddress,
																udpServerRemotePort, true);

					secureChannel.OnClientDisconnected = () =>
					{
						Logger.Debug($"Connection {secureChannel.PeerEndPoint} has been disconnected, disposing secure channel and awaiting new connection");

						_ConnectionBridge.Dispose();
						_ConnectionBridge = null;
					};

				}
				catch (Exception ex)
				{
					Logger.Error($"an exception occured in listener connection acceptance loop \r\n{ex}");
				}
			}
		}


		static string GetHowToRunMessage(bool serverMode)
		{
			MethodInfo method = typeof(Program).GetMethod(serverMode ? nameof(StartServerMode) : nameof(StartClientMode),
																BindingFlags.NonPublic | BindingFlags.Static);

			string howToRunMessage = string.Join(" ", method.GetParameters()
																	.Select(x => $"{x.Name} (Type:{x.ParameterType.Name})"));

			return $"ConnectionBridge.exe {serverMode} {howToRunMessage} {{LoggerLevel(Default:info)}}";
		}

		static string PromptStringParameter(string paramName)
		{
			Console.WriteLine($"{paramName}: ");
			return Console.ReadLine();
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

		static T PromptEnumValue<T>(string paramName) where T : struct, Enum, IComparable, IConvertible
		{
			while (true)
			{
				Console.WriteLine($"{paramName} (valid values:{string.Join(", ", Enum.GetValues<T>().Select(x => x.ToString()))}): ");

				string input = Console.ReadLine();
				if (Enum.TryParse(input, true, out T o))
					return o;

				Console.WriteLine($"Invalid input");
			}
		}
	}
}
