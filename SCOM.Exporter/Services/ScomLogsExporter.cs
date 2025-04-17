using Microsoft.EnterpriseManagement;
using Microsoft.EnterpriseManagement.Monitoring;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prometheus;
using SCOM.Exporter.Utils;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;

namespace SCOM.Exporter
{
    /// <summary>
    /// Retrieve all events and alerts every minute and push them to the Alloy Loki endpoint.
    /// Track the minute by creating a timestamped bookmark file.
    /// </summary>
    public class ScomLogsExporter
    {
        private static readonly ActivitySource _activitySource = new ActivitySource("Opslogix.SCOM.Exporter");

        private readonly ManagementGroup _managementGroup;
        private readonly HttpClient _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:12346"),
        };

        private readonly Dictionary<string, Counter> _counters = new Dictionary<string, Counter>
        {
            {"loki_logs_total_count", Metrics.CreateCounter("loki_logs_total_count", "Total amount of logs sent") },
            {"http_loki_push", Metrics.CreateCounter("http_loki_push", "Http status", new CounterConfiguration
                {
                    LabelNames = new string[]{"status"}
                })
            }
        };

        private readonly Dictionary<string, Gauge> _gauges = new Dictionary<string, Gauge>
        {
            {"loki_logs_last_round_count", Metrics.CreateGauge("loki_logs_last_round_count", "Total amount of logs sent in the previous scrape")},
            {"loki_logs_fetch_duration_ms", Metrics.CreateGauge("loki_logs_fetch_duration", "How long it takes to fetch alerts and events")}
        };

        private readonly ILogger<ScomLogsExporter> _logger;
        private readonly ExporterConfiguration _configuration;

        public ScomLogsExporter(ExporterConfiguration configuration, ILogger<ScomLogsExporter> logger)
        {
            _managementGroup = configuration.ManagementGroup;
            _configuration = configuration;
            _logger = logger;

            //Don't even bother continuing if both events and alerts are disabled.
            if (!_configuration.ShouldExportEvents && !_configuration.ShouldExportAlerts)
                return;

            Task.Run(() => ScrapeLogs());
        }

        private Task<IEnumerable<MonitoringAlert>> GetMonitoringAlerts(DateTime bookmark)
        {
            using (var activity = _activitySource.StartActivity("GetMonitoringAlerts"))
            {
                var criteria = $"TimeRaised >= '{bookmark:yyyy-MM-ddTHH\\:mm\\:ssZ}'";
                activity.SetTag("criteria", criteria);

                return Task.FromResult(_managementGroup.WithReconnect(() => _managementGroup.OperationalData.GetMonitoringAlerts(new MonitoringAlertCriteria(criteria), null)).AsEnumerable());
            }
        }

        private Task<IEnumerable<MonitoringEvent>> GetMonitoringEvents(DateTime bookmark)
        {
            using (var activity = _activitySource.StartActivity("GetMonitoringEvents"))
            {
                var criteria = $"TimeGenerated >= '{bookmark:yyyy-MM-ddTHH\\:mm\\:ssZ}'";
                activity.SetTag("criteria", criteria);

                return Task.FromResult(_managementGroup.WithReconnect(() => _managementGroup.OperationalData.GetMonitoringEvents(new MonitoringEventCriteria(criteria))).AsEnumerable());
            }
        }

        private Task<IEnumerable<LogStream>> ToAlertStreams(IEnumerable<MonitoringAlert> alerts)
        {
            using (var activity = _activitySource.StartActivity("ToAlertStreams"))
            {
                return Task.FromResult(alerts.GroupBy(x => new { x.MonitoringObjectFullName, x.Severity, x.Priority }).Where(x => x.Any()).Select(group =>
                {
                    return new LogStream
                    {
                        Stream = new Dictionary<string, string>
                        {
                            { "type", "alert" },
                            { "monitoring_object_full_path", group.Key.MonitoringObjectFullName },
                            { "severity", group.Key.Severity.ToString() },
                            { "priority", group.Key.Priority.ToString() },
                        },
                        Values = group.Select(alert =>
                        {
                            return new object[]
                            {
                                ((DateTimeOffset)alert.TimeRaised).ToUnixTimeMilliseconds().ToString(),
                                alert.Name,
                                alert
                            };
                        })
                    };
                }));
            }
        }

        private Task<IEnumerable<LogStream>> ToEventStreams(IEnumerable<MonitoringEvent> events)
        {
            using (var activity = _activitySource.StartActivity("ToEventStreams"))
            {
                return Task.FromResult(events.GroupBy(x => new { x.MonitoringObjectFullName, x.Channel, x.LoggingComputer }).Where(x => x.Any()).Select(group =>
                {
                    return new LogStream
                    {
                        Stream = new Dictionary<string, string>
                        {
                            { "type", "event" },
                            { "monitoring_object_full_path", group.Key.MonitoringObjectFullName },
                            { "channel", group.Key.Channel },
                            { "computer", group.Key.LoggingComputer }
                        },
                        Values = group.Select(@event =>
                        {
                            return new object[]
                            {
                                ((DateTimeOffset)@event.TimeGenerated).ToUnixTimeMilliseconds().ToString(),
                                string.IsNullOrEmpty(@event.Description) ? "No Description Available" : @event.Description, //.Replace("\n","")?.Replace("\r",""),
                                @event
                            };
                        })
                    };
                }));
            }
        }

        private async Task<IEnumerable<LogStream>> GetAlertStreams(DateTime bookmark)
        {
            using (var activity = _activitySource.StartActivity("GetAlertStream"))
            {
                var alerts = await GetMonitoringAlerts(bookmark);

                return await ToAlertStreams(alerts);
            }
        }

        private async Task<IEnumerable<LogStream>> GetEventStreams(DateTime bookmark)
        {
            using (var activity = _activitySource.StartActivity("GetEventStreams"))
            {
                var events = await GetMonitoringEvents(bookmark);

                return await ToEventStreams(events);
            }
        }

        private async Task ScrapeLogs()
        {
            var bookmark = GetBookmark();
            var scrapeStopwatch = new Stopwatch();

            while (true)
            {
                scrapeStopwatch.Restart();

                using (var activity = _activitySource.StartActivity("ScrapeLogs"))
                {
                    try
                    {
                        var tasks = new List<Task>();
                        Task<IEnumerable<LogStream>> exportEventsTask = null;
                        Task<IEnumerable<LogStream>> exportAlertsTask = null;

                        if (_configuration.ShouldExportEvents)
                        {
                            activity.AddEvent(new ActivityEvent("Getting events", tags: new ActivityTagsCollection
                            {
                                { "bookmark", bookmark }
                            }));

                            exportEventsTask = GetEventStreams(bookmark);
                            tasks.Add(exportEventsTask);
                        }

                        if (_configuration.ShouldExportAlerts)
                        {
                            activity.AddEvent(new ActivityEvent("Getting alerts", tags: new ActivityTagsCollection
                            {
                                { "bookmark", bookmark }
                            }));

                            exportAlertsTask = GetAlertStreams(bookmark);
                            tasks.Add(exportAlertsTask);
                        }

                        await Task.WhenAll(tasks);

                        _gauges["loki_logs_fetch_duration_ms"].Set(scrapeStopwatch.ElapsedMilliseconds);

                        var alertStreams = exportAlertsTask.Result;
                        var eventStreams = exportEventsTask.Result;

                        var streams = alertStreams.Concat(eventStreams);

                        if (streams.Any())
                        {
                            var totalAmountData = alertStreams.Count() + eventStreams.Count();
                            _gauges["loki_logs_last_round_count"].Set(totalAmountData);
                            _counters["loki_logs_total_count"].Inc(totalAmountData);

                            await PushLogs(streams);
                        }
                    }
                    catch (Exception ex)
                    {
                        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    }
                    finally
                    {
                        bookmark = DateTime.UtcNow;
                        SaveBookmark(bookmark);

                        await Task.Delay(_configuration.ScrapeIntervalSeconds);
                    }
                }
            }
        }

        private async Task PushLogs(IEnumerable<LogStream> streams)
        {
            using (var activity = _activitySource.StartActivity("PushLogs"))
            {
                var logsContent = new LogContent
                {
                    Streams = streams.ToArray()
                };

                HttpResponseMessage pushResponse = null;
                try
                {
                    pushResponse = await _httpClient.PostAsync("/loki/api/v1/push", new JsonGzipContent(logsContent, new JsonSerializerSettings
                    {
                        Converters = new JsonConverter[] { new MonitoringEventJsonLogSerializer(), new MonitoringAlertJsonLogSerializer() }
                    }));

                    pushResponse.EnsureSuccessStatusCode();
                }
                catch (Exception ex)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);

                    string pushResponseContent = string.Empty;
                    if (pushResponse != null)
                    {
                        try
                        {
                            pushResponseContent = await pushResponse.Content.ReadAsStringAsync();
                        }
                        catch
                        {
                            pushResponseContent = "Failed to read response content";
                        }
                    }

                    _logger.LogDebug($"Exception while trying to push logs to alloy loki endpoint. \n\n {pushResponseContent} \n\n {ex}");
                }
                finally
                {
                    _counters["http_loki_push"].WithLabels(new string[] { pushResponse?.StatusCode is null ? "unknown" : pushResponse.StatusCode.ToString() }).Inc();
                }
            }
        }

        private DateTime GetBookmark()
        {
            if (File.Exists("bookmark.txt"))
                return DateTime.Parse(File.ReadAllText("bookmark.txt"));
            else
                return DateTime.UtcNow - TimeSpan.FromSeconds(_configuration.ScrapeIntervalSeconds.TotalSeconds);
        }

        private void SaveBookmark(DateTime bookmark)
        {
            File.WriteAllText("bookmark.txt", bookmark.ToString());
        }
    }

    public class MonitoringAlertJsonLogSerializer : JsonConverter<MonitoringAlert>
    {
        private readonly Type _monitoringAlertType = typeof(MonitoringAlert);
        private readonly PropertyInfo[] _propertiesInfo;

        public MonitoringAlertJsonLogSerializer()
        {
            _propertiesInfo = _monitoringAlertType.GetProperties();
        }

        public override MonitoringAlert ReadJson(JsonReader reader, Type objectType, MonitoringAlert existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, MonitoringAlert value, JsonSerializer serializer)
        {
            var result = new JObject();

            // Convert all the properties to JSON format.
            foreach (var property in _propertiesInfo)
            {
                var propertyValue = property.GetValue(value);
                if (property.PropertyType.IsPrimitive || property.PropertyType.IsValueType || property.PropertyType == typeof(string))
                    result.Add(property.Name, propertyValue is null ? "null" : JToken.FromObject(Convert.ToString(propertyValue)));
            }

            result.WriteTo(writer);
        }
    }

    public class MonitoringEventJsonLogSerializer : JsonConverter<MonitoringEvent>
    {
        private readonly Type _monitoringAlertType = typeof(MonitoringEvent);
        private readonly PropertyInfo[] _propertiesInfo;

        public MonitoringEventJsonLogSerializer()
        {
            _propertiesInfo = _monitoringAlertType.GetProperties();
        }

        public override MonitoringEvent ReadJson(JsonReader reader, Type objectType, MonitoringEvent existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, MonitoringEvent value, JsonSerializer serializer)
        {
            var result = new JObject();

            foreach (var property in _propertiesInfo)
            {
                var propertyValue = property.GetValue(value);
                if (property.PropertyType.IsPrimitive || property.PropertyType.IsValueType || property.PropertyType == typeof(string))
                    result.Add(property.Name, propertyValue is null ? "null" : JToken.FromObject(Convert.ToString(propertyValue)));
            }

            result.WriteTo(writer);
        }
    }

    internal class LogContent
    {
        [JsonProperty("streams")]
        public LogStream[] Streams { get; set; }
    }

    internal class LogStream
    {
        [JsonProperty("stream")]
        public Dictionary<string, string> Stream { get; set; }

        [JsonProperty("values")]
        public IEnumerable<IEnumerable<object>> Values { get; set; }
    }

    internal class JsonGzipContent : HttpContent
    {
        private readonly JsonSerializer _serializer;
        private readonly object _value;

        public JsonGzipContent(object value, JsonSerializerSettings settings = default)
        {
            _value = value;
            Headers.ContentEncoding.Add("gzip");
            Headers.ContentType = new MediaTypeHeaderValue("application/json");

            if (settings is null)
                _serializer = new JsonSerializer();
            else
                _serializer = JsonSerializer.Create(settings);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            using (var gzip = new GZipStream(stream, CompressionMode.Compress, true))
            using (var writer = new StreamWriter(gzip))
                _serializer.Serialize(writer, _value);

            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
