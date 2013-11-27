﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.Net.Sockets;

using System.Xml.Linq;


namespace Arith.Net
{
    public static class UPnP
    {

        public static Uri SsdpMSearch(int udpPort, MSearchValue values)
        {
            var data = Encoding.UTF8.GetBytes(
@"M-SEARCH * HTTP/1.1
MX: " + values.Mx.ToString() + @"
HOST: " + values.Host.ToString() + @"
MAN: """ + values.Man + @"""
ST: " + values.St + "\r\n\r\n");

            var udpClient = new UdpClient(udpPort, values.Host.AddressFamily);

            string res;

            try
            {
                udpClient.JoinMulticastGroup(values.Host.Address);
                udpClient.Send(data, data.Length, values.Host);

                var dmyAddr = new IPEndPoint(IPAddress.Any, 0);
                res = Encoding.UTF8.GetString(udpClient.Receive(ref dmyAddr));
            }
            finally
            {
                udpClient.Close();
            }

            var loc = res.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').FirstOrDefault(x => x.Length > 10 && x.Substring(0, 10).ToLower() == "location: ").Substring(10);

            if (loc != null) return new Uri(loc);
            throw new Exception("レスポンスに Location がありませんでした。");
        }
        
        public static Uri GetUPnPControlUri(Uri location, Uri serviceUrn)
        {
            return GetUPnPControlUris(location, new[] { serviceUrn }).FirstOrDefault().Value;
        }

        public static KeyValuePair<Uri, Uri>[] GetUPnPControlUris(Uri location, Uri[] serviceUrns)
        {
            var req = (HttpWebRequest)HttpWebRequest.Create(location);
            req.KeepAlive = false;
            var res = req.GetResponse();

            XDocument resDoc;
            using (var stream = res.GetResponseStream())
                resDoc = XDocument.Load(stream);

            var ns = XNamespace.Get("urn:schemas-upnp-org:device-1-0");

            return resDoc.Root.Descendants(ns + "device")
                .SelectMany(x => x.Element(ns + "serviceList").Elements(ns + "service"))
                .Where(x => serviceUrns.Any(y => y.ToString() == x.Element(ns + "serviceType").Value))
                .Select(x => {
                    Uri ctrluri;
                    try
                    {
                        ctrluri = new Uri(x.Element(ns + "controlURL").Value);
                    }
                    catch (UriFormatException)
                    {
                        ctrluri = new Uri(location.Scheme + "://" + location.Host + ":" + location.Port + x.Element(ns + "controlURL").Value);
                    }

                    return new KeyValuePair<Uri, Uri>(serviceUrns.First(y => y.ToString() == x.Element(ns + "serviceType").Value), ctrluri);
                }).ToArray();
        }

        public static KeyValuePair<string, string>[] PostSoap(Uri controlUrl, Uri ServiceType, string action, IDictionary<string, object> values = null) {

            if (values == null) values = new Dictionary<string, object>();

            var reqBody = XDocument.Parse(
@"<?xml version=""1.0""?>
<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"" SOAP-ENV:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
  <SOAP-ENV:Body>
    <m:" + action + @" xmlns:m=""" + ServiceType + @""">
    </m:" + action + @">
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>");

            reqBody.Root.Descendants("{" + ServiceType + "}" + action).First().Add(values.Select(x => new XElement(x.Key, x.Value)).ToArray());

            var req = (HttpWebRequest)HttpWebRequest.Create(controlUrl);
            req.Method = "POST"; 
            req.ContentType = "text/xml; charset=\"utf-8\"";
            req.KeepAlive = false;
            req.Headers.Add("SOAPACTION: \"" + ServiceType + "#" + action + "\"");

            using (var stream = req.GetRequestStream())
                reqBody.Save(stream);

            WebResponse res;

            try
            {
                res = req.GetResponse();
                
                XDocument resDoc;
                using (var stream = res.GetResponseStream())
                    resDoc = XDocument.Load(stream);

                return resDoc.Descendants("{" + ServiceType + "}" + action + "Response").Elements().Select(x => new KeyValuePair<string, string>(x.Name.LocalName, x.Value)).ToArray();
            }
            catch(WebException e)
            {
                if (e.Response == null) throw e;
                using (var stream = e.Response.GetResponseStream())
                {
                    var errdoc = XDocument.Load(stream);
                    throw new WebException(errdoc.Root.Descendants("errorDescription").First().Value, e);
                }
            }
        }
    }


    public class PortMapper : IDisposable
    {

        public string Protocol { get; private set; }
        public int InternalPort { get; private set; }
        public int ExternalPort { get; private set; }
        public IPAddress InternalClientAddress { get; private set; }

        Uri location, ctrlUri, service;

        public static PortMapper Map(string protocol, int port)
        {
            return Map(protocol, port, port, GetHostAddress());
        }

        public static PortMapper Map(string protocol, int internalPort, int externalPort)
        {
            return Map(protocol, internalPort, externalPort, GetHostAddress());
        }

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


        public static PortMapper Map(string protocol, int internalPort, int externalPort, IPAddress clientAddress)
        {
            var mapper = new PortMapper()
            {
                Protocol = protocol,
                InternalPort = internalPort,
                ExternalPort = externalPort,
                InternalClientAddress = clientAddress
            };
            
            mapper.location = UPnP.SsdpMSearch(internalPort, new MSearchValue()
            {
                Mx = 3,
                Host = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900),
                Man = "ssdp:discover",
                St = "upnp:rootdevice"
            });


            var ctrlUris = UPnP.GetUPnPControlUris(mapper.location, new[] { new Uri("urn:schemas-upnp-org:service:WANPPPConnection:1"), new Uri("urn:schemas-upnp-org:service:WANIPConnection:1") });
            mapper.ctrlUri = ctrlUris.First().Value;
            mapper.service = ctrlUris.First().Key;

            UPnP.PostSoap(mapper.ctrlUri, mapper.service, "AddPortMapping", new Dictionary<string, object>
            {
                { "NewRemoteHost", ""},
                { "NewExternalPort", mapper.ExternalPort}, 
                { "NewProtocol", mapper.Protocol}, 
                { "NewInternalPort", mapper.InternalPort}, 
                { "NewInternalClient", mapper.InternalClientAddress.ToString()}, 
                { "NewEnabled", 1}, 
                { "NewPortMappingDescription", "Hello World!!"}, 
                { "NewLeaseDuration", 0}, 
            });

            return mapper;
        }

        private PortMapper() { }
        
        public bool IsDisposed { get; private set; }

        public IPAddress GetExternalIPAddress()
        {
            if (IsDisposed) throw new ObjectDisposedException(this.GetType().Name);

            var res = UPnP.PostSoap(ctrlUri, service, "GetExternalIPAddress");
            return IPAddress.Parse(res.First(x => x.Key == "NewExternalIPAddress").Value);
        }

        protected void Dispose(Boolean disposing)
        {
            if (IsDisposed) return;

            if (disposing)
            {
                //  マネージ リソースの削除
            }

            //  ポートマッピング削除
            UPnP.PostSoap(ctrlUri, service, "DeletePortMapping", new Dictionary<string, object>
                {
                    {"NewRemoteHost", ""},
                    {"NewExternalPort", ExternalPort},
                    {"NewProtocol", Protocol},
                });
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
            Dispose(false);
        }
    }

    public class MSearchValue
    {
        public int Mx { get; set; }
        public IPEndPoint Host { get; set; }
        public string Man { get; set; }
        public string St { get; set; }

        public MSearchValue()
        {
            Host = new IPEndPoint(IPAddress.Any, 0);
        }
    }
}