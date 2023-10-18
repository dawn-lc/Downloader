using System.Net.Http.Headers;
using System.Reflection;

namespace Downloader
{
    internal class Environment
    {
        private static readonly Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new(1, 0, 0);
        public static readonly string Name = "Downloader";
        public static readonly string Path = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string Developer = "dawn-lc";
        public static readonly string HomePage = $"https://github.com/{Developer}/{Name}";
        public static readonly int[] Version = new int[] { version.Major, version.Minor, version.Build };
        public static readonly HttpHeaders Headers = new HttpClient().DefaultRequestHeaders;
    }
}
