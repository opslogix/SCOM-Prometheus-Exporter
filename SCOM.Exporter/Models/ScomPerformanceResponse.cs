using Microsoft.EnterpriseManagement.Configuration;
using Microsoft.EnterpriseManagement.Monitoring;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SCOM.Exporter.Models
{
    public class ScomPerformanceResponse
    {
        public ManagementPackRule Rule { get; set; }
        public ManagementPackClass Class { get; set; }

        private string _metricName;

        public string MetricName
        {
            get
            {
                if (!string.IsNullOrEmpty(_metricName))
                    return _metricName;

                if (Rule is null || Class is null || !Values.Any())
                    throw new Exception("Unable to compile MetricName. Missing required data");

                _metricName = Counters.SanitizeMetricName($"scom_{Class.Name}_{Values.FirstOrDefault().Data.CounterName}_{Counters.GetUnit(Values.FirstOrDefault().Data)}");

                return _metricName;
            }
        }

        public string MetricDescription
        {
            get
            {
                if (Rule is null)
                    throw new Exception("Missing rule");

                return Rule.Description?.Replace("\r\n", " ").Replace("\n", " "); ;
            }
        }

        public IEnumerable<ScomPerformanceResponseValue> Values { get; set; }
    }

    public class ScomPerformanceResponseValue
    {
        public MonitoringPerformanceData Data { get; set; }
        public MonitoringPerformanceDataValue Value { get; set; }
    }
}
