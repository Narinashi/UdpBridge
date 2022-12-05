using SharpPcap;
using SharpPcap.LibPcap;
using System;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge
{
	class Program
	{
		static ConnectionBridge _ConnectionBridge;
		static bool _ServerMode;

		const int AuthenticationTimeout = 10000;

		static void Main(string[] args)
		{
			var rawClient = new Adapters.Raw.RawClientAdapter();
			var rawServer = new Adapters.Raw.RawServerAdapter();

			rawClient.Initialize("127.0.0.1", 1111);
			rawServer.Initialize("127.0.0.1", 2222);
			
			rawServer.Start();
			rawClient.Connect();

			var buffer = Encoding.ASCII.GetBytes("Hello!");

			rawClient.OnMessageReceived = (arg) => 
			{
				Console.Write($"Client received packet, {arg.Size}");
				rawClient.Send(buffer, 0, buffer.Length);
			};

			rawServer.OnMessageReceived = (arg) => 
			{
				Console.WriteLine($"Server received packet, {arg.Size}");
				rawServer.Send(arg.EndPoint, buffer, 0, buffer.Length);
			};

			rawClient.ReceiveAsync();
			rawClient.ReceiveAsync();


			rawClient.Send(buffer, 0, buffer.Length);

			Console.Write("Press enter to exit ...");
			Console.ReadLine();
		}

		static async Task Main1(string[] args)
		{
			if (args?.Length > 1)
			{
				_ServerMode = bool.Parse(args[0]);

				for (int i = 0; i < args.Length; i++)
					args[i] = args[i].Replace("\"", string.Empty);

				Logger.LogLevel = args.Length > 8 && Enum.TryParse(args[8], true, out LogLevel ll) ? ll : LogLevel.Info;

				if (_ServerMode)
				{
					await StartServerMode(args[1], args[2], int.Parse(args[3]), args[4], int.Parse(args[5]), args[6], int.Parse(args[7]));
				}
				else
				{
					await StartClientMode(args[1], int.Parse(args[2]), args[3], args[4], int.Parse(args[5]), args[6], int.Parse(args[7]));
				}

				while (Console.ReadKey().Key != ConsoleKey.Q) ;
			}
			else if (args?.Length == 1)
			{
				Console.WriteLine("In order to run the program as server mode use these params");
				Console.WriteLine(GetHowToRunMessage(true));

				Console.WriteLine("In order to run the program as client mode use these params");
				Console.WriteLine(GetHowToRunMessage(false));
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
											PromptStringParameter("udpServerLocalAddress"),
											PromptIntParameter("udpServerLocalPort"),
											PromptStringParameter("udpServerRemoteAddress"),
											PromptIntParameter("udpServerRemotePort")
											);
				}
				else
				{
					//await StartClientMode(PromptStringParameter("sslServerAddress"),
					//						PromptIntParameter("sslServerPort"),
					//						PromptStringParameter("trustedHostName"),
					//						PromptStringParameter("udpServerLocalAddress"),
					//						PromptIntParameter("udpServerLocalPort"),
					//						PromptStringParameter("udpServerRemoteAddress"),
					//						PromptIntParameter("udpServerRemotePort")
					//						);
					
					//await StartClientMode("51.75.68.16", 42069, "WrexUwU", "127.0.0.1", 1111, "51.75.68.16", 39911);
					
					await StartClientMode("159.69.45.213", 8554, "WrexUwU", "127.0.0.1", 1111, "159.69.45.213", 53);

				}
				while (Console.ReadKey().Key != ConsoleKey.Q) ;
			}
		}

		static async Task StartClientMode(string sslServerAddress,
											int sslServerPort,
											string trustedHostName,
											string udpServerLocalAddress,
											int udpServerLocalPort,
											string udpServerRemoteAddress,
											int udpServerRemotePort)
		{
			try
			{
				Logger.Info(() => $"Initiating in ClientMode ...");

				var secureChannel = await SecureChannel.ConnectTo(sslServerAddress, sslServerPort, trustedHostName, 4096);
				secureChannel.StartReceiving();

				Logger.Info(() => $"Initiating ConnectionBridge ...");

				_ConnectionBridge = new ConnectionBridge(secureChannel,
														udpServerLocalAddress,
														udpServerLocalPort,
														udpServerRemoteAddress,
														udpServerRemotePort);

				await _ConnectionBridge.Handshake();

				Logger.Info(() => $"ConnectionBridge Handshake complete");
			}
			catch (Exception ex)
			{
				Logger.Error(() => $"An exception occurred while trying to connect to server, \r\n{ex}");
				await StartClientMode(sslServerAddress,
										sslServerPort,
										trustedHostName,
										udpServerLocalAddress,
										udpServerLocalPort,
										udpServerRemoteAddress,
										udpServerRemotePort);
			}
		}

		static async Task StartServerMode(string sslCertificateFileAddress,
										string sslCertificatePassword,
										int sslServerListeningPort,
										string udpServerLocalAddress,
										int udpServerLocalPort,
										string udpServerRemoteAddress,
										int udpServerRemotePort)
		{
			Logger.Info(() => $"Initiating in ServerMode ...");

			var cert = new X509Certificate2(sslCertificateFileAddress, sslCertificatePassword);
			var listener = new SecureChannelListener(sslServerListeningPort);

			while (true)
			{
				try
				{
					Logger.Debug(() => $"Awaiting new connection");

					TcpClient client = listener.AcceptConnection();

					client.ReceiveTimeout = client.SendTimeout = 20000;

					Logger.Info(() => $"New TCP Connection from {client.Client?.RemoteEndPoint}");

					if (_ConnectionBridge != null)
					{
						Logger.Debug(() => "Connection bridge already instatiated, going to dispose the coming connection");
						client.Dispose();

						continue;
					}

					SecureChannel secureChannel = new(client, cert, 4096);

					if (await secureChannel.Authenticate())
					{
						secureChannel.StartReceiving();

						ConnectionBridge connectionBridge = new(secureChannel,
																	udpServerLocalAddress,
																	udpServerLocalPort,
																	udpServerRemoteAddress,
																	udpServerRemotePort, true);

						_ConnectionBridge = connectionBridge;

						secureChannel.OnClientDisconnected = () => 
						{
							Logger.Info(() => $"A connected client has disconnected, going to accept new client");
							_ConnectionBridge.Dispose();
							_ConnectionBridge = null;
						};
					}
				}
				catch (Exception ex)
				{
					Logger.Error(() => $"an exception occured in listener connection acceptance loop \r\n{ex}");
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
