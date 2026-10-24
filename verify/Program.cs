// <copyright>
// Copyright by the Spark Development Network
//
// Licensed under the Rock Community License (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.rockrms.com/license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//

using System.Diagnostics;

using RockSampleData.Loader;
using RockSampleData.Loader.Database;
using RockSampleData.Loader.Packages;
using RockSampleData.Loader.Validation;
using RockSampleData.Verify;

/*
    The verify tool: proves that dataset packages work on one exact Rock, and writes a receipt for each.
    It runs when packages are made, never when a church installs one. See README.md.

    Usage:
      rock-sample-data-verify --rock <built Rock checkout> [--dataset core-1|all] [--packages <folder>] [--server localhost]
                              [--base RockSampleVerify-base] [--run <tag>] [--anchor yyyy-MM-dd] [--seed text] [--record-contract] [--keep]
      rock-sample-data-verify --check-receipts [<packages folder>]
*/

var options = ParseArguments( args );
var root = SampleDataSource.FindRoot( AppContext.BaseDirectory );
var packagesFolder = options.GetValueOrDefault( "packages" ) ?? Path.Combine( root, "packages" );

// --check-receipts is the publishing gate: every package needs a passing receipt for exactly its bytes.
if ( options.TryGetValue( "check-receipts", out var receiptFolder ) )
{
    var folder = receiptFolder == "true" ? packagesFolder : receiptFolder;
    var problems = Receipt.CheckFolder( folder );
    foreach ( var problem in problems )
    {
        Console.WriteLine( $"  {problem}" );
    }

    Console.WriteLine( problems.Count == 0 ? $"Every package in {folder} has a passing receipt." : "Do not publish: some packages are not verified." );
    return problems.Count == 0 ? 0 : 1;
}

try
{
    var total = Stopwatch.StartNew();
    var rock = RockRelease.Read( options.GetValueOrDefault( "rock" ) ?? Environment.GetEnvironmentVariable( "ROCK_PATH" ) );
    var sql = new SqlServer( options.GetValueOrDefault( "server", "localhost" ) );
    var baseDatabase = options.GetValueOrDefault( "base", $"{SqlServer.DatabasePrefix}-base" );

    // Each run's own databases carry its tag (by default its worktree's name), so runs from different worktrees never meet.
    var runPrefix = $"{SqlServer.DatabasePrefix}-{RunTag( options.GetValueOrDefault( "run" ) ?? Path.GetFileName( root ) )}";
    var anchor = options.TryGetValue( "anchor", out var anchorText ) ? DateTime.Parse( anchorText ) : DateTime.Today;
    var seed = options.GetValueOrDefault( "seed", "rock-sample-data" );
    var isRecording = options.ContainsKey( "record-contract" );
    var allDatasets = Directory.GetFiles( Path.Combine( root, "datasets" ), "*.json" ).Select( Path.GetFileNameWithoutExtension ).OrderBy( k => k, StringComparer.Ordinal ).ToList();
    var datasetOption = options.GetValueOrDefault( "dataset", "all" );
    var datasets = datasetOption == "all" ? allDatasets : new List<string> { datasetOption };
    var isEveryDataset = datasets.Count == allDatasets.Count;
    if ( isRecording && !isEveryDataset )
    {
        throw new InvalidOperationException( "--record-contract needs every dataset, so the contract covers everything the loader writes." );
    }

    // The loader gets the Rock version from RockWeb\bin\Rock.Version.dll, as Rock's installer gets its own (decision 52).
    // A different version from the checkout's props means RockWeb\bin was built from other code.
    var rockMajor = RockVersionInfo.MajorOf( rock.Version ) ?? throw new InvalidOperationException( $"The checkout's version {rock.Version} does not start with a major release number." );
    var builtRockVersion = RockVersionInfo.Read( Path.Combine( rock.BinPath, $"{RockVersionInfo.AssemblyName}.dll" ) );
    if ( builtRockVersion != rock.Version )
    {
        throw new InvalidOperationException( $"RockWeb\\bin\\{RockVersionInfo.AssemblyName}.dll is Rock {builtRockVersion}, but the checkout is Rock {rock.Version}. Rebuild Rock." );
    }

    Console.WriteLine( $"Verifying {string.Join( ", ", datasets )} on Rock {rock.Version} (newest migration {rock.NewestMigration}) in {runPrefix}-* databases" );

    // 1. The content standards, which need no database.
    var standards = ContentStandards.Run( root );
    standards.Print();

    // 2. The trusted base. An existing one is reused only if it still certifies for this checkout. Runs share it,
    //    so this run holds a shared lock on it until it ends, and takes an exclusive lock only to rebuild it.
    using var baseLock = new BaseLock( sql, baseDatabase );
    baseLock.Take( "Shared" );
    var (baseCheck, certificate) = TrustedBase.Certify( sql, baseDatabase, rock );
    if ( !baseCheck.Passed )
    {
        baseLock.Take( "Exclusive" );

        // Another run may have rebuilt it while this one waited.
        (baseCheck, certificate) = TrustedBase.Certify( sql, baseDatabase, rock );
        if ( !baseCheck.Passed )
        {
            Console.WriteLine( $"  {baseDatabase} is missing or stale: {string.Join( " ", baseCheck.Details.Take( 2 ) )}" );
            TrustedBase.Create( sql, baseDatabase, rock );
            (baseCheck, certificate) = TrustedBase.Certify( sql, baseDatabase, rock );
        }

        baseLock.Take( "Shared" );
    }

    baseCheck.Print();
    if ( !baseCheck.Passed )
    {
        Console.Error.WriteLine( "No receipt was written: there is no trusted base to verify on." );
        return 1;
    }

    // 3. Each dataset: load it into a copy of the base while recording what it writes, then check the result.
    var contractPath = Path.Combine( root, "validation", "rock-contract.json" );
    var recorder = new WriteRecorder();
    var referee = new CleanupReferee( sql, rock, Path.Combine( root, "validation", "cleanup-accepted.json" ), runPrefix );
    // The receipt binds the bytes and the commit read now, before the long run, and checks at the end that neither moved.
    var verifier = Git.Source( root );
    var perDataset = new Dictionary<string, (string Package, string Sha256, PackageManifest Manifest, List<CheckResult> Checks)>();
    foreach ( var dataset in datasets )
    {
        var packagePath = Path.Combine( packagesFolder, $"{dataset}.zip" );
        var sha256 = Receipt.Sha256( packagePath );
        var package = SampleDataPackage.Open( packagePath );
        if ( package.Manifest.Dataset != dataset )
        {
            throw new InvalidOperationException( $"{packagePath} holds dataset {package.Manifest.Dataset}, not {dataset}." );
        }

        if ( package.Manifest.Rock?.Major != rockMajor || package.Manifest.Rock?.Version != rock.Version || package.Manifest.Rock?.Migration != rock.NewestMigration )
        {
            throw new InvalidOperationException( $"{packagePath} is for Rock {package.Manifest.Rock?.Major}, built on {package.Manifest.Rock?.Version} ({package.Manifest.Rock?.Migration}), not this checkout's {rock.Version} ({rock.NewestMigration}). Build packages from the same checkout." );
        }

        Console.WriteLine( $"\n{dataset}" );
        var database = $"{runPrefix}-{dataset}";
        var connection = sql.ConnectionString( database );
        var foreignKey = $"SampleData:{dataset}";
        var checks = new List<CheckResult>();
        sql.Copy( baseDatabase, database );
        var stage = "load";
        try
        {
            var load = Stopwatch.StartNew();
            DatasetLoader.Load( connection, package, new DatasetLoadOptions
            {
                Anchor = anchor,
                Seed = seed,
                Progress = new ConsoleProgress(),
                RockWebConfigPath = rock.WebConfigPath,
                RockVersion = builtRockVersion,
                Recorder = recorder
            } );
            checks.Add( CheckResult.From( "load", $"Loaded into a copy of the trusted base in {load.Elapsed.TotalSeconds:N0} seconds", new List<string>() ) );

            stage = "cast-expectations";
            using ( var db = new RockDatabase( connection ) )
            {
                var (expectations, failures) = CastExpectations.Evaluate( db, new SampleDataSource( package ), SampleDataParts.All.Select( p => p.Part ).ToList() );
                checks.Add( CheckResult.From( stage, $"{expectations - failures.Count} of {expectations} Cast expectations hold", failures ) );
            }

            stage = "invariants";
            // The dataset card was measured at the default seed, so only a default-seed load is held to its numbers.
            var card = seed == new DatasetLoadOptions().Seed ? SampleDataDatasets.Get( dataset ) : null;
            checks.Add( Invariants.Run( sql, database, foreignKey, card, Path.Combine( root, "validation", "invariants.sql" ) ) );
            stage = "rock-cleanup";
            checks.Add( referee.Referee( database, baseDatabase ) );
        }
        catch ( Exception ex ) when ( ex is not InvalidDataException )
        {
            checks.Add( CheckResult.From( stage, "Could not finish", new List<string> { ex.Message } ) );
        }
        finally
        {
            if ( !options.ContainsKey( "keep" ) )
            {
                sql.Drop( database );
            }
        }

        foreach ( var check in checks )
        {
            check.Print();
        }

        perDataset[dataset] = (packagePath, sha256, package.Manifest, checks);
    }

    // 4. The contract: recorded from these loads, compared with validation/rock-contract.json and with this Rock.
    var recorded = RockContract.Record( recorder, sql, baseDatabase, rock );
    if ( isRecording )
    {
        recorded.Save( contractPath );
        Console.WriteLine( $"\nRecorded the contract to {contractPath}. Review its diff before committing." );
    }

    var contract = RockContract.Load( contractPath );
    var contractChecks = contract == null
        ? new List<CheckResult> { CheckResult.From( "contract-current", "There is no contract", new List<string> { "Run with --record-contract to record validation/rock-contract.json." } ) }
        : new List<CheckResult> { contract.CheckSchema( sql, baseDatabase, rock.Version ), contract.CheckCurrent( recorded, recorder.Unparsed, isEveryDataset ) };
    Console.WriteLine();
    foreach ( var check in contractChecks )
    {
        check.Print();
    }

    // 5. One receipt per package, for the Rock checked here, not the Rock the package claims.
    var verifierNow = Git.Source( root );
    var isEverythingPassed = true;
    Console.WriteLine();
    foreach ( var (dataset, (packagePath, sha256, manifest, checks)) in perDataset )
    {
        var moved = new List<string>();
        if ( Receipt.Sha256( packagePath ) != sha256 )
        {
            moved.Add( $"{Path.GetFileName( packagePath )} changed during the run." );
        }

        if ( verifierNow.Commit != verifier.Commit || verifierNow.IsModified != verifier.IsModified )
        {
            moved.Add( "This repository changed during the run." );
        }

        var unchanged = CheckResult.From( "unchanged-during-run", "The package and the checks stayed the same for the whole run", moved );
        var all = new List<CheckResult> { baseCheck, standards }.Concat( contractChecks ).Concat( checks ).Append( unchanged ).ToList();
        var receipt = new Receipt
        {
            Passed = all.All( c => c.Passed ),
            Package = new Receipt.ReceiptPackage { File = Path.GetFileName( packagePath ), Sha256 = sha256, Dataset = dataset, Source = manifest.Source, MinimumLoaderVersion = manifest.MinimumLoaderVersion },
            Rock = new PackageRock { Major = rockMajor, Version = rock.Version, Migration = rock.NewestMigration },
            VerifiedAt = DateTime.UtcNow,
            Anchor = anchor.ToString( "yyyy-MM-dd" ),
            Seed = seed,
            Verifier = verifier,
            Base = certificate,
            Checks = all
        };
        var receiptPath = Receipt.PathFor( packagePath );
        receipt.Save( receiptPath );
        isEverythingPassed &= receipt.Passed;
        var failed = all.Where( c => !c.Passed ).Select( c => c.Name ).ToList();
        Console.WriteLine( $"{dataset}: {( receipt.Passed ? "PASSED" : $"FAILED ({string.Join( ", ", failed )})" )} -> {receiptPath}" );
    }

    Console.WriteLine( $"Done in {total.Elapsed.TotalMinutes:N1} minutes." );
    return isEverythingPassed ? 0 : 1;
}
catch ( Exception ex ) when ( ex is InvalidOperationException || ex is IOException || ex is InvalidDataException )
{
    Console.Error.WriteLine( $"Verification stopped. {ex.Message}" );
    return 1;
}

/// <summary>
/// The run tag in this run's database names: the worktree folder without its "rock-sample-data-" prefix
/// (the main checkout is "main"), in lowercase letters, digits, and hyphens.
/// </summary>
static string RunTag( string name )
{
    var tag = name.ToLowerInvariant();
    tag = tag == "rock-sample-data" ? "main" : tag.StartsWith( "rock-sample-data-", StringComparison.Ordinal ) ? tag.Substring( "rock-sample-data-".Length ) : tag;
    tag = new string( tag.Select( c => char.IsAsciiLetterOrDigit( c ) ? c : '-' ).ToArray() ).Trim( '-' );
    return tag.Length == 0 ? "run" : tag.Length > 40 ? tag.Substring( 0, 40 ) : tag;
}

/// <summary>
/// Reads "--name value" pairs and bare "--flag" switches.
/// </summary>
static Dictionary<string, string> ParseArguments( string[] args )
{
    var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
    for ( var i = 0; i < args.Length; i++ )
    {
        if ( !args[i].StartsWith( "--" ) )
        {
            continue;
        }

        var name = args[i].Substring( 2 );
        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith( "--" );
        result[name] = hasValue ? args[++i] : "true";
    }

    return result;
}
