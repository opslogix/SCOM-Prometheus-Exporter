using Microsoft.EnterpriseManagement.Common;
using Prometheus;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace SCOM.Exporter.Controllers
{
    [RoutePrefix("metrics")]
    public class MetricsController : ApiController
    {
        private static readonly ActivitySource _activitySource = new ActivitySource("Opslogix.SCOM.Exporter");
        private static readonly Dictionary<string, Counter> _counters = new Dictionary<string, Counter> {
            {"process_scrape_request_count", Metrics.CreateCounter("process_scrape_request_count", "number of alloy scrape requests since uptime") }
        };

        private readonly ScomMetricsCollector _collector;

        public MetricsController(ScomMetricsCollector collector)
        {
            _collector = collector;
        }

        [HttpGet, Route("{group?}")]
        public async Task<HttpResponseMessage> Get(string group = default, [FromUri]TraversalDepth depth = TraversalDepth.OneLevel,CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder("/metrics");
            if (!string.IsNullOrEmpty(group))
                sb.Append($"/{group}");

            using (var activity = _activitySource.StartActivity(sb.ToString()))
            {
                _counters["process_scrape_request_count"].Inc();
                var response = new HttpResponseMessage(HttpStatusCode.OK);

                try
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await _collector.GetMetrics(memoryStream, group);
                        memoryStream.Position = 0;
                        var metricsText = new StreamReader(memoryStream).ReadToEnd();
                        response.Content = new StringContent(metricsText, Encoding.UTF8, "text/plain");
                    }
                }
                catch (Exception ex)
                {
                    activity.SetStatus(ActivityStatusCode.Error);
                    response.StatusCode = HttpStatusCode.InternalServerError;
                    response.Content = new StringContent(ex.Message);
                }

                return response;
            }
        }
    }
}