// The apps (EXP-121): five container apps and one job, dropped into the environment that
// `base.bicep` created. This is the file a release redeploys — `imageTag` is normally the only
// thing that changes.
//
// Deployed at resource-group scope into `rg-experttojob-app`. Nothing here is deployed by the
// agent that wrote it; every `az` write is a human's (EXP-108).
//
// The shape is the resolution of EXP-111 (topology and the IP lockdown), EXP-112 (secrets) and
// EXP-114 (SSE against the 240 s ingress cap — decided: ship as is and measure, so there is no
// timeout knob here). `infra/apps.test.sh` compiles this file and reads the ARM JSON back, so
// **everything it asserts is written out literally**: an `env` array behind a `concat()`, a
// `probes` array behind a variable or a `[for ...]` loop all compile to one opaque ARM expression
// string, and a test that can no longer see inside it is a test that passes whatever is in there.
// The repetition below is what buys the assertions.

targetScope = 'resourceGroup'

@description('Region. Must be the region `base` was deployed into.')
param location string = 'swedencentral'

// `base`'s resources are reached by name rather than threaded through as five pipeline variables.
// The names are decisions (EXP-111 item 8) and they are literals in `base.bicep` too; reading the
// live resources here means the environment's default domain — which carries a unique id nobody
// can know before `base` runs — is never copied into a variable that can go stale.
@description('The Container Apps environment created by base.bicep.')
param environmentName string = 'cae-experttojob'

@description('The container registry created by base.bicep.')
param registryName string = 'experttojobacr'

@description('The user-assigned identity created by base.bicep. It holds AcrPull on the registry.')
param appsIdentityName string = 'id-etj-apps'

@description('The PostgreSQL flexible server created by base.bicep.')
param postgresServerName string = 'pg-experttojob-swc'

@minLength(7)
@description('Image tag for all six images — the git SHA the pipeline built. No default: a deployment that cannot say which build it is rolling out is not one.')
param imageTag string

// One Allow rule denies everything else, which is the whole lockdown (EXP-111 item 1). A *Deny*
// rule does the opposite — it allows everything it does not name — so the action is asserted in
// `apps.test.sh` rather than left to whoever edits this next.
@description('The single CIDR allowed to reach the edge app. Everything else is denied.')
param allowedIp string = '46.231.152.114/32'

@description('Administrator login of the PostgreSQL flexible server. One login for both databases (EXP-112 item 2, demo scope).')
param postgresAdminLogin string = 'etjadmin'

@secure()
@minLength(16)
@description('The PostgreSQL administrator password. Supplied by the pipeline; never stored in this repo.')
param postgresAdminPassword string

@secure()
@minLength(32)
@description('The session JWT signing key. The Web host issues the session token with it and both Web and Agents validate with it, so it is one value in two places.')
param jwtSigningKey string

@description('The account made staff on first start (EXP-112 item 4). Not a secret, and not a personal address.')
param seedAdministratorEmail string

@description('The Azure AI Foundry v1 endpoint — the resource chat and embeddings already use (EXP-9, EXP-56).')
param aiFoundryEndpoint string = 'https://experttojob-openai-swc.openai.azure.com/openai/v1/'

@secure()
@description('The Foundry key. Production uses the account\'s key2; local development keeps key1 (EXP-112 item 1).')
param aiFoundryApiKey string

@description('Keycloak bootstrap administrator username. The admin console is not browser-reachable; this is for `az containerapp exec`.')
param keycloakAdminUsername string = 'admin'

@secure()
@minLength(16)
@description('Keycloak bootstrap administrator password.')
param keycloakAdminPassword string

// Eight parameters rather than one object: a property of a secureObject is not a secure value as
// far as the linter is concerned, and the bar for this file is zero warnings — but more to the
// point, each of the eight is separately required. `apps.bicepparam` reads each from the
// environment with no fallback, so a missing one fails the *compile*. That matters more here than
// anywhere else in this deployment: Keycloak's realm import leaves an unresolved
// `${AGENT_X_SECRET}` in place rather than failing, so a forgotten secret becomes a client whose
// password is a string printed in this public repository (manuals/keycloak-prod-realm.md;
// EXP-124 is the ticket for the other half of that).

@secure()
@minLength(16)
@description('Keycloak client secret for `agent-roster-qa`. The same value the container receives as AGENT_ROSTER_QA_SECRET.')
param agentRosterQaSecret string

@secure()
@minLength(16)
@description('Keycloak client secret for `agent-cv-tailoring`. The same value the container receives as AGENT_CV_TAILORING_SECRET.')
param agentCvTailoringSecret string

@secure()
@minLength(16)
@description('Keycloak client secret for `agent-match`. The same value the container receives as AGENT_MATCH_SECRET.')
param agentMatchSecret string

@secure()
@minLength(16)
@description('Keycloak client secret for `agent-shortlist`. The same value the container receives as AGENT_SHORTLIST_SECRET.')
param agentShortlistSecret string

@secure()
@minLength(16)
@description('Keycloak client secret for `agent-interview-kit`. The same value the container receives as AGENT_INTERVIEW_KIT_SECRET.')
param agentInterviewKitSecret string

@secure()
@minLength(16)
@description('Keycloak client secret for `agent-bench-report`. The same value the container receives as AGENT_BENCH_REPORT_SECRET.')
param agentBenchReportSecret string

@secure()
@minLength(16)
@description('Keycloak client secret for `agent-resume-ingestion`. The same value the container receives as AGENT_RESUME_INGESTION_SECRET.')
param agentResumeIngestionSecret string

@secure()
@minLength(16)
@description('Keycloak client secret for `agent-roster-scan`. The same value the container receives as AGENT_ROSTER_SCAN_SECRET.')
param agentRosterScanSecret string

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: environmentName
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource appsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: appsIdentityName
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: postgresServerName
}

var acrLoginServer = registry.properties.loginServer
var defaultDomain = containerAppsEnvironment.properties.defaultDomain

// An external app answers on `<app>.<defaultDomain>`; an internal one on
// `<app>.internal.<defaultDomain>`, where defaultDomain is
// `<environment unique id>.<region>.azurecontainerapps.io`.
// https://learn.microsoft.com/en-us/azure/container-apps/connect-apps
//
// Both are computed rather than written down: the environment's unique id does not exist until
// `base` has been deployed, and `*.azurecontainerapps.io` is on the Public Suffix List, so the
// passkey relying-party id has to be this full host and cannot be a registrable parent of it
// (EXP-114 item 3).
var edgeHost = 'etj-edge.${defaultDomain}'
var keycloakHost = 'etj-keycloak.internal.${defaultDomain}'

// One string, used by the container that mints the tokens (KC_HOSTNAME), by the resource server
// that validates them (Mcp:Authority) and by the eight agents that ask for them. Three copies of
// it would be three chances at a 401 nobody can read.
var keycloakBaseUrl = 'https://${keycloakHost}'
var keycloakIssuer = '${keycloakBaseUrl}/realms/expert-to-job'

// Npgsql against the VNet-integrated flexible server. SSL is not optional on Azure PostgreSQL;
// `Trust Server Certificate` is on because the server presents a certificate for its public
// `*.postgres.database.azure.com` name while this connection resolves through the private DNS
// zone — and the traffic never leaves the virtual network.
var appConnectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=experttojob;Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require;Trust Server Certificate=true'

// Keycloak takes a JDBC URL, and its own database on the same server (EXP-111 item 2).
var keycloakJdbcUrl = 'jdbc:postgresql://${postgres.properties.fullyQualifiedDomainName}:5432/keycloak?sslmode=require'

// ----------------------------------------------------------------------------- the edge

// The only app with public ingress, and the reason the other four need none: the browser talks to
// one origin, so the Web host's hardcoded localhost CORS policy never comes into it, and the SSE
// settings the staffing stream needs live in the nginx template rather than here
// (deploy/images/nginx/default.conf.template, EXP-114 item 2).
resource edgeApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'etj-edge'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appsIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: acrLoginServer
          identity: appsIdentity.id
        }
      ]
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        // Container Apps denies everything an Allow list does not name, so this single entry *is*
        // the lockdown. A Deny next to it would change nothing; a second Allow would widen it.
        ipSecurityRestrictions: [
          {
            name: 'allow-operator'
            description: 'The one address the demo is opened to (EXP-111 item 1).'
            ipAddressRange: allowedIp
            action: 'Allow'
          }
        ]
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'edge'
          image: '${acrLoginServer}/etj-edge:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'WEB_UPSTREAM'
              value: 'etj-web'
            }
            {
              name: 'AGENTS_UPSTREAM'
              value: 'etj-agents'
            }
          ]
          // The image has no health endpoint of its own; nginx serves the SPA's index.html at /,
          // which is both the liveness signal and the thing a person would check.
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/'
                port: 8080
              }
              periodSeconds: 5
              failureThreshold: 12
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/'
                port: 8080
              }
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/'
                port: 8080
              }
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ----------------------------------------------------------------------------- the Web API

resource webApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'etj-web'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appsIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: acrLoginServer
          identity: appsIdentity.id
        }
      ]
      secrets: [
        {
          name: 'connection-string'
          value: appConnectionString
        }
        {
          name: 'jwt-signing-key'
          value: jwtSigningKey
        }
      ]
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${acrLoginServer}/etj-web:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__Default'
              secretRef: 'connection-string'
            }
            {
              name: 'Auth__Jwt__SigningKey'
              secretRef: 'jwt-signing-key'
            }
            // The relying-party id is the full edge host, computed from the environment's own
            // default domain — see `edgeHost` above for why neither half can be written down.
            {
              name: 'Auth__Passkey__ServerDomain'
              value: edgeHost
            }
            {
              name: 'Auth__Passkey__Origins__0'
              value: 'https://${edgeHost}'
            }
            {
              name: 'Auth__SeedAdministratorEmail'
              value: seedAdministratorEmail
            }
            // This host constructs neither AI provider. It reads the two names to say who receives
            // personal data on the privacy page (EXP-61), and all three hosts have to agree on them
            // (ProviderConfigAgreementTests) — which is also why it is given no key.
            {
              name: 'Ai__Chat__Provider'
              value: 'AzureFoundry'
            }
            {
              name: 'Ai__Embeddings__Provider'
              value: 'AzureFoundry'
            }
          ]
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/alive'
                port: 8080
              }
              periodSeconds: 5
              failureThreshold: 30
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/alive'
                port: 8080
              }
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      // max 1 is a correctness requirement here rather than a cost one: the passkey challenge
      // cache is in memory, so a second replica fails ceremonies at random (EXP-111 item 6).
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ----------------------------------------------------------------------------- the MCP server

resource mcpApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'etj-mcp'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appsIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: acrLoginServer
          identity: appsIdentity.id
        }
      ]
      secrets: [
        {
          name: 'connection-string'
          value: appConnectionString
        }
        {
          name: 'ai-api-key'
          value: aiFoundryApiKey
        }
      ]
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'mcp'
          image: '${acrLoginServer}/etj-mcp:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__Default'
              secretRef: 'connection-string'
            }
            {
              name: 'Ai__Chat__Provider'
              value: 'AzureFoundry'
            }
            {
              name: 'Ai__Embeddings__Provider'
              value: 'AzureFoundry'
            }
            {
              name: 'Ai__AzureFoundry__Endpoint'
              value: aiFoundryEndpoint
            }
            // This is the host that embeds, so it is the host that needs the key: a Production MCP
            // host refuses to start without one rather than degrading to keyword search
            // (EmbeddingProviderStartupGuard).
            {
              name: 'Ai__AzureFoundry__ApiKey'
              secretRef: 'ai-api-key'
            }
            // The resource server validates against the issuer Keycloak was told to mint.
            // `Mcp:Resource` is deliberately absent: it is an opaque audience identifier the realm's
            // own mapper already carries, and overriding it here breaks the match (EXP-112 item 5).
            {
              name: 'Mcp__Authority'
              value: keycloakIssuer
            }
          ]
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/alive'
                port: 8080
              }
              periodSeconds: 5
              failureThreshold: 30
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/alive'
                port: 8080
              }
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ----------------------------------------------------------------------------- the Agents host

resource agentsApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'etj-agents'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appsIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: acrLoginServer
          identity: appsIdentity.id
        }
      ]
      secrets: [
        {
          name: 'connection-string'
          value: appConnectionString
        }
        {
          name: 'jwt-signing-key'
          value: jwtSigningKey
        }
        {
          name: 'ai-api-key'
          value: aiFoundryApiKey
        }
        // The same eight values the Keycloak container below receives as AGENT_*_SECRET, from
        // the same parameter. `apps.test.sh` compares the two lists expression by expression,
        // because a client secret that matches on one side only is a 401 at the first tool call.
        {
          name: 'agent-roster-qa-secret'
          value: agentRosterQaSecret
        }
        {
          name: 'agent-cv-tailoring-secret'
          value: agentCvTailoringSecret
        }
        {
          name: 'agent-match-secret'
          value: agentMatchSecret
        }
        {
          name: 'agent-shortlist-secret'
          value: agentShortlistSecret
        }
        {
          name: 'agent-interview-kit-secret'
          value: agentInterviewKitSecret
        }
        {
          name: 'agent-bench-report-secret'
          value: agentBenchReportSecret
        }
        {
          name: 'agent-resume-ingestion-secret'
          value: agentResumeIngestionSecret
        }
        {
          name: 'agent-roster-scan-secret'
          value: agentRosterScanSecret
        }
      ]
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'agents'
          image: '${acrLoginServer}/etj-agents:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__Default'
              secretRef: 'connection-string'
            }
            {
              name: 'Auth__Jwt__SigningKey'
              secretRef: 'jwt-signing-key'
            }
            {
              name: 'Ai__Chat__Provider'
              value: 'AzureFoundry'
            }
            {
              name: 'Ai__Embeddings__Provider'
              value: 'AzureFoundry'
            }
            {
              name: 'Ai__AzureFoundry__Endpoint'
              value: aiFoundryEndpoint
            }
            {
              name: 'Ai__AzureFoundry__ApiKey'
              secretRef: 'ai-api-key'
            }
            // Inside the environment an app answers on its own name over plain HTTP, so the roster
            // never leaves the virtual network on its way to the host that reasons over it.
            {
              name: 'McpServer__BaseUrl'
              value: 'http://etj-mcp'
            }
            // One keyed client-credentials identity per agent (api/Agents/Program.cs), each asking
            // the same realm the MCP host validates against. The Agents host refuses to start in
            // Production when any one of these secrets is empty.
            {
              name: 'McpAuth__roster-qa__Authority'
              value: keycloakIssuer
            }
            {
              name: 'McpAuth__roster-qa__ClientSecret'
              secretRef: 'agent-roster-qa-secret'
            }
            {
              name: 'McpAuth__cv-tailoring__Authority'
              value: keycloakIssuer
            }
            {
              name: 'McpAuth__cv-tailoring__ClientSecret'
              secretRef: 'agent-cv-tailoring-secret'
            }
            {
              name: 'McpAuth__match__Authority'
              value: keycloakIssuer
            }
            {
              name: 'McpAuth__match__ClientSecret'
              secretRef: 'agent-match-secret'
            }
            {
              name: 'McpAuth__shortlist__Authority'
              value: keycloakIssuer
            }
            {
              name: 'McpAuth__shortlist__ClientSecret'
              secretRef: 'agent-shortlist-secret'
            }
            {
              name: 'McpAuth__interview-kit__Authority'
              value: keycloakIssuer
            }
            {
              name: 'McpAuth__interview-kit__ClientSecret'
              secretRef: 'agent-interview-kit-secret'
            }
            {
              name: 'McpAuth__bench-report__Authority'
              value: keycloakIssuer
            }
            {
              name: 'McpAuth__bench-report__ClientSecret'
              secretRef: 'agent-bench-report-secret'
            }
            {
              name: 'McpAuth__resume-ingestion__Authority'
              value: keycloakIssuer
            }
            {
              name: 'McpAuth__resume-ingestion__ClientSecret'
              secretRef: 'agent-resume-ingestion-secret'
            }
            {
              name: 'McpAuth__roster-scan__Authority'
              value: keycloakIssuer
            }
            {
              name: 'McpAuth__roster-scan__ClientSecret'
              secretRef: 'agent-roster-scan-secret'
            }
          ]
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/alive'
                port: 8080
              }
              periodSeconds: 5
              failureThreshold: 30
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/alive'
                port: 8080
              }
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ----------------------------------------------------------------------------- Keycloak

// The image is the official distribution plus the generated production realm
// (keycloak/Dockerfile, manuals/keycloak-prod-realm.md). Nothing about where it runs is baked in,
// so everything it needs is below.
resource keycloakApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'etj-keycloak'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appsIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: acrLoginServer
          identity: appsIdentity.id
        }
      ]
      secrets: [
        {
          name: 'db-password'
          value: postgresAdminPassword
        }
        {
          name: 'admin-password'
          value: keycloakAdminPassword
        }
        // The other half of the eight above: same parameter, same eight values.
        {
          name: 'agent-roster-qa-secret'
          value: agentRosterQaSecret
        }
        {
          name: 'agent-cv-tailoring-secret'
          value: agentCvTailoringSecret
        }
        {
          name: 'agent-match-secret'
          value: agentMatchSecret
        }
        {
          name: 'agent-shortlist-secret'
          value: agentShortlistSecret
        }
        {
          name: 'agent-interview-kit-secret'
          value: agentInterviewKitSecret
        }
        {
          name: 'agent-bench-report-secret'
          value: agentBenchReportSecret
        }
        {
          name: 'agent-resume-ingestion-secret'
          value: agentResumeIngestionSecret
        }
        {
          name: 'agent-roster-scan-secret'
          value: agentRosterScanSecret
        }
      ]
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'keycloak'
          image: '${acrLoginServer}/etj-keycloak:${imageTag}'
          // The one JVM in the deployment, and the only container that gets more than a quarter
          // core. The figures in infra/README.md are costed against exactly this split.
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'KC_DB'
              value: 'postgres'
            }
            {
              name: 'KC_DB_URL'
              value: keycloakJdbcUrl
            }
            {
              name: 'KC_DB_USERNAME'
              value: postgresAdminLogin
            }
            {
              name: 'KC_DB_PASSWORD'
              secretRef: 'db-password'
            }
            // With --proxy-headers=xforwarded the container builds its URLs from the forwarded
            // headers; KC_HOSTNAME is what makes the issuer in every token it mints the address the
            // MCP host validates against.
            {
              name: 'KC_HOSTNAME'
              value: keycloakBaseUrl
            }
            {
              name: 'KC_BOOTSTRAP_ADMIN_USERNAME'
              value: keycloakAdminUsername
            }
            {
              name: 'KC_BOOTSTRAP_ADMIN_PASSWORD'
              secretRef: 'admin-password'
            }
            {
              name: 'KC_HEALTH_ENABLED'
              value: 'true'
            }
            // The eight `${AGENT_*_SECRET}` placeholders the realm import resolves. An unset one
            // does not fail the import — it imports the literal placeholder as the client secret,
            // a string printed in this public repository — which is why they come from a parameter
            // that cannot be absent (manuals/keycloak-prod-realm.md).
            {
              name: 'AGENT_ROSTER_QA_SECRET'
              secretRef: 'agent-roster-qa-secret'
            }
            {
              name: 'AGENT_CV_TAILORING_SECRET'
              secretRef: 'agent-cv-tailoring-secret'
            }
            {
              name: 'AGENT_MATCH_SECRET'
              secretRef: 'agent-match-secret'
            }
            {
              name: 'AGENT_SHORTLIST_SECRET'
              secretRef: 'agent-shortlist-secret'
            }
            {
              name: 'AGENT_INTERVIEW_KIT_SECRET'
              secretRef: 'agent-interview-kit-secret'
            }
            {
              name: 'AGENT_BENCH_REPORT_SECRET'
              secretRef: 'agent-bench-report-secret'
            }
            {
              name: 'AGENT_RESUME_INGESTION_SECRET'
              secretRef: 'agent-resume-ingestion-secret'
            }
            {
              name: 'AGENT_ROSTER_SCAN_SECRET'
              secretRef: 'agent-roster-scan-secret'
            }
          ]
          // Health lives on the management port and only with KC_HEALTH_ENABLED; it never appears
          // on 8080, which is the mistake that flag invites (manuals/keycloak-prod-realm.md).
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/health/started'
                port: 9000
              }
              periodSeconds: 10
              failureThreshold: 30
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 9000
              }
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 9000
              }
              periodSeconds: 10
              failureThreshold: 6
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ----------------------------------------------------------------------------- the migrator

// A job, not an app: it runs, it finishes, it exits (api/Migrator/Program.cs). Manual trigger, so
// redeploying this file never touches the schema on its own — the deploy workflow starts it
// explicitly, between pushing the images and rolling the apps.
resource migratorJob 'Microsoft.App/jobs@2024-03-01' = {
  name: 'etj-migrator'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appsIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      // Zero retries on purpose: a migration that failed half way is not improved by being run
      // again unattended, and the second failure buries the first one's logs.
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          server: acrLoginServer
          identity: appsIdentity.id
        }
      ]
      secrets: [
        {
          name: 'connection-string'
          value: appConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrator'
          image: '${acrLoginServer}/etj-migrator:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__Default'
              secretRef: 'connection-string'
            }
          ]
        }
      ]
    }
  }
}

// Azure's own answer rather than the `edgeHost` this file computed, so the deploy workflow's smoke
// test hits the host that exists. If the two ever disagree, passkeys fail at the first sign-in and
// this output is what says why.
output edgeFqdn string = edgeApp.properties.configuration.ingress.fqdn
