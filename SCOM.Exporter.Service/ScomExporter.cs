using Microsoft.Owin.Hosting;
using System.ServiceProcess;

namespace SCOM.Exporter.Service
{
    public partial class ScomExporter: ServiceBase
    {
        public ScomExporter()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            WebApp.Start<Startup>("http://localhost:3005");
        }
 
        protected override void OnStop()
        {
        }
    }
}
