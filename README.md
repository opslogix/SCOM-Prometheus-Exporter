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
- .NET 6 or higher on the exporter host
- Either Prometheus or Grafana Alloy
- Grafana instance

### Installation Steps

1. Clone the repository:


