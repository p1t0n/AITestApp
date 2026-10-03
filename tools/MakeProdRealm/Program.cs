// Regenerates keycloak/realm-export.prod.json from keycloak/realm-export.json (EXP-117).
//
//   dotnet run --project tools/MakeProdRealm
//
// The output is checked in and `ProdRealmTransformTests` fails if it drifts from what this
// produces, so editing the dev realm without rerunning this is a red test rather than a
// production realm that quietly lags behind. `--check` reports drift without writing, which is
// what a deploy pipeline wants.
using ExpertToJob.KeycloakRealm;

var check = args.Contains("--check");

var root = RepoRoot();
var sourcePath = Path.Combine(root, "keycloak", ProdRealmTransform.SourceFileName);
var prodPath = Path.Combine(root, "keycloak", ProdRealmTransform.ProdFileName);

var produced = ProdRealmTransform.Apply(File.ReadAllText(sourcePath));

if (check)
{
    var committed = File.Exists(prodPath) ? File.ReadAllText(prodPath) : null;
    if (committed == produced)
    {
        Console.WriteLine($"{ProdRealmTransform.ProdFileName} is up to date.");
        return 0;
    }

    Console.Error.WriteLine(
        $"{ProdRealmTransform.ProdFileName} is stale. Run `dotnet run --project tools/MakeProdRealm`.");
    return 1;
}

File.WriteAllText(prodPath, produced);
Console.WriteLine($"Wrote {prodPath}");
return 0;

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpertToJob.slnx")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName
           ?? throw new InvalidOperationException("Could not find ExpertToJob.slnx above the binary.");
}
