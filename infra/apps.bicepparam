// Parameters for `apps.bicep`. This file is tracked and the repository is public, so it holds no
// secret and no personal address: every secret comes out of the environment, which is where the
// deploy workflow (EXP-122) puts the GitHub `production` environment secrets.
//
// None of the `readEnvironmentVariable` calls below has a fallback, and that is the whole point:
// with any one of them unset this file fails to **compile**, so there is no path from "forgot a
// secret" to a deployment that half worked. For the eight agent secrets that is not a nicety —
// Keycloak's realm import leaves an unresolved `${AGENT_X_SECRET}` in place rather than failing,
// which would create a client whose password is a string printed in this repository
// (manuals/keycloak-prod-realm.md).
//
// A .bicepparam file cannot be combined with inline `--parameters` overrides on the az CLI, which
// is why every one of these arrives by environment variable rather than on the command line.

using './apps.bicep'

// The git SHA the pipeline built the six images from. Deliberately not `latest`: a rollback is
// "deploy the previous tag", and a tag that moves makes the deployed revision unknowable.
param imageTag = readEnvironmentVariable('ETJ_IMAGE_TAG')

// The same variable `base.bicepparam` reads, because it is the same password — one administrator
// login serves both the `experttojob` and `keycloak` databases (EXP-112 item 2, demo scope).
param postgresAdminPassword = readEnvironmentVariable('ETJ_PG_ADMIN_PASSWORD')

param jwtSigningKey = readEnvironmentVariable('ETJ_JWT_SIGNING_KEY')
param keycloakAdminPassword = readEnvironmentVariable('ETJ_KEYCLOAK_ADMIN_PASSWORD')

// Prefixed, unlike the `AZURE_FOUNDRY_API_KEY` the application reads at run time: production uses
// the account's **key2** and local development keeps key1 (EXP-112 item 1), and a deploy that
// silently picked up whichever key the operator happened to have exported would deploy the wrong
// one without saying so.
param aiFoundryApiKey = readEnvironmentVariable('ETJ_AZURE_FOUNDRY_API_KEY')

// These eight keep Keycloak's own names. They are the values the Keycloak container resolves its
// realm placeholders from *and* the values the Agents host reads under `McpAuth:<agent>:ClientSecret`,
// and the deployment has to set both halves from one source — so the source is named once, here.
param agentRosterQaSecret = readEnvironmentVariable('AGENT_ROSTER_QA_SECRET')
param agentCvTailoringSecret = readEnvironmentVariable('AGENT_CV_TAILORING_SECRET')
param agentMatchSecret = readEnvironmentVariable('AGENT_MATCH_SECRET')
param agentShortlistSecret = readEnvironmentVariable('AGENT_SHORTLIST_SECRET')
param agentInterviewKitSecret = readEnvironmentVariable('AGENT_INTERVIEW_KIT_SECRET')
param agentBenchReportSecret = readEnvironmentVariable('AGENT_BENCH_REPORT_SECRET')
param agentResumeIngestionSecret = readEnvironmentVariable('AGENT_RESUME_INGESTION_SECRET')
param agentRosterScanSecret = readEnvironmentVariable('AGENT_ROSTER_SCAN_SECRET')

// The project mailbox (EXP-112 item 4), not a personal one. The account that first signs in with
// this address is made staff.
param seedAdministratorEmail = 'expert2job@hotmail.com'
