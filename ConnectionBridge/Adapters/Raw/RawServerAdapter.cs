using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

using PacketDotNet;
using PacketDotNet.Utils;
using SharpPcap;
using SharpPcap.LibPcap;

namespace ConnectionBridge.Adapters.Raw
{
	class RawServerAdapter : IServerAdapter
	{
		public OnMessageReceived OnMessageReceived { set; get; }
	
		RawServer _RawServer;

		ARP _Arp;
		LibPcapLiveDevice _Device;
		
		PhysicalAddress _TargetPhysicalAddress;
		IPEndPoint _Endpoint;

		public void Initialize(string address, ushort port)
		{
			_RawServer = new RawServer(address, port)
			{
				OnMessageReceived = OnRawMessageReceived
			};

			_Endpoint = new IPEndPoint(IPAddress.Parse(address), port);

			var devices = CaptureDeviceList.Instance;

			_Device = devices
				.Where(x => x.MacAddress != null)
				.FirstOrDefault(x =>x is LibPcapLiveDevice liveDevice && liveDevice.Interface.GatewayAddresses.Any()) as LibPcapLiveDevice;
			
			if (_Device == null)
				throw new InvalidOperationException("No valid device found");
			
			_Arp = new ARP(_Device);
			_Device.Open();

			Logger.Info(() => $"Server selected device: {_Device.Name} {_Device.Description}");
		}

		public void Start()
		{
			if (_RawServer == null)
				throw new InvalidOperationException("Raw server hasnt been initialized yet");

			_RawServer.Start();
		}

		public void Send(byte[] buffer, long offset, long length)
		{
			throw new NotImplementedException();
		}

		public void Send(IPEndPoint endpoint, byte[] buffer, long offset, long length)
		{
			if (_RawServer == null || _Device == null)
				throw new InvalidOperationException("Raw server hasnt been initialized yet");

			if (_TargetPhysicalAddress == null)
				FindTargetPhysicalAddress();

			EthernetPacket ethernetPacket = new(_Device.MacAddress, _TargetPhysicalAddress, EthernetType.IPv4);
			IPv4Packet ipPacket = new(_Endpoint.Address, endpoint.Address);
			TcpPacket tcpPacket = new((ushort)_Endpoint.Port, (ushort)endpoint.Port);

			tcpPacket.PayloadDataSegment = new ByteArraySegment(buffer, (int)offset, (int)length);

			ipPacket.PayloadPacket = tcpPacket;
			ethernetPacket.PayloadPacket = ipPacket;

			_Device.SendPacket(ethernetPacket);
		}

		private PhysicalAddress FindTargetPhysicalAddress()
		{
			return _TargetPhysicalAddress = _Arp.Resolve(_Device.Interface.GatewayAddresses[0]);
		}

		public void ReceiveAsync()
		{
			if (_RawServer == null)
				throw new InvalidOperationException("Raw server hasnt been initialized yet");

			_RawServer.ReceiveAsync();
		}

		private void OnRawMessageReceived(MessageReceivedArgs args)
		{
			if (OnMessageReceived == null)
				return;

			Logger.Debug(() => $"Raw messaged received, going to parse to ethernet");

			var ethernetPacket = new EthernetPacket(new ByteArraySegment(args.Buffer, (int)args.Offset, (int)args.Size));
			
			Logger.Debug(() => $"Ethernetpacket parsed, source HAddress: {ethernetPacket.SourceHardwareAddress}");
			Logger.Debug(() => $"Going to parse to IP packet (Has payload ? {ethernetPacket.HasPayloadPacket})");
			
			if (!ethernetPacket.HasPayloadPacket)
				return;

			var tcpPacket = ethernetPacket.Extract<TcpPacket>();
			
			Logger.Debug(() => $"Tcp packet is null ? {tcpPacket == null}");
			
			if (tcpPacket == null)
				return;

			Logger.Debug(() => $"Tcp packet contains data ? {tcpPacket.HasPayloadData}");

			if (!tcpPacket.HasPayloadData)
				return;

			args.Buffer = tcpPacket.BytesSegment.Bytes;
			args.Offset = tcpPacket.BytesSegment.Offset;
			args.Size = tcpPacket.BytesSegment.Length;

			OnMessageReceived(args);
		}




		public void Dispose()
		{
			_RawServer?.Dispose();
			_Device?.Dispose();
		}
	}
}
