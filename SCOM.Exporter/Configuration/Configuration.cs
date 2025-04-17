using Microsoft.EnterpriseManagement;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;

namespace SCOM.Exporter
{
    /// <summary>
    /// Reads out the appsettings.json and parses the settings. 
    /// Throws errors and/or warnings when validation fails
    /// </summary>
    public class ExporterConfiguration
    {
        private static readonly ActivitySource _activitySource = new ActivitySource("Opslogix.SCOM.Exporter");

        public readonly IEnumerable<Regex> IncludedRules = Enumerable.Empty<Regex>();
        public readonly IEnumerable<Regex> ExcludedRules = Enumerable.Empty<Regex>();
        public readonly TimeSpan ScrapeIntervalSeconds = TimeSpan.FromSeconds(60);
        public readonly ManagementGroup ManagementGroup;
        public readonly ReadOnlyDictionary<string, string> CounterMap;

        public bool ShouldExportMonitors = true;
        public bool ShouldExportMetrics = true;
        public bool ShouldExportAlerts = true;
        public bool ShouldExportEvents = true;

        public ExporterConfiguration(IConfigurationRoot configuration)
        {
            using (var activity = _activitySource.StartActivity("ParseConfiguration"))
            {
                try
                {
                    var managementServer = configuration.GetSection("connection:management_server").Get<string>();
                    if (string.IsNullOrEmpty(managementServer))
                        throw new Exception("Missing required connection setting 'connection:management_server'");

                    var user = configuration.GetSection("connection:user").Get<string>();
                    var password = configuration.GetSection("connection:password").Get<string>();

                    if (string.IsNullOrEmpty(user))
                    {
                        ManagementGroup = new ManagementGroup(managementServer);
                    }
                    else if (!string.IsNullOrEmpty(password))
                    {
                        var p = new SecureString();

                        foreach (char c in password)
                            p.AppendChar(c);

                        ManagementGroup = new ManagementGroup(new ManagementGroupConnectionSettings(managementServer)
                        {
                            UserName = user,
                            Password = p
                        });
                    }

                    activity.AddEvent(new ActivityEvent("Connecting to configured SCOM environment..", tags: new ActivityTagsCollection
                    {
                        { "server", managementServer },
                        { "user", user }
                    }));

                    var includeSection = configuration.GetSection("rules:include").Get<string[]>();
                    var excludeSection = configuration.GetSection("rules:exclude").Get<string[]>();

                    if (includeSection != null && excludeSection != null)
                        throw new Exception("Invalid configuration. Include and Exclude can not both be used at the same time");

                    if (includeSection != null)
                    {
                        activity.AddEvent(new ActivityEvent("Include rules configured"));
                        IncludedRules = includeSection.Select(x => new Regex(x));
                    }

                    if (excludeSection != null)
                    {
                        activity.AddEvent(new ActivityEvent("Exclude rules configured"));
                        ExcludedRules = excludeSection.Select(x => new Regex(x));
                    }

                    var scrapeIntervalSeconds = configuration.GetSection("scrape_interval_seconds").Get<double>();
                    if (scrapeIntervalSeconds > 0)
                    {
                        activity.AddEvent(new ActivityEvent("Scrape interval configuration override", tags: new ActivityTagsCollection
                        {
                            { "interval", scrapeIntervalSeconds }
                        }));
                        ScrapeIntervalSeconds = TimeSpan.FromSeconds(scrapeIntervalSeconds);
                    }

                    var shouldExportMonitors = configuration.GetSection("monitors:enabled").Get<bool?>();
                    if (shouldExportMonitors.HasValue)
                    {
                        activity.AddEvent(new ActivityEvent("Monitor exporting enabled override", tags: new ActivityTagsCollection
                        {
                            { "value", shouldExportMonitors.Value}
                        }));
                        ShouldExportMonitors = shouldExportMonitors.Value;
                    }

                    var shouldExportMetrics = configuration.GetSection("rules:enabled").Get<bool?>();
                    if (shouldExportMetrics.HasValue)
                    {
                        activity.AddEvent(new ActivityEvent("Rules/Metrics exporting enabled override", tags: new ActivityTagsCollection
                        {
                            { "value", shouldExportMetrics.Value}
                        }));
                        ShouldExportMetrics = shouldExportMetrics.Value;
                    }

                    var shouldExportEvents = configuration.GetSection("events:enabled").Get<bool?>();
                    if (shouldExportEvents.HasValue)
                    {
                        activity.AddEvent(new ActivityEvent("Event exporting enabled override", tags: new ActivityTagsCollection
                        {
                            { "value", shouldExportEvents.Value}
                        }));
                        ShouldExportEvents = shouldExportEvents.Value;

                    }

                    var shouldExportAlerts = configuration.GetSection("alerts:enabled").Get<bool?>();
                    if (shouldExportAlerts.HasValue)
                    {
                        activity.AddEvent(new ActivityEvent("Alert exporting enabled override", tags: new ActivityTagsCollection
                        {
                            { "value", shouldExportAlerts.Value}
                        }));
                        ShouldExportAlerts = shouldExportAlerts.Value;
                    }

                    var counterMap = configuration.GetSection("rules:map").Get<Dictionary<string, string>>();
                    if (counterMap.Any())
                    {
                        activity.AddEvent(new ActivityEvent("Rules counter map found"));
                        var values = new HashSet<string>();
                        foreach (var kvp in counterMap)
                        {
                            if (!values.Add(kvp.Value))
                                throw new Exception($"Duplicate value found in rules counter mapping: '{kvp.Value}'");
                        }

                        CounterMap = new ReadOnlyDictionary<string, string>(counterMap.ToDictionary(k => k.Key.ToLower(), v => v.Value));
                    }
                }
                catch (Exception ex)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }
            }
        }
    }
}
