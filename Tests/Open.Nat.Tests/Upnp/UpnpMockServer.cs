using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace Open.Nat.Tests.Upnp;

internal class UpnpMockServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly ServerConfiguration _cfg;
    public Action<HttpListenerContext> WhenRequestServiceDesc = WhenRequestService;
    public Action<HttpListenerContext> WhenGetExternalIpAddress = ResponseOk;
    public Action<HttpListenerContext> WhenAddPortMapping = ResponseOk;
    public Action<HttpListenerContext> WhenGetGenericPortMappingEntry = ResponseOk;
    public Action<HttpListenerContext> WhenDeletePortMapping = ResponseOk;
    public Func<string> WhenDiscoveryRequest;

    private string HandleDiscoveryRequest()
    {
        return "HTTP/1.1 200 OK\r\n" +
            "Server: Custom/1.0 UPnP/1.0 Proc/Ver\r\n" +
            "EXT:\r\n" +
            $"Location: {_cfg.ServiceUrl}\r\n" +
            "Cache-Control:max-age=1800\r\n" +
            $"ST:urn:schemas-upnp-org:service:{_cfg.ServiceType}\r\n" +
            $"USN:uuid:0000e068-20a0-00e0-20a0-48a802086048::urn:schemas-upnp-org:service:{_cfg.ServiceType}";
    }

    private static void ResponseOk(HttpListenerContext context)
    {
        context.Response.Status(200, "OK");
    }

    private static void WhenRequestService(HttpListenerContext context)
    {
        var responseBytes = File.OpenRead("..\\..\\Responses\\ServiceDescription.txt");
        responseBytes.CopyTo(context.Response.OutputStream);
        context.Response.OutputStream.Flush();

        context.Response.Status(200, "OK");
    }

    public UpnpMockServer() : this(new ServerConfiguration()) { }

    public UpnpMockServer(ServerConfiguration cfg)
    {
        _cfg = cfg;
        _listener = new HttpListener();
        _listener.Prefixes.Add(cfg.Prefix);
        _listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
        WhenDiscoveryRequest = HandleDiscoveryRequest;
    }

    public void Start()
    {
        StartAnnouncer();
        StartServer();
    }

    private void StartAnnouncer()
    {
        Task.Run(
            () =>
            {
                UdpClient udpClient = default!;
                try
                {
                    var remoteIPEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    using (udpClient = new UdpClient(1900))
                    {
                        while (true)
                        {
                            var bytes = udpClient.Receive(ref remoteIPEndPoint);
                            if (bytes == null || bytes.Length == 0) return;

                            var response = WhenDiscoveryRequest();

                            var responseBytes = Encoding.UTF8.GetBytes(response);
                            udpClient.Send(responseBytes, responseBytes.Length, remoteIPEndPoint);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                }
                finally
                {
                    udpClient?.Close();
                    udpClient?.Dispose();
                }
            });
    }

    private void StartServer()
    {
        _listener.Start();
        Task.Run(() =>
        {
            while (_listener.IsListening)
            {
                ProcessRequest();
            }
        });
    }

    private void ProcessRequest()
    {
        var result = _listener.BeginGetContext(ListenerCallback, _listener);
        result.AsyncWaitHandle.WaitOne();
    }

    private void ListenerCallback(IAsyncResult result)
    {
        if (!_listener.IsListening) return;
        var context = _listener.EndGetContext(result);
        var request = context.Request;
        if (request.Url.AbsoluteUri == _cfg.ServiceUrl)
        {
            WhenRequestServiceDesc(context);
            return;
        }

        if (request.Url.AbsoluteUri == _cfg.ControlUrl)
        {
            var soapActionHeader = request.Headers["SOAPACTION"];
            soapActionHeader = soapActionHeader.Substring(1, soapActionHeader.Length - 2);

            var soapActionHeaderParts = soapActionHeader.Split(new[] { '#' });
            var serviceType = soapActionHeaderParts[0];
            var soapAction = soapActionHeaderParts[1];
            var buffer = new byte[request.ContentLength64 - 4];
            request.InputStream.Read(buffer, 0, buffer.Length);
            var body = Encoding.UTF8.GetString(buffer);
            var envelop = XElement.Parse(body);

            switch (soapAction)
            {
                case "GetExternalIPAddress":
                    WhenGetExternalIpAddress(context);
                    return;
                case "AddPortMapping":
                    WhenAddPortMapping(context);
                    return;
                case "GetGenericPortMappingEntry":
                    WhenGetGenericPortMappingEntry(context);
                    return;
                case "DeletePortMapping":
                    WhenDeletePortMapping(context);
                    return;
            }
            context.Response.Status(200, "OK");
            return;
        }
        context.Response.Status(500, "Internal Server Error");
    }

    public void Dispose()
    {
        _listener.Close();
    }
}

internal static class HttpListenerResponseExtensions
{
    public static void Status(this HttpListenerResponse res, int statusCode, string description)
    {
        res.StatusCode = statusCode;
        res.StatusDescription = description;
        res.Close();
    }
}

internal class ServerConfiguration
{
    public ServerConfiguration()
    {
        ServiceType = "WANIPConnection:1";
        Prefix = "http://127.0.0.1:5431/";
        ServiceUrl = "/dyndev/uuid:0000e068-20a0-00e0-20a0-48a8000808e0";
        ControlUrl = "/uuid:0000e068-20a0-00e0-20a0-48a802086048/" + ServiceType;
    }

    public string ControlUrl { get; set; }

    public string ServiceUrl { get; set; }

    public string ServiceType { get; set; }

    public string Prefix { get; set; }
}
