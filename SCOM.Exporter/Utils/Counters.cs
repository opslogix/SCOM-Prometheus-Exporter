using Microsoft.EnterpriseManagement.Monitoring;
using System;
using System.Text.RegularExpressions;

namespace SCOM.Exporter
{
    internal static class Counters
    {
        public static readonly Regex ValidMetricNameRegex = new Regex("^[a-zA-Z_:][a-zA-Z0-9_:]*$");
        public static readonly Regex InvalidCharsRegex = new Regex("[^a-zA-Z0-9_]");

        // Define keywords and corresponding units.
        private static readonly string[] _timeKeywords = new[] { "Latency", "Processing Time", "Wait Time", "Up Time", "Idle Time", "Ready Time", "Summation", "IOPS", "ms", "sec", "cpu idle" };
        private static readonly string[] _countKeywords = new[] { "Size", "Count", "Number", "Connections", "Threads", "Errors", "Data Items", "Alerts", "Workflows", "Modules", "Uploads", "Avg. Batch Size" };
        private static readonly string[] _sizeKeywords = new[] { "Memory", "KiloBytes", "Working Set", "Bytes", "Working Set", "GB" };
        private static readonly string[] _kbPSKeywords = new[] { "Network Usage", "Disk Usage", "Activity Usage" };
        private static readonly string[] _percentageKeywords = new[] { "Percentage", "% Used", "% Free", "% Activity", "% CPU", "% Memory", "%", "agent processor utilization", "Read Cache Hit Rate", "Percent" };
        private static readonly string[] _rateKeywords = new[] { "Rate", "Throughput", "Items/sec", "Batches/sec", "Errors/sec" };
        private static readonly string[] _frequencyKeywords = new[] { "MHz", "Average CPU time" };
        private static readonly string[] _ruleDescriptionKeywords = new[] { "IOPS", "percent", "maximum" };

        public static string SanitizeMetricName(string metricName)
        {
            if (metricName is null)
                return "";

            // Replace any character that doesn't match [a-zA-Z0-9_] with an underscore
            var sanitized = InvalidCharsRegex.Replace(metricName, "_");

            // Ensure the name starts with a letter or underscore (if not, prepend an underscore)
            if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
            {
                sanitized = "_" + sanitized;
            }

            // Replace consecutive underscores with a single underscore
            sanitized = Regex.Replace(sanitized, "_+", "_");

            return sanitized.ToLower();
        }

        /// <summary>
        /// Tries its best to determine the unit of the metric based on the counter information
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static string GetUnit(MonitoringPerformanceData data)
        {
            // The keyword "Capacity" is used in different categories of units, so it is separated from the other keywords.
            if (data.CounterName.IndexOf("Capacity", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var properties = data.GetType().GetProperties();

                foreach (var property in properties)
                {
                    var value = property.GetValue(data)?.ToString();
                    if (value != null && value.IndexOf("bytes", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "_bytes";
                    }
                    else if (value != null && value == "kiloBytes")
                    {
                        return "_kibibytes";
                    }
                    else if (value != null && value == "megaBytes")
                    {
                        return "_mebibytes";
                    }
                    else if (value != null && value == "gigaBytes")
                    {
                        return "_gibibytes";
                    }
                    else if (value != null && value == "percent")
                    {
                        return "_percentage";
                    }
                    else if (value != null && value == "megaHertz")
                    {
                        return "_megahertz";
                    }
                    else if (value != null && value == "microseconds")
                    {
                        return "_percentage";
                    }
                }
            }

            // Check keywords and return corresponding units.
            foreach (var keyword in _timeKeywords)
            {
                if (data.CounterName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (keyword == "IOPS")
                    {
                        return "_seconds";
                    }
                    else if (keyword == "ms" || (keyword == "cpu idle" && data.InstanceName == "millisecond"))
                    {
                        return "_milliseconds";
                    }

                    // Check other properties for further hints on unit-type.
                    // ? Maybe it is enough to use the "InstanceName" property instead of all of them?
                    var properties = data.GetType().GetProperties();

                    foreach (var property in properties)
                    {
                        var value = property.GetValue(data)?.ToString();
                        if (value != null && value.IndexOf("millisecond", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return "_milliseconds";
                        }
                        else if (value != null && value.IndexOf("microsecond", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return "_microseconds";
                        }
                        else if (value != null && value.IndexOf("nanosecond", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return "_nanoseconds";
                        }

                    }

                    return "_seconds";
                }

            }

            foreach (var keyword in _countKeywords)
            {
                if (data.CounterName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "_count";
                }
            }

            foreach (var keyword in _sizeKeywords)
            {
                if (data.CounterName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (keyword == "GB")
                    {
                        return "_gibibytes";
                    }
                    else if (keyword == "Memory")
                    {
                        // Go through other properties to identify the unit.
                        var properties = data.GetType().GetProperties();

                        foreach (var property in properties)
                        {
                            var value = property.GetValue(data)?.ToString();
                            if (value != null && value.IndexOf("kiloBytes", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return "_kibibytes";
                            }
                            else if (value != null && value.IndexOf("megaBytes", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return "_mebibytes";
                            }
                            else if (value != null && value.IndexOf("gigabytes", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return "_gibibytes";
                            }
                            else if (value != null && value.IndexOf("percent", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return "_percentage";
                            }
                        }

                        return "_bytes";
                    }
                    else
                    {
                        return "_bytes";
                    }
                }
            }

            foreach (var keyword in _kbPSKeywords)
            {
                if (data.CounterName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "_kibibytessecond";
                }
            }

            foreach (var keyword in _percentageKeywords)
            {
                if (data.CounterName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "_percentage";
                }
            }

            foreach (var keyword in _rateKeywords)
            {
                if (data.CounterName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (keyword == "Throughput")
                    {
                        return "_bytessecond";
                    }

                    return "_seconds";
                }
            }

            foreach (var keyword in _frequencyKeywords)
            {
                if (data.CounterName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "_megahertz";
                }
            }

            // Go trough help tags and see if we can find clue there aswell.
            if (string.IsNullOrEmpty(data.RuleDescription))
                return "_unknown_unit";

            foreach (var keyword in _ruleDescriptionKeywords)
            {
                if (data.RuleDescription?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (keyword == "IOPS")
                    {
                        return "_seconds";
                    }
                    else if (keyword == "percent")
                    {
                        return "_percentage";
                    }
                    else if (keyword == "maximum")
                    {
                        return "maximum";
                    }
                }
            }

            return "_unknown_unit";
        }
    }
}
