// Base infrastructure for ExpertToJob (EXP-116). Everything here is created once and then left
// alone: the network, the database, the registry, the log sink and the Container Apps environment
// the apps deployment (EXP-121) drops its five apps and one job into.
//
// Deployed at resource-group scope into `rg-experttojob-app`, which the bootstrap ticket
// (EXP-119) creates by hand. Nothing in this file is deployed by the agent that wrote it.
//
// Shape and SKU choices are the resolution of EXP-111; the money behind them is in
// `infra/README.md`, which reads its figures off EXP-109's research note — that note lives on
// branch `research/azure-deploy-cost-ip`, not on main.

targetScope = 'resourceGroup'

@description('Region for every resource. The Foundry account the apps call lives in swedencentral too.')
param location string = 'swedencentral'

@description('Administrator login for the PostgreSQL flexible server.')
param postgresAdminLogin string = 'etjadmin'

// No Key Vault in this deployment (EXP-108): the password is generated once and kept as a GitHub
// `production` environment secret, reaching Bicep through the params file's readEnvironmentVariable.
// The length floor is what turns a missing secret into a failed deployment instead of a server
// nobody can explain the credentials of.
@secure()
@minLength(16)
@description('Administrator password. Supplied by the pipeline; never stored in this repo.')
param postgresAdminPassword string

@description('Address space of the virtual network. Must hold the /23 infrastructure subnet and the /24 database subnet.')
param vnetAddressPrefix string = '10.20.0.0/16'

// A Consumption-only Container Apps environment needs a /23 or larger infrastructure subnet, and
// that subnet must carry no delegation (workload-profile environments are the ones that take /27
// and a Microsoft.App/environments delegation).
// https://learn.microsoft.com/en-us/azure/container-apps/networking
param containerAppsSubnetPrefix string = '10.20.0.0/23'

@description('Subnet delegated to Microsoft.DBforPostgreSQL/flexibleServers. One server, so a /24 is already generous.')
param postgresSubnetPrefix string = '10.20.2.0/24'

@description('Where the budget alerts go.')
param budgetAlertEmail string

// Budgets need a start date on the first of a month, and Bicep cannot compute one at deployment
// time — utcNow() is only legal as a parameter default, which is exactly this.
@description('First day of the month the budget starts counting from. Leave at the default.')
param budgetStartDate string = utcNow('yyyy-MM-01')

var vnetName = 'vnet-experttojob'
var containerAppsSubnetName = 'snet-cae-infra'
var postgresSubnetName = 'snet-postgres'
var postgresServerName = 'pg-experttojob-swc'
var registryName = 'experttojobacr'
var environmentName = 'cae-experttojob'
var logAnalyticsName = 'log-experttojob'
var appsIdentityName = 'id-etj-apps'

// The zone name *is* the server's FQDN for a VNet-integrated flexible server: Azure puts an apex
// record in it rather than a record named after the server.
// https://learn.microsoft.com/en-us/azure/postgresql/network/concepts-networking-private
var postgresPrivateDnsZoneName = '${postgresServerName}.private.postgres.database.azure.com'

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [vnetAddressPrefix]
    }
    subnets: [
      {
        name: containerAppsSubnetName
        properties: {
          addressPrefix: containerAppsSubnetPrefix
        }
      }
      {
        name: postgresSubnetName
        properties: {
          addressPrefix: postgresSubnetPrefix
          delegations: [
            {
              name: 'postgres-flexible'
              properties: {
                serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
              }
            }
          ]
        }
      }
    ]
  }
}

resource containerAppsSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: vnet
  name: containerAppsSubnetName
}

resource postgresSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: vnet
  name: postgresSubnetName
}

resource postgresPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: postgresPrivateDnsZoneName
  location: 'global'
}

// The link has to exist before the server is created, or the server deployment fails resolving
// its own name.
resource postgresPrivateDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: postgresPrivateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

// 30-day retention and a 1 GB/day cap: the logs are a demo's logs, and an uncapped workspace is
// the one line in this deployment that can quietly outgrow the $60 budget.
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: 1
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// No `workloadProfiles` property at all — that is what makes this a Consumption-only environment
// (the portal defaults to workload profiles; ARM does not). `internal: false` keeps the one
// external app, the nginx edge, reachable; every other app takes internal ingress.
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: containerAppsSubnet.id
      internal: false
    }
    zoneRedundant: false
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresServerName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '17'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: {
      storageSizeGB: 32
      autoGrow: 'Disabled'
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    // Private access: a delegated subnet plus the private DNS zone, and no firewall rules
    // anywhere. This cannot be changed after creation — a server cannot move in or out of a VNet.
    network: {
      delegatedSubnetResourceId: postgresSubnet.id
      privateDnsZoneArmResourceId: postgresPrivateDnsZone.id
      publicNetworkAccess: 'Disabled'
    }
  }
  dependsOn: [
    postgresPrivateDnsZoneLink
  ]
}

// pgvector is allow-listed here and still needs `CREATE EXTENSION vector;` per database — the
// migrator does that. `azure.extensions` is a static parameter, so this restarts the server.
resource postgresExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: {
    value: 'VECTOR'
    source: 'user-override'
  }
}

// The flexible server rejects concurrent child operations, so the two databases and the
// configuration are chained rather than left to run in parallel.
resource appDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'experttojob'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
  dependsOn: [
    postgresExtensions
  ]
}

resource keycloakDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'keycloak'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
  dependsOn: [
    appDatabase
  ]
}

// Basic: 10 GiB included, which five small images fit inside several times over. Admin user off —
// the apps pull with the managed identity below, and the pipeline pushes with its OIDC identity.
resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource appsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: appsIdentityName
  location: location
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  // AcrPull, written out rather than hidden behind a variable so the compiled ARM — which is what
  // `infra/base.test.sh` reads — names the role itself. Built-in role ids are the same in every tenant.
  name: guid(registry.id, appsIdentity.id, '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: appsIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Scoped to the resource group this is deployed into. 80% actual is the "look now" signal; 100%
// forecast is the one that fires before the money is spent.
resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: 'budget-experttojob'
  properties: {
    category: 'Cost'
    // Literal on purpose: the figure is a decision (EXP-111), not a knob, and a literal is what
    // `infra/base.test.sh` can hold to account in the compiled ARM.
    amount: 60
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [budgetAlertEmail]
      }
      forecasted100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [budgetAlertEmail]
      }
    }
  }
}

output acrLoginServer string = registry.properties.loginServer
output environmentId string = containerAppsEnvironment.id

// The apps deployment builds every internal FQDN, and the passkey relying-party id, out of this.
output environmentDefaultDomain string = containerAppsEnvironment.properties.defaultDomain
output postgresFqdn string = postgres.properties.fullyQualifiedDomainName
output appsIdentityId string = appsIdentity.id
