using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using System.IO;

using ConnectionBridge.Messages;

namespace ConnectionBridge
{
	class MessageSerializer
	{
		readonly SecureChannel _Channel;

		public MessageSerializer(SecureChannel channel)
		{
			_Channel = channel ?? throw new ArgumentNullException(nameof(channel));
		}

		public Task Send<T>(T message) where T : BaseMessage
		{
			var dataToSend = Serialize(message);
			return _Channel.Send(dataToSend);
		}

		private static byte[] Serialize<T>(T message) where T : BaseMessage
		{
			var fields = typeof(T).GetFields();
			var baseMessageFields = typeof(BaseMessage).GetFields();

			using var memoryStream = new MemoryStream();
			using var writer = new BinaryWriter(memoryStream);

			//2 for length
			//1 for type
			memoryStream.Position = 3;

			for (int index = 0; index < fields.Length; index++)
			{
				var field = fields[index];
				if (baseMessageFields.Any(x => x.Name == field.Name))
					continue;

				if (field.FieldType == typeof(byte))
					writer.Write((byte)field.GetValue(message));

				if (field.FieldType == typeof(ushort))
					writer.Write((ushort)field.GetValue(message));

				if (field.FieldType == typeof(short))
					writer.Write((short)field.GetValue(message));

				if (field.FieldType == typeof(uint))
					writer.Write((uint)field.GetValue(message));

				if (field.FieldType == typeof(int))
					writer.Write((int)field.GetValue(message));

				if (field.FieldType == typeof(ulong))
					writer.Write((ulong)field.GetValue(message));

				if (field.FieldType == typeof(long))
					writer.Write((long)field.GetValue(message));

				if (field.FieldType == typeof(float))
					writer.Write((float)field.GetValue(message));

				if (field.FieldType == typeof(double))
					writer.Write((double)field.GetValue(message));

				if (field.FieldType == typeof(string) && field.GetValue(message) is string str)
					writer.Write(str);

				if (field.FieldType == typeof(bool))
					writer.Write((bool)field.GetValue(message));

				if (field.FieldType == typeof(Guid))
					writer.Write(((Guid)field.GetValue(message)).ToByteArray());
			}

			memoryStream.Position = 0;

			writer.Write((short)memoryStream.Length);
			writer.Write(message.MessageType);

			return memoryStream.ToArray();
		}
	}
}
