using System;
using System.Linq;
using System.Text;

using System.IO;

using ConnectionBridge.Messages;

namespace ConnectionBridge
{
	delegate void OnMessageReceived(BaseMessage message);
	class MessageDeserializer
	{
		public OnMessageReceived OnMessageReceived;

		readonly byte[] _MessageBuffer;

		readonly MemoryStream _MessageBufferStream;
		readonly BinaryReader _MessageBufferReader;

		readonly SecureChannel _Channel;

		int _BufferOffset;

		public MessageDeserializer(SecureChannel channel, int maxMessageSize)
		{
			if (maxMessageSize < 1)
				throw new ArgumentException("Buffer size cant be less than 1");

			_Channel = channel ?? throw new ArgumentNullException(nameof(channel));

			_MessageBuffer = new byte[maxMessageSize];
			_MessageBufferStream = new MemoryStream(_MessageBuffer);
			_MessageBufferReader = new BinaryReader(_MessageBufferStream, Encoding.ASCII, true);

			_Channel.OnDataReceived = OnChannelDataReceived;
		}

		private void OnChannelDataReceived(DataReceivedArgs args)
		{
			Buffer.BlockCopy(args.Buffer, 0, _MessageBuffer, _BufferOffset, args.Length);

			if (_BufferOffset < 2 && args.Length < 2)
				return;

			var length = _MessageBufferReader.ReadInt16();

			if (_BufferOffset + args.Length < length)
			{
				_MessageBufferStream.Position = 0;
				_BufferOffset += args.Length;
				return;
			}

			var type = _MessageBufferReader.ReadByte();

			var message = Parse(length, type);

			var remainingBytesInBuffer = _BufferOffset + args.Length - (int)_MessageBufferStream.Position;

			//If there is anything left in the _MessageBuffer, relocate it
			if (remainingBytesInBuffer > 0)
				Buffer.BlockCopy(_MessageBuffer, (int)_MessageBufferStream.Position, _MessageBuffer, 0, remainingBytesInBuffer);
			else if (remainingBytesInBuffer < 0)
				Logger.Warning($"Remaining bytes in MessageBuffer is {remainingBytesInBuffer}, which shouldnt happen");

			_BufferOffset = remainingBytesInBuffer;
			_MessageBufferStream.Position = 0;

			OnMessageReceived?.Invoke(message);
		}

		private BaseMessage Parse(short length, byte type)
		{
			BaseMessage message;

			switch ((MessageType)type)
			{
				case MessageType.Hello:
					message = new HelloMessage();
					break;
				case MessageType.KeyExchange:
					message = new KeyExchangeMessage();
					break;
				default:
					Logger.Warning($"Invalid MessgeType:{type}, len:{length}");
					return null;
			}

			message.MessageType = type;
			message.Length = length;

			PopulateProperties(message);

			return message;
		}

		private BaseMessage PopulateProperties(BaseMessage message) 
		{
			var fields = message.GetType().GetFields();
			var baseMessageFields = typeof(BaseMessage).GetFields();

			for (int index = 0; index < fields.Length; index++)
			{
				var field = fields[index];

				if (baseMessageFields.Any(x => x.Name == field.Name))
					continue;

				if (field.FieldType == typeof(byte))
					field.SetValue(message, _MessageBufferReader.ReadByte());

				if (field.FieldType == typeof(ushort))
					field.SetValue(message, _MessageBufferReader.ReadUInt16());

				if (field.FieldType == typeof(short))
					field.SetValue(message, _MessageBufferReader.ReadInt16());

				if (field.FieldType == typeof(uint))
					field.SetValue(message, _MessageBufferReader.ReadUInt32());

				if (field.FieldType == typeof(int))
					field.SetValue(message, _MessageBufferReader.ReadInt32());

				if (field.FieldType == typeof(ulong))
					field.SetValue(message, _MessageBufferReader.ReadUInt64());

				if (field.FieldType == typeof(long))
					field.SetValue(message, _MessageBufferReader.ReadInt64());

				if (field.FieldType == typeof(float))
					field.SetValue(message, _MessageBufferReader.ReadSingle());

				if (field.FieldType == typeof(double))
					field.SetValue(message, _MessageBufferReader.ReadDouble());

				if (field.FieldType == typeof(string))
					field.SetValue(message, _MessageBufferReader.ReadString());

				if (field.FieldType == typeof(bool))
					field.SetValue(message, _MessageBufferReader.ReadBoolean());

				if (field.FieldType == typeof(Guid))
					field.SetValue(message, new Guid(_MessageBufferReader.ReadBytes(16)));
			}

			return message;
		}
	}
}
