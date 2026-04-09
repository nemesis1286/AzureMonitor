# Azure Synthetic Probe Health Checks

An Azure Function that performs synthetic availability checks against private resources (VNet-internal) and reports telemetry to Azure Monitor / Application Insights via `TelemetryClient.TrackAvailability()`.

Azure's built-in availability tests run from public Microsoft endpoints and cannot reach private resources. This solution runs inside your VNet to probe internal APIs, databases, caches, and other services that are not exposed to the internet.

Results appear in the Application Insights **Availability** blade as "CUSTOM" tests, enabling dashboards and alerting.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Azure VNet                                                 │
│                                                             │
│  ┌──────────────────────┐     ┌──────────────────────────┐  │
│  │  Azure Function      │     │  Private Resources       │  │
│  │  (Timer Trigger)     │────>│  - Internal APIs         │  │
│  │                      │     │  - SQL Server (TCP 1433) │  │
│  │  Runs every 5 min    │     │  - Redis (TCP 6380)      │  │
│  │  via CRON schedule   │     │  - Storage Accounts      │  │
│  └──────────┬───────────┘     └──────────────────────────┘  │
│             │                                               │
└─────────────┼───────────────────────────────────────────────┘
              │ TrackAvailability()
              ▼
   ┌─────────────────────┐     ┌──────────────────────┐
   │ Application Insights │────>│ Availability Blade   │
   │                      │     │ Alerts & Dashboards  │
   └─────────────────────┘     └──────────────────────┘
```

## Probe Types

| Type | Use Case | How It Works |
|------|----------|--------------|
| **HTTP** | REST APIs, health endpoints, web apps | Sends GET request, validates status code and optionally response body |
| **TCP** | Databases, Redis, message brokers, any TCP service | Opens a TCP connection to host:port, reports success if connection established |

## Project Structure

```
AzureMonitor/
├── AzureMonitor.sln
├── src/AzureMonitor.SyntheticProbes/
│   ├── Program.cs                        # DI, App Insights, HttpClient setup
│   ├── Functions/SyntheticProbeFunction.cs  # Timer-triggered entry point
│   ├── Models/
│   │   ├── ProbeEndpoint.cs              # Endpoint configuration model
│   │   ├── ProbeResult.cs                # Probe execution result
│   │   └── ProbeConfiguration.cs         # Root configuration
│   ├── Services/
│   │   ├── HttpHealthProbe.cs            # HTTP/HTTPS probe implementation
│   │   ├── TcpHealthProbe.cs             # TCP port probe implementation
│   │   ├── ProbeOrchestrator.cs          # Parallel probe execution
│   │   └── AvailabilityReporter.cs       # App Insights telemetry reporting
│   ├── host.json
│   ├── appsettings.json                  # Probe endpoint definitions
│   └── local.settings.json               # Local dev settings (connection strings)
└── tests/AzureMonitor.SyntheticProbes.Tests/
    └── ...                               # Unit tests (xUnit + Moq)
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- An Azure Application Insights resource (for the connection string)
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) for local development (or an Azure Storage account)

## Getting Started

### 1. Clone and Build

```bash
git clone <repository-url>
cd AzureMonitor
dotnet build
```

### 2. Configure Local Settings

Edit `src/AzureMonitor.SyntheticProbes/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "APPLICATIONINSIGHTS_CONNECTION_STRING": "<your-app-insights-connection-string>",
    "PROBE_SCHEDULE": "0 */5 * * * *",
    "REGION_NAME": "Local-Dev"
  }
}
```

| Setting | Description |
|---------|-------------|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | From your App Insights resource > Overview > Connection String |
| `PROBE_SCHEDULE` | CRON expression for probe frequency. `0 */5 * * * *` = every 5 minutes |
| `REGION_NAME` | Identifies where probes run from. Auto-set by Azure in deployed environments |

### 3. Configure Probe Endpoints

Edit `src/AzureMonitor.SyntheticProbes/appsettings.json`:

```json
{
  "ProbeConfiguration": {
    "RunLocation": "East US",
    "DefaultTimeoutSeconds": 30,
    "Endpoints": [
      {
        "Name": "Internal API Health",
        "ProbeType": "Http",
        "Url": "https://internal-api.private.contoso.com/health",
        "ExpectedStatusCode": 200,
        "TimeoutSeconds": 15,
        "Headers": {
          "X-Health-Check": "synthetic-probe"
        },
        "ExpectedResponseBodyContains": "Healthy"
      },
      {
        "Name": "SQL Server",
        "ProbeType": "Tcp",
        "Host": "sql-server.private.contoso.com",
        "Port": 1433,
        "TimeoutSeconds": 10
      }
    ]
  }
}
```

#### HTTP Endpoint Options

| Property | Required | Default | Description |
|----------|----------|---------|-------------|
| `Name` | Yes | | Display name in App Insights Availability blade |
| `ProbeType` | Yes | | Must be `Http` |
| `Url` | Yes | | Full URL to probe |
| `ExpectedStatusCode` | No | `200` | HTTP status code that indicates success |
| `TimeoutSeconds` | No | `30` | Max seconds to wait for response |
| `Headers` | No | | Custom HTTP headers to include in the request |
| `ExpectedResponseBodyContains` | No | | Substring that must appear in the response body (case-insensitive) |

#### TCP Endpoint Options

| Property | Required | Default | Description |
|----------|----------|---------|-------------|
| `Name` | Yes | | Display name in App Insights Availability blade |
| `ProbeType` | Yes | | Must be `Tcp` |
| `Host` | Yes | | Hostname or IP address |
| `Port` | Yes | | TCP port number |
| `TimeoutSeconds` | No | `30` | Max seconds to wait for connection |

### 4. Run Locally

```bash
# Start Azurite (in a separate terminal)
azurite

# Run the function
cd src/AzureMonitor.SyntheticProbes
func start
```

The function will fire on the configured CRON schedule. Check the console output for probe results.

### 5. Run Tests

```bash
dotnet test
```

## Deploying to Azure

### Infrastructure Requirements

The function **must** run on a plan that supports VNet integration to reach private resources:

- **Azure Functions Premium plan** (EP1 or higher) — recommended for production
- **Dedicated App Service plan** (S1 or higher) — alternative option

The **Consumption plan does not support VNet integration** and cannot reach private resources.

### Step-by-Step Deployment

#### 1. Create Azure Resources

```bash
# Variables
RG="rg-synthetic-probes"
LOCATION="eastus"
STORAGE="stsyntheticstorage"
APPINSIGHTS="ai-synthetic-probes"
FUNCAPP="func-synthetic-probes"
PLAN="asp-synthetic-probes"
VNET="vnet-your-existing-vnet"
SUBNET="snet-functions"

# Resource group
az group create --name $RG --location $LOCATION

# Storage account (required by Azure Functions)
az storage account create \
  --name $STORAGE --resource-group $RG \
  --location $LOCATION --sku Standard_LRS

# Application Insights
az monitor app-insights component create \
  --app $APPINSIGHTS --resource-group $RG \
  --location $LOCATION --kind web

# Premium function plan
az functionapp plan create \
  --name $PLAN --resource-group $RG \
  --location $LOCATION --sku EP1 --is-linux

# Function app
az functionapp create \
  --name $FUNCAPP --resource-group $RG \
  --storage-account $STORAGE \
  --plan $PLAN \
  --runtime dotnet-isolated \
  --runtime-version 8 \
  --functions-version 4 \
  --app-insights $APPINSIGHTS
```

#### 2. Configure VNet Integration

```bash
# Create a delegated subnet for Functions (if not already present)
az network vnet subnet create \
  --name $SUBNET \
  --resource-group $RG \
  --vnet-name $VNET \
  --address-prefixes 10.0.1.0/24 \
  --delegations Microsoft.Web/serverFarms

# Enable VNet integration
az functionapp vnet-integration add \
  --name $FUNCAPP --resource-group $RG \
  --vnet $VNET --subnet $SUBNET
```

#### 3. Configure App Settings

In Azure, configure probe endpoints using flattened key format:

```bash
az functionapp config appsettings set --name $FUNCAPP --resource-group $RG --settings \
  "PROBE_SCHEDULE=0 */5 * * * *" \
  "ProbeConfiguration__RunLocation=East US" \
  "ProbeConfiguration__DefaultTimeoutSeconds=30" \
  "ProbeConfiguration__Endpoints__0__Name=Internal API Health" \
  "ProbeConfiguration__Endpoints__0__ProbeType=Http" \
  "ProbeConfiguration__Endpoints__0__Url=https://internal-api.private.contoso.com/health" \
  "ProbeConfiguration__Endpoints__0__ExpectedStatusCode=200" \
  "ProbeConfiguration__Endpoints__0__TimeoutSeconds=15" \
  "ProbeConfiguration__Endpoints__1__Name=SQL Server" \
  "ProbeConfiguration__Endpoints__1__ProbeType=Tcp" \
  "ProbeConfiguration__Endpoints__1__Host=sql-server.private.contoso.com" \
  "ProbeConfiguration__Endpoints__1__Port=1433" \
  "ProbeConfiguration__Endpoints__1__TimeoutSeconds=10"
```

#### 4. Deploy the Function App

```bash
cd src/AzureMonitor.SyntheticProbes
func azure functionapp publish $FUNCAPP
```

### DNS and Network Considerations

For the function to resolve private endpoint hostnames:

- **Private DNS Zones** must be linked to the VNet (e.g., `privatelink.database.windows.net` for Azure SQL, `privatelink.blob.core.windows.net` for Blob Storage)
- **NSGs** on the Functions subnet must allow outbound traffic to private endpoints on the required ports
- Verify connectivity from the Function App's Kudu console: **Advanced Tools > Debug Console > CMD** and run `nameResolverExercise <hostname>` or `tcpping <hostname>:<port>`

## Viewing Results in Application Insights

1. Open your **Application Insights** resource in the Azure Portal
2. Navigate to **Availability** in the left menu
3. Probe results appear as **CUSTOM** availability tests

### Sample Kusto Queries

```kusto
// View recent availability results
availabilityResults
| order by timestamp desc
| take 50

// Availability by test name over the last 24 hours
availabilityResults
| where timestamp > ago(24h)
| summarize SuccessRate = avg(toint(success)) * 100 by name
| order by SuccessRate asc

// Failed probes with details
availabilityResults
| where success == false
| project timestamp, name, message, duration, location
| order by timestamp desc
```

### Setting Up Alerts

1. In Application Insights, go to **Alerts > Create alert rule**
2. Select signal: **Availability** (under Metrics)
3. Configure: e.g., "Alert when availability drops below 100% for any test over a 5-minute window"
4. Add an action group (email, SMS, webhook, Azure Function, Logic App, etc.)

## Telemetry Details

Each probe result is reported with the following telemetry:

| Field | Value |
|-------|-------|
| `Name` | Endpoint name from configuration |
| `Timestamp` | UTC time when probe executed |
| `Duration` | Response time of the probe |
| `RunLocation` | From `REGION_NAME` env var or `ProbeConfiguration.RunLocation` |
| `Success` | Whether the probe passed |
| `Message` | Status detail or error description |

**Custom Properties:**
- `ProbeType` — `Http` or `Tcp`
- `Url` — Target URL (HTTP probes)
- `Target` — `host:port` (TCP probes)
- `StatusCode` — HTTP response status code (HTTP probes)

**Custom Metrics:**
- `DurationMs` — Probe duration in milliseconds
