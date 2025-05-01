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

Configuration is done using the appsettings.json. Basic examples/comments can be found inside the provided appsettings.json. The minimum that needs to be configured is the management_server property inside the connection section. Optionally you can provide a username and password to sign in to the SCOM SDK as a specific user.

The appsettings will also allow you to turn on/off exporting of alerts, events, monitors and rules.

For rules we do our best to translate rules into metric names that confine to the prometheus standard, this however more often then not exports metrics with a name where the unit is incorrect or even unknown. You can use the appsettings.json and configure the rules -> map property where you can map rule names to specific counter names.

You can also limit what rules should be included or excluded by using regex patterns.

## Final steps
Whether its running as a windows service or using the debug.exe, if everything is configured correctly you should be able to reach the exporter endpoint by navigating to http://localhost:3005/metrics

For SCOM metrics use the following pattern http://localhost:3005/metrics/{GROUP} where {GROUP} can be any SCOM group in your environment. For viewing ALL exported SCOM metrics replace group with "all" e.g. http://localhost:3005/metrics/all


If you can see the exported metrics on the endpoint the exporter is working correctly.