// The AppHost (P1T-216): one command brings the local stack up. The reasoning behind every choice
// here — and the tripwires that made them non-obvious — is in manuals/adr-aspire-apphost.md.
var builder = DistributedApplication.CreateBuilder(args);

// Pinned, not generated. The postgres entrypoint reads POSTGRES_PASSWORD only when PGDATA is empty,
// so against the already-seeded volume a generated password is ignored and every connection then
// fails authentication against the baked-in one (P1T-204).
var pgPassword = builder.AddParameter("pg-password", "postgres", secret: true);

var postgres = builder.AddPostgres("postgres", password: pgPassword, port: 5432)
    // WithImage MUST come before WithDataVolume. The mount path is chosen by parsing the tag at
    // call time, and Aspire's default Postgres is now 18, which moved PGDATA — get the order wrong
    // and initdb writes a fresh empty cluster into this volume with no error, while the seeded data
    // sits there invisible.
    .WithImage("pgvector/pgvector", "pg17")
    // The real Docker object. `experttojob-pgdata` is the compose *key* and names nothing; passing
    // it would quietly create a new empty volume and orphan the roster.
    .WithDataVolume("aitestapp_experttojob-pgdata");

var db = postgres.AddDatabase("experttojob");

// A plain container rather than AddKeycloak: that package has never shipped stable, and everything
// it was wanted for is reachable from the stable API in the lines below (P1T-213).
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.0")
    .WithHttpEndpoint(port: 8080, targetPort: 8080)
    // Health lives on the management port and only with KC_HEALTH_ENABLED; it never appears on
    // 8080. Verified against this exact image, not assumed.
    .WithHttpEndpoint(targetPort: 9000, name: "management")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    // Without this, the OTLP variables land but Keycloak emits nothing.
    .WithEnvironment("KC_FEATURES", "opentelemetry")
    // Copied in, not bind-mounted — the same stable API the preview integration's WithRealmImport
    // calls underneath. No data volume here, ever: Keycloak skips realm import when the realm
    // already exists, so a volume would freeze the realm at its first import (P1T-203).
    .WithContainerFiles("/opt/keycloak/data/import", "../../keycloak/realm-export.json")
    .WithArgs("start-dev", "--import-realm")
    .WithHttpHealthCheck(endpointName: "management", path: "/health/ready")
    .WithOtlpExporter();

// The one process that owns the schema. Everything that reads the database waits for this to exit
// cleanly; today that is nothing, and from slice 4 it is all three hosts (P1T-208).
var migrator = builder.AddProject<Projects.ExpertToJob_Migrator>("migrator")
    // connectionName is required: the default key is the *resource* name, which this app does not
    // read, so it would fall back to its hardcoded localhost:5432 literal and appear to work.
    .WithReference(db, connectionName: "Default")
    .WaitFor(postgres);

// 500 synthetic experts on a dashboard button, never on boot. Nothing waits on it — an explicit
// start resource that something WaitForCompletion'd would be a deadlock nobody could see.
builder.AddProject<Projects.SeedDemoRoster>("demo-roster")
    .WithReference(db, connectionName: "Default")
    .WaitFor(postgres)
    .WithExplicitStart();

// Referenced so the container is part of the graph and the dashboard shows it; the hosts that
// actually need Keycloak arrive in slice 4.
_ = keycloak;
_ = migrator;

builder.Build().Run();
