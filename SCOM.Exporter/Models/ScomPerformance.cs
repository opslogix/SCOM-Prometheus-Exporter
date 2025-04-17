using System.Collections.Generic;

namespace SCOM.Exporter.Models
{
    public class ScomPerformance
    {
        public ScomPerformanceRequest Request { get; set; }
        public IEnumerable<ScomPerformanceResponse> Response { get; set; }
    }
}
