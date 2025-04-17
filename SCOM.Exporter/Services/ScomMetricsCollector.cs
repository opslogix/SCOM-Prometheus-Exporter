using Microsoft.EnterpriseManagement;
using Microsoft.EnterpriseManagement.Common;
using Microsoft.EnterpriseManagement.Configuration;
using Microsoft.EnterpriseManagement.Monitoring;
using Microsoft.Extensions.Logging;
using Prometheus;
using SCOM.Exporter.Models;
using SCOM.Exporter.Utils;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SCOM.Exporter
{
    /// <summary>
    /// This collector will poll the provided management server for rules and performance data.
    /// </summary>
    public class ScomMetricsCollector
    {
        private readonly ManagementGroup _managementGroup;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private static readonly ActivitySource _activitySource = new ActivitySource("Opslogix.SCOM.Exporter");
        private static readonly string[] _metricLabels = new string[] { "instance_path", "tag" };
        private static readonly string[] _monitorLabels = new string[] { "instance_path" };

        private readonly ExporterConfiguration _configuration;
        private readonly ILogger _logger;

        private ScomPerformance _cachedMetrics;

        private IEnumerable<ManagementPackRule> _rules = Enumerable.Empty<ManagementPackRule>();
        private IEnumerable<ManagementPackClass> _classes = Enumerable.Empty<ManagementPackClass>();
        private IEnumerable<MonitoringObjectGroup> _groups = Enumerable.Empty<MonitoringObjectGroup>();
        private IEnumerable<ManagementPackMonitor> _monitors = Enumerable.Empty<ManagementPackMonitor>();
        private IEnumerable<MonitoringObject> _instances = Enumerable.Empty<MonitoringObject>();

        private Dictionary<MonitoringObject, IList<MonitoringState>> _instanceStates = new Dictionary<MonitoringObject, IList<MonitoringState>>();
        private Dictionary<MonitoringObjectGroup, IEnumerable<MonitoringObject>> _groupedMonitoringObjects = new Dictionary<MonitoringObjectGroup, IEnumerable<MonitoringObject>>();

        private readonly Dictionary<string, CollectorRegistry> _registries = new Dictionary<string, CollectorRegistry>
        {
            { "all", Metrics.NewCustomRegistry() }
        };

        private readonly Dictionary<string, Dictionary<string, Gauge>> _registryMetrics = new Dictionary<string, Dictionary<string, Gauge>>
        {
            { "all", new Dictionary<string, Gauge>() }
        };

        private Task _getScomConfigurationTask;

        public ScomMetricsCollector(ExporterConfiguration configuration, ILogger<ScomMetricsCollector> logger)
        {
            _managementGroup = configuration.ManagementGroup;
            _logger = logger;
            _configuration = configuration;

            //Kick of the scom configuration polling task
            _ = PollScomConfiguration();
            //Kick of the scraping task
            _ = ScrapeMetrics();

        }

        /// <summary>
        /// Populates the provided stream with SCOM metrics
        /// </summary>
        /// <param name="stream">Stream to push the results to</param>
        /// <param name="group">A SCOM monitoring group for filtering the results. If no group is provided this method will return only internal metrics</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task GetMetrics(Stream stream, string group = default)
        {
            using (var activity = _activitySource.StartActivity("GetMetrics"))
            {
                activity.SetTag("group", group);

                // Return internal metrics only
                if (string.IsNullOrEmpty(group))
                {
                    await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream);
                    return;
                }

                if (_cachedMetrics == null && _configuration.ShouldExportMetrics)
                    throw new Exception("Metric collection has not been completed yet.");

                var metrics = _cachedMetrics?.Response ?? Enumerable.Empty<ScomPerformanceResponse>();
                var registry = _registries["all"];
                var gauges = _registryMetrics["all"];
                var instanceStates = _instanceStates;

                if (!group.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    var monitoringGroup = _groups.FirstOrDefault(x => x.FullName.Equals(group, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"No monitoring group found with full name '{group}'");

                    if (!_groupedMonitoringObjects.TryGetValue(monitoringGroup, out var monitoringGroupObjects))
                        throw new Exception($"No monitoring objects found for group with full name '{group}'");

                    // Create or get the custom registry for the requested group
                    if (!_registries.TryGetValue(group, out var groupRegistry))
                    {
                        registry = Metrics.NewCustomRegistry();
                        _registries.Add(group, registry);
                    }
                    else
                    {
                        registry = groupRegistry;
                    }

                    // Get gauges/counters for the registry
                    if (!_registryMetrics.TryGetValue(group, out var registryGauges))
                    {
                        gauges = new Dictionary<string, Gauge>();
                        _registryMetrics.Add(group, gauges);
                    }
                    else
                    {
                        gauges = registryGauges;
                    }

                    // Get metrics for group only
                    instanceStates = _instanceStates?.Where(x => monitoringGroupObjects.Any(y => y.Id == x.Key.Id)).ToDictionary(x => x.Key, v => v.Value);
                    metrics = _cachedMetrics.Response.Where(x => monitoringGroupObjects.Any(y => x.Values.FirstOrDefault()?.Data.MonitoringObjectId == y.Id)).ToList();
                }

                var test = metrics.FirstOrDefault(x => x.MetricName == "scom_opslogix_imp_vmware_datastore_datastore_free_space_percentage");

                var tasks = new List<Task>
                {
                    Task.Run(() =>
                    {
                        foreach (var metric in metrics)
                        {
                            if (!gauges.TryGetValue(metric.MetricName, out var gauge))
                            {
                                var metricName = metric.MetricName;

                                //Check if there is a mapping for this metric/rule
                                if(_configuration.CounterMap.TryGetValue(metric.Rule.Name.ToLower(), out string mappedMetricName))
                                    metricName = mappedMetricName;

                                gauge = Metrics.WithCustomRegistry(registry).CreateGauge(metricName, metric.MetricDescription, new GaugeConfiguration
                                {
                                    LabelNames = _metricLabels
                                });

                                gauges[metric.MetricName] = gauge;
                            }

                            foreach(var metricValue in metric.Values)
                            {
                                if (metricValue is null || !metricValue.Value.SampleValue.HasValue)
                                    continue;

                                var monitoringObjectPath = metricValue.Data.MonitoringObjectPath;
                                var monitoringObjectDisplayName = metricValue.Data.MonitoringObjectDisplayName;
                                var instanceName = metricValue.Data.InstanceName;
                                var label = monitoringObjectPath == null ? _managementGroup.Name : $"{monitoringObjectPath}/{monitoringObjectDisplayName}";

                                gauge.WithLabels(label, instanceName).Set(metricValue.Value.SampleValue.Value);
                            }
                        }
                    }),

                    Task.Run(() =>
                    {
                        foreach (var instance in instanceStates)
                        {
                            var monitorGauges = new Dictionary<Guid, Gauge>();

                            foreach (var monitorState in instance.Value)
                            {
                                var monitor = _monitors.FirstOrDefault(x => x.Id == monitorState.MonitorId);
                                if (monitor == null)
                                    continue;

                                if (!monitorGauges.TryGetValue(monitor.Id, out var gauge))
                                {
                                    gauge = Metrics.WithCustomRegistry(registry).CreateGauge(Counters.SanitizeMetricName($"scom_monitor_{monitor.Name}_state"), string.IsNullOrEmpty(monitor.Description) ? string.Empty : $"{monitor.Description.Replace("\r\n", " ").Replace("\n", " ")} (0=UNINITIALIZED, 1=HEALTHY, 2=WARNING, 3=ERROR)", new GaugeConfiguration
                                    {
                                        LabelNames = _monitorLabels
                                    });
                                    monitorGauges[monitor.Id] = gauge;
                                }

                                gauge.WithLabels(instance.Key.Path ?? _managementGroup.Name).Set((double)monitorState.HealthState);
                            }
                        }
                    })
                };

                await Task.WhenAll(tasks);

                await registry.CollectAndExportAsTextAsync(stream);
            }
        }

        private Task<IEnumerable<ManagementPackClass>> GetClasses(IEnumerable<ManagementPackRule> rules)
        {
            using (var activity = _activitySource.StartActivity("GetClasses"))
            {
                return Task.FromResult(_managementGroup.WithReconnect(() => _managementGroup.EntityTypes.GetClasses(new ManagementPackClassCriteria($"Id IN ({string.Join(",", rules.Select(x => $"'{x.Target.Id}'"))})"))).AsEnumerable());
            }
        }

        private Task<IEnumerable<ManagementPackRule>> GetRules()
        {
            using (var activity = _activitySource.StartActivity("GetRules"))
            {
                var perfRules = _managementGroup.WithReconnect(() => _managementGroup.Monitoring.GetRules().Where(x => x.Category == ManagementPackCategoryType.PerformanceCollection && x.Enabled == ManagementPackMonitoringLevel.@true));

                if (_configuration.IncludedRules.Any())
                    perfRules = perfRules.Where(x => _configuration.IncludedRules.Any(y => y.IsMatch(x.Name)));

                if (_configuration.ExcludedRules.Any())
                    perfRules = perfRules.Where(x => _configuration.ExcludedRules.Any(y => !y.IsMatch(x.Name)));

                return Task.FromResult(perfRules.AsEnumerable());
            }
        }

        private Task<IEnumerable<MonitoringObjectGroup>> GetGroups()
        {
            using (var activity = _activitySource.StartActivity("GetGroups"))
            {
                return Task.FromResult(_managementGroup.WithReconnect(() => _managementGroup.EntityObjects.GetRootObjectGroups<MonitoringObjectGroup>(new ObjectQueryOptions
                {
                    DefaultPropertyRetrievalBehavior = ObjectPropertyRetrievalBehavior.All
                })).AsEnumerable());
            }
        }

        private async Task<IEnumerable<MonitoringObject>> GetMonitoringObjects(IEnumerable<ManagementPackClass> classes)
        {
            using (var activity = _activitySource.StartActivity("GetMonitoringObjects"))
            {
                var getInstancesTasks = new List<Task<IEnumerable<MonitoringObject>>>();
                var instances = new List<MonitoringObject>();

                foreach (var @class in classes)
                    getInstancesTasks.Add(Task.Run(() =>
                    {
                        using (var activity2 = _activitySource.StartActivity("GetMonitoringObjectsForClass"))
                        {
                            var monitoringObjects = _managementGroup.WithReconnect(() => _managementGroup.EntityObjects.GetObjectReader<MonitoringObject>(@class, ObjectQueryOptions.Default).AsEnumerable());

                            return monitoringObjects;
                        }
                    }));

                await Task.WhenAll(getInstancesTasks);

                return getInstancesTasks.SelectMany(x => x.Result);
            }
        }

        private async Task GetScomConfiguration()
        {
            await _semaphore.WaitAsync();

            using (var activity = _activitySource.StartActivity("GetScomConfiguration"))
            {
                try
                {
                    _rules = await GetRules();
                    _groups = await GetGroups();
                    _classes = await GetClasses(_rules);
                    _instances = await GetMonitoringObjects(_classes);
                    _monitors = _managementGroup.WithReconnect(() => _managementGroup.Monitoring.GetMonitors());

                    _groupedMonitoringObjects = _groups.ToDictionary(x => x, x =>
                    {
                        var monitoringObjects = _managementGroup.WithReconnect(() => x.GetMostDerivedClasses().SelectMany(y => _managementGroup.EntityObjects.GetRelatedObjects<MonitoringObject>(y.Id, TraversalDepth.Recursive, ObjectQueryOptions.Default)).ToList());
                        return monitoringObjects.AsEnumerable();
                    });

                    InternalMetrics.Gauges[InternalMetrics.PROCESS_SCOM_INSTANCES_COUNT_NAME].Set(_instances.Count());
                    InternalMetrics.Gauges[InternalMetrics.PROCESS_SCOM_RULES_COUNT_NAME].Set(_rules.Count());
                    InternalMetrics.Gauges[InternalMetrics.PROCESS_SCOM_CLASSES_COUNT_NAME].Set(_classes.Count());
                    InternalMetrics.Gauges[InternalMetrics.PROCESS_SCOM_GROUPS_COUNT_NAME].Set(_groups.Count());
                    InternalMetrics.Counters[InternalMetrics.PROCESS_SCOM_CONFIGURATION_REFRESH_COUNT_NAME].Inc();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while retrieving SCOM configuration");
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }

        private async Task PollScomConfiguration()
        {
            while(true)
            {
                if(_getScomConfigurationTask != null) 
                    await _getScomConfigurationTask;

                _getScomConfigurationTask = GetScomConfiguration();
                await Task.Delay(TimeSpan.FromMinutes(5));
            }
        }

        private Task<KeyValuePair<MonitoringPerformanceData, MonitoringPerformanceDataValue>> GetMonitoringPerformanceDataValue(MonitoringPerformanceData monitoringPerformanceData, DateTime start, DateTime end)
        {
            using (var activity = _activitySource.StartActivity("GetMonitoringPerformanceDataValues"))
            {
                activity.SetTag("scom_counter_name", monitoringPerformanceData.CounterName);
                activity.SetTag("scom_rule_display_name", monitoringPerformanceData.RuleDisplayName);
                activity.SetTag("scom_instance_name", monitoringPerformanceData.InstanceName);

                var result = default(KeyValuePair<MonitoringPerformanceData, MonitoringPerformanceDataValue>);

                try
                {
                    var values = monitoringPerformanceData.GetValues(start, end);
                    if (values.Any())
                        result = new KeyValuePair<MonitoringPerformanceData, MonitoringPerformanceDataValue>(monitoringPerformanceData, values.Last());
                }
                catch (Exception ex)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                }

                return Task.FromResult(result);
            }
        }

        private Task<MonitoringPerformanceDataReader> GetMonitoringPerformanceDataReader(IEnumerable<ManagementPackRule> rules)
        {
            using (var activity = _activitySource.StartActivity("GetMonitoringPerformanceDataReader"))
            {
                MonitoringPerformanceDataReader result = default;

                try
                {
                    var criteria = $"MonitoringRuleId IN ({string.Join(",", rules.Select(x => $"'{x.Id}'"))})";
                    result = _managementGroup.WithReconnect(() => _managementGroup.OperationalData.GetMonitoringPerformanceDataReader(new MonitoringPerformanceDataCriteria(criteria)));
                }
                catch (Exception ex)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }

                return Task.FromResult(result);
            }
        }

        private async Task<ScomPerformance> CollectMetrics()
        {
            using (var activity = _activitySource.StartActivity("CollectMetrics"))
            using (var timer = InternalMetrics.Histograms[InternalMetrics.PROCESS_SCOM_METRICS_SCRAPE_DURATION_NAME].NewTimer())
            {
                ScomPerformance result;

                try
                {
                    var reader = await GetMonitoringPerformanceDataReader(_rules);
                    var tasks = new List<Task<KeyValuePair<MonitoringPerformanceData, MonitoringPerformanceDataValue>>>();

                    var start = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(10000));
                    var end = DateTime.UtcNow;

                    activity.SetTag("start_time", start);
                    activity.SetTag("end_time", end);

                    while (reader.Read())
                    {
                        var monitoringPerformance = reader.GetMonitoringPerformanceData();
                        tasks.Add(GetMonitoringPerformanceDataValue(monitoringPerformance, start, end));
                    }

                    var results = (await Task.WhenAll(tasks))
                        .Where(r => r.Key != null || r.Value != null)
                        .ToDictionary(r => r.Key, r => r.Value);

                    result = new ScomPerformance
                    {
                        Request = new ScomPerformanceRequest
                        {
                            Start = start,
                            End = end
                        },
                        Response = results.GroupBy(x => new { x.Key.RuleId, x.Key.ClassId }).Select(x =>
                        {
                            var rule = _rules.FirstOrDefault(y => y.Id == x.Key.RuleId);
                            var @class = _classes.FirstOrDefault(y => y.Id == rule.Target.Id);
                            return new ScomPerformanceResponse
                            {
                                Rule = rule,
                                Class = @class,
                                Values = x.Select(y => new ScomPerformanceResponseValue
                                {
                                    Data = y.Key,
                                    Value = y.Value
                                })
                            };
                        })
                    };
                }
                catch (Exception ex)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }

                return result;
            }
        }

        private async Task ScrapeMetrics()
        {
            var scrapeStopwatch = new Stopwatch();

            while (true)
            {
                await _semaphore.WaitAsync();

                scrapeStopwatch.Restart();

                using (var activity = _activitySource.StartActivity("ScrapeMetrics"))
                {
                    activity.SetTag("scrape_interval_seconds", _configuration.ScrapeIntervalSeconds.TotalSeconds.ToString());
                    activity.SetTag("rule_count", _rules.Count().ToString());
                    activity.SetTag("class_count", _classes.Count().ToString());
                    activity.SetTag("group_count", _groups.Count().ToString());

                    try
                    {
                        if (_getScomConfigurationTask != null)
                            await _getScomConfigurationTask;

                        //Only retrieve metrics if configured
                        if (_configuration.ShouldExportMetrics)
                            _cachedMetrics = await CollectMetrics();

                        //Only retrieve instance states if its configured.
                        if (_configuration.ShouldExportMonitors)
                            _instanceStates = _managementGroup.WithReconnect(() => _managementGroup.EntityObjects.GetStateForObjects(_instances, _monitors));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error while scraping");
                        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    }
                    finally
                    {
                        scrapeStopwatch.Stop();

                        activity.SetTag("scrape_duration_seconds", scrapeStopwatch.Elapsed.TotalSeconds.ToString());
                        bool isScrapeDurationOverlapping = scrapeStopwatch.Elapsed > _configuration.ScrapeIntervalSeconds;
                        if (isScrapeDurationOverlapping)
                            _logger.LogWarning("SCOM scrape duration is overlapping with scrape interval. Scrape duration: {ScrapeDuration}, Scrape interval: {ScrapeInterval}", scrapeStopwatch.Elapsed.TotalSeconds, _configuration.ScrapeIntervalSeconds.TotalSeconds);

                        activity.SetTag("scrape_duration_is_overlapping", isScrapeDurationOverlapping.ToString());

                        InternalMetrics.Counters[InternalMetrics.PROCESS_SCOM_METRICS_SCRAPE_REQUEST_COUNT_NAME].Inc();
                        InternalMetrics.Gauges[InternalMetrics.PROCESS_SCOM_METRICS_SCRAPE_LAST_NAME].SetToCurrentTimeUtc();

                        _semaphore.Release();
                        var remaining = _configuration.ScrapeIntervalSeconds - scrapeStopwatch.Elapsed;
                        await Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                    }
                }
            }
        }
    }
}