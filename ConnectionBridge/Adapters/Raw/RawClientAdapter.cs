using SharpPcap;
using SharpPcap.LibPcap;
using PacketDotNet;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Net.NetworkInformation;

namespace ConnectionBridge.Adapters.Raw
{
	class RawClientAdapter : IClientAdapter
	{
		public OnMessageReceived OnMessageReceived
		{
			get => _OnUdpMessageReceived;
			set
			{
				_OnUdpMessageReceived = value;

				if (_Client != null)
					_Client.OnMessageReceived = value;
			}
		}

		OnMessageReceived _OnUdpMessageReceived;
		RawClient _Client;
		LibPcapLiveDevice _Device;

		ARP _Arp;
		PhysicalAddress _TargetPhysicalAddress;

		IPEndPoint _Endpoint;

		public void Connect()
		{
			if (_Client == null)
				throw new InvalidOperationException("Client hasnt been intialized yet");

			_Client.Connect();
		}

		public void Initialize(string address, ushort port)
		{
			_Endpoint = new IPEndPoint(IPAddress.Parse(address), port);
			_Client = new RawClient(address, port)
			{
				OnMessageReceived = _OnUdpMessageReceived
			};

			var devices = CaptureDeviceList.Instance;

			_Device = devices
						.Where(x => x.MacAddress != null)
						.FirstOrDefault(x => x is LibPcapLiveDevice liveDevice && liveDevice.Interface.GatewayAddresses.Any()) as LibPcapLiveDevice;
			
			if (_Device == null)
				throw new InvalidOperationException("No valid device found");
			
			_Device.Open();

			_Arp = new ARP(_Device);

			Logger.Info(() => $"Client selected device: {_Device.Name} {_Device.Description}");
		}

		public void Send(byte[] buffer, long offset, long length)
		{
			if (_Client == null || _Device == null)
				throw new InvalidOperationException("Raw client hasnt been initialized yet");

			Send(_Endpoint, buffer, offset, length);
		}

		public void Send(IPEndPoint endpoint, byte[] buffer, long offset, long length)
		{
			if (_Client == null || _Device == null)
				throw new InvalidOperationException("Raw client hasnt been initialized yet");

			if (_TargetPhysicalAddress == null)
				FindTargetPhysicalAddress();
			
			EthernetPacket ethernetPacket = new(_Device.MacAddress, _TargetPhysicalAddress, EthernetType.IPv4);
			IPv4Packet ipPacket = new(_Client.Endpoint.Address, endpoint.Address);
			TcpPacket tcpPacket = new((ushort)_Client.Endpoint.Port, (ushort)endpoint.Port);

			tcpPacket.PayloadDataSegment = new PacketDotNet.Utils.ByteArraySegment(buffer, (int)offset, (int)length);

			ipPacket.PayloadPacket = tcpPacket;
			ethernetPacket.ParentPacket = ipPacket;

			_Device.SendPacket(ethernetPacket);
		}

		private PhysicalAddress FindTargetPhysicalAddress()
		{
			return _TargetPhysicalAddress = _Arp.Resolve(_Device.Interface.GatewayAddresses[0]);
		}

		public void ReceiveAsync()
		{
			if (_Client == null)
				throw new InvalidOperationException("Raw client hasnt been initialized yet");

			_Client.ReceiveAsync();
		}

		public void Dispose()
		{
			_Client?.Dispose();
			_Device?.Dispose();
		}
	}
}
