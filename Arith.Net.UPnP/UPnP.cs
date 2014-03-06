using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.Net.Sockets;

using System.Xml.Linq;
using UPNPLib;

namespace Arith.Net
{
    public static class UPnP
    {
        static UPnPDeviceFinder finder;
        static UPnPDeviceFinder Finder { get { return finder = finder ?? new UPnPDeviceFinder(); } }

        private static UPnPDevice[] GetDevices(UPnPDeviceFinder finder, Uri typeUri)
        {
            return finder.FindByType(typeUri.ToString(), 0).Cast<UPnPDevice>().ToArray();
        }

        public static object[] InvokeAction(this UPnPService service, string actionName)
        {
            return service.InvokeAction(actionName, new object[0]);
        }

        public static object[] InvokeAction(this UPnPService service, string actionName, object[] actionArgs)
        {
            object obj = new object();
            service.InvokeAction(actionName, actionArgs, ref obj);
            return (object[])obj;
        }

        private static UPnPDevice GetDevice(UPnPDeviceFinder finder, Uri udn)
        {
            return finder.FindByUDN(udn.ToString());
        }

        public static UPnPDevice[] GetDevices(Uri typeUri)
        {
            return GetDevices(Finder, typeUri);
        }

        public static Uri[] GetDeviceUDNs(Uri typeUri)
        {
            return GetDevices(typeUri).Select(x => new Uri(x.UniqueDeviceName)).ToArray();
        }

        public static UPnPDevice GetDevice(Uri udn)
        {
            return GetDevice(Finder, udn);
        }

        public static IDictionary<Uri, UPnPService> GetServices(this UPnPDevice device)
        {
            return device.Services.Cast<UPnPService>().ToDictionary(x => new Uri(x.Id), y => device.Services[y.Id]);
        }

        public static UPnPService GetService(this UPnPDevice device, Uri serviceId)
        {
            return device.GetServices()[serviceId];
        }

        [Obsolete("GetDeviceUDNs(Uri) を使用してください。", true)]
        public static Uri SsdpMSearch(IPEndPoint Host, string Man, string St, int Mx)
        {
            throw new NotImplementedException();
        }

        [Obsolete("GetDeviceUDNs(Uri) を使用してください。", true)]
        public static Uri SsdpMSearch(int udpPort, MSearchValue values)
        {
            throw new NotImplementedException();
        }

        [Obsolete("GetDeviceUDNs(Uri) を使用してください。", true)]
        public static Uri GetUPnPControlUri(Uri location, Uri serviceUrn)
        {
            throw new NotImplementedException();
        }

        [Obsolete("GetDeviceUDNs(Uri) を使用してください。", true)]
        public static KeyValuePair<Uri, Uri>[] GetUPnPControlUris(Uri location, Uri[] serviceUrns)
        {
            throw new NotImplementedException();
        }
    }


    public class PortMapper : IDisposable
    {

        public string Protocol { get; private set; }
        public int InternalPort { get; private set; }
        public int ExternalPort { get; private set; }
        public IPAddress InternalClientAddress { get; private set; }
        
        UPnPService service;

        public static PortMapper Map(string protocol, ushort port)
        {
            return Map(protocol, port, port, GetHostAddress());
        }

        [Obsolete]
        public static PortMapper Map(string protocol, int port)
        {
            return Map(protocol, port, port, GetHostAddress());
        }

        public static PortMapper Map(string protocol, ushort internalPort, ushort externalPort)
        {
            return Map(protocol, internalPort, externalPort, GetHostAddress());
        }

        [Obsolete]
        public static PortMapper Map(string protocol, int internalPort, int externalPort)
        {
            return Map(protocol, internalPort, externalPort, GetHostAddress());
        }

        public static PortMapper Map(string protocol, ushort port, IPAddress clientAddress)
        {
            return Map(protocol, port, port, clientAddress);
        }

        [Obsolete]
        public static PortMapper Map(string protocol, int port, IPAddress clientAddress)
        {
            return Map(protocol, port, port, clientAddress);
        }

        public static IPAddress[] GetHostAddresses()
        {
            return Dns.GetHostAddresses(Dns.GetHostName());
        }

        public static IPAddress GetHostAddress()
        {
            return GetHostAddresses().FirstOrDefault();
        }

        public static IPAddress GetHostAddress(AddressFamily family)
        {
            return GetHostAddresses().FirstOrDefault(x => x.AddressFamily == family);
        }


        public static PortMapper Map(string protocol, ushort internalPort, ushort externalPort, IPAddress clientAddress)
        {
            return Map(protocol, internalPort, externalPort, clientAddress);
        }

        public static PortMapper Map(string protocol, ushort internalPort, ushort externalPort, IPAddress clientAddress, string description)
        {
            var devices = 
                UPnP.GetDevices(new Uri("urn:schemas-upnp-org:service:WANPPPConnection:1")).Union(
                UPnP.GetDevices(new Uri("urn:schemas-upnp-org:service:WANIPConnection:1")));

            return Map(new Uri(devices.FirstOrDefault().UniqueDeviceName), protocol, internalPort, externalPort, clientAddress, description);
        }

        public static PortMapper Map(Uri deviceUDN, string protocol, ushort internalPort, ushort externalPort, IPAddress clientAddress, string description)
        {
            var device = UPnP.GetDevice(deviceUDN);
            var service = device.Services["urn:upnp-org:serviceId:WANPPPConn1"];
            if (service == null)
                service = device.Services["urn:upnp-org:serviceId:WANIPConn1"];

            var mapper = new PortMapper()
            {
                Protocol = protocol,
                InternalPort = internalPort,
                ExternalPort = externalPort,
                InternalClientAddress = clientAddress,

                service = service,
            };

            service.InvokeAction(
                "AddPortMapping",
                new object[] { "", externalPort, protocol, internalPort, clientAddress, true, "", 0 });

            return mapper;
        }

        [Obsolete]
        public static PortMapper Map(string protocol, int internalPort, int externalPort, IPAddress clientAddress)
        {
            return Map(protocol, (ushort)internalPort, (ushort)externalPort, clientAddress);
        }

        [Obsolete]
        public static PortMapper Map(string protocol, int internalPort, int externalPort,
            IPAddress clientAddress, Uri service, Uri location) {
                return Map(protocol, (ushort)internalPort, (ushort)externalPort, clientAddress, service, location);
        }

        [Obsolete("Map(Uri, string, ushort, ushort, IPAddress) を使用してください。", true)]
        public static PortMapper Map(string protocol, ushort internalPort, ushort externalPort,
            IPAddress clientAddress, Uri service, Uri location)
        {
            throw new NotImplementedException();
        }

        private PortMapper() { }
        
        public bool IsDisposed { get; private set; }

        public IPAddress GetExternalIPAddress()
        {
            if (IsDisposed) throw new ObjectDisposedException(this.GetType().Name);

            var res = service.InvokeAction("GetExternalIPAddress", new object[0]);
            return IPAddress.Parse((string)res[0]);
        }

        protected void Dispose(Boolean disposing)
        {
            if (IsDisposed) return;
            IsDisposed = true;

            if (disposing)
            {
                //  マネージ リソースの削除
            }

            //  ポートマッピング削除
            service.InvokeAction("DeletePortMapping", new object[] { "", ExternalPort, Protocol });
        }

        public void Delete()
        {
            ((IDisposable)this).Dispose();
        }

        void IDisposable.Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        ~PortMapper()
        {
            try
            {
                Dispose(false);
            }
            catch (WebException)
            {
                
            }
        }
    }

    [Obsolete("", true)]
    public class MSearchValue
    {
        public MSearchValue()
        {
            throw new NotImplementedException();
        }
    }
}
