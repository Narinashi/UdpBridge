using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionBridge
{
	static class Logger
	{
		public static void Info(string message)
		{
			Console.WriteLine($"Info: {message}");
		}
		public static void Warning(string message)
		{
			Console.WriteLine($"Warning:{message}");
		}
		public static void Error(string message)
		{
			Console.WriteLine($"Error: {message}");
		}
	}
}
