using System.Net.Http;

namespace SCLOCUA
{
    public static class HttpClientService
    {
        public static readonly HttpClient Client;

        static HttpClientService()
        {
            Client = new HttpClient();
            if (!Client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                Client.DefaultRequestHeaders.Add("User-Agent", "SCLOCUA");
            }
        }
    }
}
