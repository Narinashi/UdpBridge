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

		public static void Debug(Func<string> messageFunc)
		{
			if (LogLevel < LogLevel.Debug)
				return;

			Console.WriteLine($"Debug: {messageFunc()}");
		}

		public static void Info(Func<string> messageFunc)
		{
			if (LogLevel < LogLevel.Info)
				return;

			Console.WriteLine($"Info: {messageFunc()}");
		}

		public static void Warning(Func<string> messageFunc)
		{
			if (LogLevel < LogLevel.Warning)
				return;

			Console.WriteLine($"Warning:{messageFunc()}");
		}

		public static void Error(Func<string> messageFunc)
		{
			if (LogLevel < LogLevel.Error)
				return;

			Console.WriteLine($"Error: {messageFunc()}");
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
