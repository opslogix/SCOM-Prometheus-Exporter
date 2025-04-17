using Microsoft.Owin.Hosting;
using System;

namespace SCOM.Exporter.Debug
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string baseAddress = "http://localhost:3005/";

            // Start OWIN host 
            using (WebApp.Start<Startup>(url: baseAddress))
            {
                Console.WriteLine($"Collector listening on {baseAddress}");
                Console.ReadLine();
            }
        }
    }
}
