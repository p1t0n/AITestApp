// Parameters for `base.bicep`. This file is tracked and the repository is public, so it holds no
// secret and no personal address: the one secret comes out of the environment, which is where the
// deploy workflow (EXP-122) puts the GitHub `production` environment secret.
//
// A .bicepparam file cannot be combined with inline `--parameters` overrides on the az CLI, which
// is why the password arrives by environment variable rather than on the command line.

using './base.bicep'

// No default on purpose. With `@minLength(16)` on the parameter, a missing or short
// ETJ_PG_ADMIN_PASSWORD fails the *compile* — so there is no path from "secret not set" to a
// deployed server. `infra/base.test.sh` compiles this file with a throwaway value of its own.
param postgresAdminPassword = readEnvironmentVariable('ETJ_PG_ADMIN_PASSWORD')

// The project mailbox (EXP-112), not a personal one.
param budgetAlertEmail = 'expert2job@hotmail.com'
