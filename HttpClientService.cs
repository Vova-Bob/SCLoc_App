using System;
using System.Net.Http;

namespace SCLOCUA
{
    public static class HttpClientService
    {
        public static readonly HttpClient Client;

        static HttpClientService()
        {
            // Ensure TLS 1.2/1.3 on older Windows
            System.Net.ServicePointManager.SecurityProtocol =
                System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls13;

            // ? increase timeout (10s -> 30s або 60s)
            Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            // GitHub requires User-Agent
            if (!Client.DefaultRequestHeaders.Contains("User-Agent"))
                Client.DefaultRequestHeaders.Add("User-Agent", "SCLOCUA/1.0");
        }
    }
}
