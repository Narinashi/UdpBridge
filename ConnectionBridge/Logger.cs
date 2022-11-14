using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge
{
	static class Logger
	{
		public static LogLevel LogLevel;

		public static void Debug(string message)
		{
			if (LogLevel < LogLevel.Debug)
				return;

			Console.WriteLine($"Debug: {message}");
		}

		public static void Info(string message)
		{
			if (LogLevel < LogLevel.Info)
				return;

			Console.WriteLine($"Info: {message}");
		}

		public static void Warning(string message)
		{
			if (LogLevel < LogLevel.Warning)
				return;

			Console.WriteLine($"Warning:{message}");
		}

		public static void Error(string message)
		{
			if (LogLevel < LogLevel.Error)
				return;

			Console.WriteLine($"Error: {message}");
		}
	}

	enum LogLevel
	{
		Error,
		Warning,
		Info,
		Debug
	}
}
