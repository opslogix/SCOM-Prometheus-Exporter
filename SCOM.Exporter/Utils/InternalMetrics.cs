using Prometheus;
using System.Collections.Generic;

namespace SCOM.Exporter.Models
{
    public static class InternalMetrics
    {

        public const string PROCESS_SCOM_RULES_COUNT_NAME = "process_scom_rules_count";
        public const string PROCESS_SCOM_RULES_COUNT_DESCRIPTION = "number of rules found in SCOM environment";
        public const string PROCESS_SCOM_METRICS_SCRAPE_REQUEST_COUNT_NAME = "process_scom_metrics_scrape_request_count";
        public const string PROCESS_SCOM_METRICS_SCRAPE_REQUEST_COUNT_DESCRIPTION = "number of SCOM metrics scrape requests since uptime";
        public const string PROCESS_SCOM_CONFIGURATION_REFRESH_COUNT_NAME = "process_scom_configuration_refresh_count";
        public const string PROCESS_SCOM_CONFIGURATION_REFRESH_COUNT_DESCRIPTION = "number of SCOM configuration changes since uptime";
        public const string PROCESS_SCOM_CLASSES_COUNT_NAME = "process_scom_classes_count";
        public const string PROCESS_SCOM_CLASSES_COUNT_DESCRIPTION = "number of classes found in SCOM environment";
        public const string PROCESS_SCOM_INSTANCES_COUNT_NAME = "process_scom_instances_count";
        public const string PROCESS_SCOM_INSTANCES_COUNT_DESCRIPTION = "number of instances found in SCOM environment";

        public const string PROCESS_SCOM_GROUPS_COUNT_NAME = "process_scom_groups_count";
        public const string PROCESS_SCOM_GROUPS_COUNT_DESCRIPTION = "number of groups found in SCOM environment";
        public const string PROCESS_SCOM_MONITORINGOBJECTS_COUNT_NAME = "process_scom_monitoringobjects_count";
        public const string PROCESS_SCOM_MONITORINGOBJECTS_COUNT_DESCRIPTION = "number of monitoring objects found in SCOM environment";
        public const string PROCESS_SCOM_METRICS_SCRAPE_LAST_NAME = "process_scom_metrics_scrape_last";
        public const string PROCESS_SCOM_METRICS_SCRAPE_LAST_DESCRIPTION = "last time SCOM metrics were scraped";
        public const string PROCESS_SCOM_METRICS_SCRAPE_DURATION_NAME = "process_scom_metrics_scrape_duration_seconds";
        public const string PROCESS_SCOM_METRICS_SCRAPE_DURATION_DESCRIPTION = "duration of retrieval of SCOM metrics";

        public static readonly Dictionary<string, Counter> Counters = new Dictionary<string, Counter>
        {
            { PROCESS_SCOM_METRICS_SCRAPE_REQUEST_COUNT_NAME, Metrics.CreateCounter(PROCESS_SCOM_METRICS_SCRAPE_REQUEST_COUNT_NAME, PROCESS_SCOM_METRICS_SCRAPE_REQUEST_COUNT_DESCRIPTION) },
            { PROCESS_SCOM_CONFIGURATION_REFRESH_COUNT_NAME, Metrics.CreateCounter(PROCESS_SCOM_CONFIGURATION_REFRESH_COUNT_NAME, PROCESS_SCOM_CONFIGURATION_REFRESH_COUNT_DESCRIPTION) }
        };

        public static readonly Dictionary<string, Gauge> Gauges = new Dictionary<string, Gauge>
        {
            { PROCESS_SCOM_RULES_COUNT_NAME, Metrics.CreateGauge(PROCESS_SCOM_RULES_COUNT_NAME, PROCESS_SCOM_RULES_COUNT_DESCRIPTION) },
            { PROCESS_SCOM_INSTANCES_COUNT_NAME, Metrics.CreateGauge(PROCESS_SCOM_INSTANCES_COUNT_NAME, PROCESS_SCOM_INSTANCES_COUNT_DESCRIPTION)},
            { PROCESS_SCOM_CLASSES_COUNT_NAME, Metrics.CreateGauge(PROCESS_SCOM_CLASSES_COUNT_NAME, PROCESS_SCOM_CLASSES_COUNT_DESCRIPTION) },
            { PROCESS_SCOM_GROUPS_COUNT_NAME, Metrics.CreateGauge(PROCESS_SCOM_GROUPS_COUNT_NAME, PROCESS_SCOM_GROUPS_COUNT_DESCRIPTION) },
            { PROCESS_SCOM_MONITORINGOBJECTS_COUNT_NAME, Metrics.CreateGauge(PROCESS_SCOM_MONITORINGOBJECTS_COUNT_NAME, PROCESS_SCOM_MONITORINGOBJECTS_COUNT_DESCRIPTION) },
            { PROCESS_SCOM_METRICS_SCRAPE_LAST_NAME, Metrics.CreateGauge(PROCESS_SCOM_METRICS_SCRAPE_LAST_NAME, PROCESS_SCOM_METRICS_SCRAPE_LAST_DESCRIPTION) },
        };

        public static readonly Dictionary<string, Histogram> Histograms = new Dictionary<string, Histogram>
        {
             { PROCESS_SCOM_METRICS_SCRAPE_DURATION_NAME, Metrics.CreateHistogram(PROCESS_SCOM_METRICS_SCRAPE_DURATION_NAME, PROCESS_SCOM_METRICS_SCRAPE_DURATION_DESCRIPTION) }
        };
    }
}
