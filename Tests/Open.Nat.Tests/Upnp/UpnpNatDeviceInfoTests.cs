using System.Net;

using NUnit.Framework;

namespace Open.Nat.Tests.Upnp;

public class UpnpNatDeviceInfoTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void TestServiceControlUri()
    {
        var info = new UpnpNatDeviceInfo(IPAddress.Loopback, new Uri("http://127.0.0.1:3221"), "/control?WANIPConnection", null);
        Assert.AreEqual("http://127.0.0.1:3221/control?WANIPConnection", info.ServiceControlUri.ToString());
    }
}
