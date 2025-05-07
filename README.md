# SCOM-Prometheus-Exporter

This project bridges the gap between Microsoft System Center Operations Manager (SCOM) and modern observability platforms such as Grafana, Prometheus, and Grafana Alloy. The **SCOM Exporter** enables you to export performance data from SCOM and visualize it using open source observability tools.

## Overview

SCOM is a well-established monitoring tool for enterprise environments, but it's often isolated from modern observability stacks. This exporter helps bring SCOM data into platforms like Grafana, enabling hybrid monitoring strategies and smoother transitions to modern architectures.

## Key Features

- Export performance data from SCOM to modern observability tools.
- Integrate with both Prometheus and Grafana Alloy.
- Flexible configuration of what to export (metrics, counters, objects).
- Lightweight service that runs alongside your monitoring stack.

## Supported Backends

- Prometheus
- Grafana Alloy

Both Prometheus and Grafana Alloy can scrape the metrics exposed by this exporter. You can then visualize the data using Grafana dashboards.

## Architecture

SCOM → SCOM Exporter → Prometheus or Grafana Alloy → Grafana

## Getting Started

### Prerequisites

- Microsoft SCOM 2019 or later
- .NET 4.7.2 or higher on the exporter host
- Either Prometheus or Grafana Alloy
- Grafana instance

### Installation Steps

Installation can be done by using SC. E.G. 'sc create "ScomExporter" start=auto binpath="path/scom.exporter.service.exe"'

## Configuration

<details>
<summary>Click to expand</summary>

```json
{
  "connection": {
    "management_server": "mymanagementserver.com",
    //User and password is not required but can be used to sign in as a specific user.
    //"user": "",
    //"password": ""
  },
  "scrape_interval_seconds": 60,
  "alerts": {
    "enabled": true
  },
  "events": {
    "enabled": true
  },
  "monitors": {
    "enabled": true
  },
  "rules": {
    "enabled": true,
    "include": [
      //"Microsoft.SystemCenter.*"
    ],
    "exclude": [
      //"Microsoft.SystemCenter.*"
    ],
    "map": {
      //"the_scom_rule_name": "my_exported_counter_name"
    }
  }
}
```

</details>

### Key Settings

- **`connection.management_server`**: The hostname or IP address of your SCOM management server.
- **`connection.user` / `password`** *(optional)*: Use these if connecting with specific credentials is required.
- **`scrape_interval_seconds`**: Interval in seconds at which metrics are collected and exported.
- **`alerts.enabled`**: Set to `true` to export alerts as Loki logs.
- **`events.enabled`**: Set to `true` to export events as Loki logs.
- **`monitors.enabled`**: Set to `true` to export monitors as Prometheus gauges or histograms.
- **`rules.enabled`**: Set to `true` to export rule-based performance metrics.

#### `rules.include` (optional)

A list of regular expressions for rule names to include. Cannot be used with `exclude`.

#### `rules.exclude` (optional)

A list of regular expressions for rule names to exclude. Cannot be used with `include`.

#### `rules.map` (optional)

A dictionary mapping SCOM rule names to Prometheus metric names. Keys and values must be unique. Metric names must match the regex: `^[a-zA-Z_][a-zA-Z0-9_]*$`.

For rules we do our best to translate rules into metric names that confine to the prometheus standard, this however more often then not exports metrics with a name where the unit is incorrect or even unknown. You can use the appsettings.json and configure the rules -> map property where you can map rule names to specific counter names.

---

## ✅ Final Steps

Whether you're running the exporter as a Windows service or via `debug.exe`, you can verify everything is working by checking the metrics endpoint:

- **Base Exporter Endpoint**  
  [http://localhost:3005/metrics](http://localhost:3005/metrics)

### 🔍 Accessing SCOM Metrics

You can query SCOM-specific metrics by targeting a particular group:

```
http://localhost:3005/metrics/{GROUP}
```

Replace `{GROUP}` with the name of any valid SCOM group in your environment.

To view **all available SCOM metrics**, use:

```
http://localhost:3005/metrics/all
```

---

If you can successfully access these endpoints and see metrics output, your exporter is configured and running correctly
