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

// A literal, in a tracked file, in a public repository — on purpose. An Entra object id is an
// identifier and not a credential: it names `sp-etj-github-deploy`, it is printed by any
// `az role assignment list` on this registry, and holding it gives nobody a token. The
// alternative, a non-secret GitHub `vars.*`, would add a fourth value to EXP-119's bootstrap
// contract that no test in this repository can see is missing — whereas a wrong literal here is
// a wrong-looking line in a diff.
param deployPrincipalObjectId = '822c48bb-d583-4772-8f56-3c21ab4ed426'

// The project mailbox (EXP-112), not a personal one.
param budgetAlertEmail = 'expert2job@hotmail.com'
