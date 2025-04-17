using System.ServiceProcess;

namespace SCOM.Exporter.Service
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[] 
            { 
                new ScomExporter() 
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}
