using System;
using System.Net.Http;

namespace SCLOCUA
{
    public static class HttpClientService
    {
        public static readonly HttpClient Client;

        static HttpClientService()
        {
            Client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            if (!Client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                Client.DefaultRequestHeaders.Add("User-Agent", "SCLOCUA");
            }
        }
    }
}
