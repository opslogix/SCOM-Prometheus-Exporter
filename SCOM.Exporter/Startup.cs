using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Owin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Http;
using System.Web.Http.Dependencies;

namespace SCOM.Exporter
{
    public class Startup
    {
        private static ExporterConfiguration _configuration;

        public static void Configuration(IAppBuilder appBuilder)
        {
            // Configuration for tracing
            var serviceName = "Opslogix-SCOM-Exporter";
            var serviceVersion = "0.0.1";
            var tracerBuilder = Sdk.CreateTracerProviderBuilder()
            .AddSource("Opslogix.SCOM.Exporter")
            .ConfigureResource(resource =>
                resource.AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion))
            .AddOtlpExporter();

#if DEBUG
            tracerBuilder.AddConsoleExporter();
#endif
            tracerBuilder.Build();

            // Meter provider for tracing
            var meterProviderBuilder = Sdk.CreateMeterProviderBuilder()
            .AddMeter(serviceName)
            .ConfigureResource(resource =>
                resource.AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion));

#if DEBUG
            meterProviderBuilder.AddConsoleExporter();
#endif

            var meterProvider = meterProviderBuilder.Build();

            var config = new HttpConfiguration();
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging(logging =>
            {
                logging.ClearProviders();

#if DEBUG
                logging.AddSimpleConsole();
#endif
                logging.AddOpenTelemetry(options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                    options.AddConsoleExporter();
                    options.AddOtlpExporter(configure =>
                    {
                        configure.Endpoint = new Uri("http://localhost:4317");
                    });
                });
            });

            var builder = new ConfigurationBuilder()
              .SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            var configuration = builder.Build();
            _configuration = new ExporterConfiguration(configuration);

            serviceCollection.AddSingleton(_configuration);
            serviceCollection.AddSingleton<ScomMetricsCollector>();
            serviceCollection.AddSingleton<ScomLogsExporter>();

            foreach (var controller in Assembly.GetAssembly(typeof(Startup)).GetTypes().Where(type => type.IsSubclassOf(typeof(ApiController))))
                serviceCollection.AddTransient(controller);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            config.DependencyResolver = new DefaultDependencyResolver(serviceProvider);
            config.MapHttpAttributeRoutes();

            //boot up the metrics collector and log exporter
            serviceProvider.GetRequiredService<ScomMetricsCollector>();
            serviceProvider.GetRequiredService<ScomLogsExporter>();

            appBuilder.UseWebApi(config);
        }
    }

    public class DefaultScopeResolver : IDependencyScope
    {
        private readonly IServiceScope _scope;

        public DefaultScopeResolver(IServiceProvider serviceProvider) => _scope = serviceProvider.CreateScope();

        public void Dispose()
        {
            _scope.Dispose();
            GC.SuppressFinalize(this);
        }

        public object GetService(Type serviceType)
        {
            return _scope.ServiceProvider.GetService(serviceType);
        }

        public IEnumerable<object> GetServices(Type serviceType)
        {
            return _scope.ServiceProvider.GetServices(serviceType);
        }
    }

    public class DefaultDependencyResolver : IDependencyResolver
    {
        public IServiceProvider ServiceProvider { get; }

        public DefaultDependencyResolver(IServiceProvider serviceProvider) => ServiceProvider = serviceProvider;

        public IDependencyScope BeginScope() => new DefaultScopeResolver(ServiceProvider);

        public object GetService(Type serviceType) => ServiceProvider.GetService(serviceType);

        public IEnumerable<object> GetServices(Type serviceType) => ServiceProvider.GetServices(serviceType);

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}