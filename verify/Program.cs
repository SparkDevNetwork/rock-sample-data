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

using Microsoft.Data.SqlClient;

using RockSampleData.Loader;
using RockSampleData.Loader.Database;
using RockSampleData.Loader.Model;
using RockSampleData.Loader.Packages;
using RockSampleData.Loader.Validation;
using RockSampleData.Verify;

/*
    The verify tool: proves that dataset packages work on the Rock release they were built on, and writes a receipt for each.
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
    // The database server's date by default: Rock Cleanup, the referee, computes ages and daily counts from that clock (decision 152).
    var anchor = options.TryGetValue( "anchor", out var anchorText ) ? DateTime.Parse( anchorText ) : sql.Today();
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
    var perDataset = new Dictionary<string, (string Package, string Sha256, PackageManifest Manifest, List<CheckResult> Checks, Receipt.ReceiptLoad Load)>();

    // Every load makes its logins with a test password that Rock's own Database login hashed, the way the Sample Data
    // Installer block hashes the one a person types (decision 180). It is not a secret: it signs in only to this
    // run's throwaway databases.
    const string TestLoginPassword = "sample-data-verify";
    string loginPasswordHash = null;
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
        Receipt.ReceiptLoad loadRecord = null;
        sql.Copy( baseDatabase, database );
        var stage = "load";
        try
        {
            stage = "load";
            loginPasswordHash ??= RockRunner.Run( rock, sql, database, "hash-password", new[] { TestLoginPassword } ).Trim();
            var load = Stopwatch.StartNew();
            var progress = new ConsoleProgress();
            var space = new SpaceSampler( sql, database );
            try
            {
                DatasetLoader.Load( connection, package, new DatasetLoadOptions
                {
                    Anchor = anchor,
                    Seed = seed,
                    Progress = progress,
                    RockWebConfigPath = rock.WebConfigPath,
                    RockVersion = builtRockVersion,
                    LoginPasswordHash = loginPasswordHash,
                    Recorder = recorder
                } );
            }
            finally
            {
                loadRecord = new Receipt.ReceiptLoad { Timings = progress.Timings.Select( Receipt.ReceiptTiming.From ).ToList(), Space = space.Stop() };
            }

            Console.WriteLine( $"  Space: {loadRecord.Space}" );
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
            // Rock Cleanup recomputes ages and daily counts for "today", so it can only judge a load anchored to today.
            // Checked before and after, because the job itself takes minutes and the date can change while it runs.
            var cleanup = ClockCheck( sql, anchor ) ?? referee.Referee( database, baseDatabase );
            checks.Add( ClockCheck( sql, anchor ) ?? cleanup );

            // After the referee, because a sign-in can rewrite a password hash.
            stage = "sign-in";
            checks.Add( SignIn( rock, sql, database, foreignKey, TestLoginPassword ) );

            stage = "data-views";
            var source = new SampleDataSource( package );
            checks.Add( DataViewsCheck( rock, sql, database, source ) );
            stage = "access";
            checks.Add( AccessCheck( rock, sql, database, source ) );
            stage = "reports";
            checks.Add( ReportsCheck( rock, sql, database, source ) );

            // Last, because a failing sync adds or removes members, and could make logins.
            stage = "group-sync";
            checks.Add( GroupSyncCheck( rock, sql, database, foreignKey, anchor ) );
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

        perDataset[dataset] = (packagePath, sha256, package.Manifest, checks, loadRecord);
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
    foreach ( var (dataset, (packagePath, sha256, manifest, checks, loadRecord)) in perDataset )
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
            Checks = all,
            Load = loadRecord
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
/// Null when the database server and this machine are both on the anchor's date, so Rock Cleanup's ages and
/// daily counts can be compared with the load. Otherwise a failed rock-cleanup check that names the dates,
/// instead of the false data changes Rock Cleanup would report. A run that crosses midnight lands here.
/// </summary>
static CheckResult ClockCheck( SqlServer sql, DateTime anchor )
{
    var serverToday = sql.Today();
    var machineToday = DateTime.Today;
    if ( serverToday == anchor.Date && machineToday == anchor.Date )
    {
        return null;
    }

    return CheckResult.From( "rock-cleanup", "Rock Cleanup was not judged: today's date is not the loads' anchor", new List<string>
    {
        $"The loads were anchored to {anchor:yyyy-MM-dd}, but the database server says {serverToday:yyyy-MM-dd} and this machine says {machineToday:yyyy-MM-dd}.",
        "Rock Cleanup recomputes ages and daily counts for today, so it would report false changes. If the date changed during the run, run it again. Otherwise leave --anchor out, and keep the database server and this machine in one time zone."
    } );
}

/// <summary>
/// Signs in through Rock's own Database login, the check Rock's login block makes (decision 180): as Ted Decker,
/// the Cast's best-known login, and as the load's newest confirmed login, with the test password, and as Ted with a
/// wrong password, which Rock must refuse. This proves Rock accepts the hash the load wrote.
/// </summary>
static CheckResult SignIn( RockRelease rock, SqlServer sql, string database, string foreignKey, string password )
{
    var newest = sql.Scalar( database, "SELECT TOP 1 [UserName] FROM [UserLogin] WHERE [ForeignKey] = @ForeignKey AND [IsConfirmed] = 1 AND [UserName] <> 'tdecker' ORDER BY [Id] DESC",
        new SqlParameter( "@ForeignKey", foreignKey ) ) as string;
    var tries = new List<(string UserName, string Password, bool IsAccepted)> { ("tdecker", password, true) };
    if ( newest != null )
    {
        tries.Add( (newest, password, true) );
    }

    tries.Add( ("tdecker", $"not-{password}", false) );
    var results = RockRunner.Run( rock, sql, database, "sign-in", tries.Select( t => $"{t.UserName}\t{t.Password}" ) )
        .Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
    var problems = new List<string>();
    for ( var i = 0; i < tries.Count; i++ )
    {
        var expected = $"{tries[i].UserName} {( tries[i].IsAccepted ? "signed in" : "refused" )}";
        var actual = i < results.Length ? results[i] : "no answer";
        if ( actual != expected )
        {
            problems.Add( $"Expected \"{expected}\" from Rock's Database login{( tries[i].IsAccepted ? string.Empty : " with a wrong password" )}, but got \"{actual}\"." );
        }
    }

    var accepted = string.Join( " and ", tries.Where( t => t.IsAccepted ).Select( t => t.UserName ) );
    return CheckResult.From( "sign-in", problems.Count == 0 ? $"Rock signed in as {accepted} and refused a wrong password" : "Rock's Database login did not treat the sample logins as it should", problems );
}

/// <summary>
/// Runs every data view in the load, the church's and Rock's own, through Rock's own engine. A filter whose selection Rock
/// cannot read fails here, the way it would for a person opening the view. Prints each church view's count and time.
/// </summary>
static CheckResult DataViewsCheck( RockRelease rock, SqlServer sql, string database, SampleDataSource source )
{
    var names = ( source.Reporting?.DataViews ?? new List<DataViewDefinition>() )
        .Concat( source.GroupSyncs?.DataViews ?? new List<DataViewDefinition>() )
        .Concat( source.Registrations?.DataViews ?? new List<DataViewDefinition>() )
        .ToDictionary( v => v.Guid, v => v.Key );
    var lines = RockRunner.Run( rock, sql, database, "data-views" ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
    var problems = new List<string>();
    var church = 0;
    foreach ( var parts in lines.Select( l => l.Split( '\t' ) ) )
    {
        var known = Guid.TryParse( parts.ElementAtOrDefault( 1 ), out var guid ) && names.ContainsKey( guid );
        var name = known ? names[guid] : $"Rock's data view {parts.ElementAtOrDefault( 1 )}";
        if ( parts[0] != "ok" )
        {
            problems.Add( $"Rock could not run {name}: {parts.ElementAtOrDefault( 2 )}" );
        }
        else if ( known )
        {
            church++;
            Console.WriteLine( $"    {name}: {int.Parse( parts[2] ):N0} in {parts[3]} ms" );
        }
    }

    return CheckResult.From( "data-views", problems.Count == 0 ? $"Rock ran all {lines.Length} data views, {church} of them the church's" : "Rock could not run every data view", problems );
}

/// <summary>
/// Asks Rock's own security check each question in the security walkthrough (configuration/reporting.json access): may
/// this Cast person take this action on this category or data view. Rock walks each item's categories as it does on a page.
/// </summary>
static CheckResult ReportsCheck( RockRelease rock, SqlServer sql, string database, SampleDataSource source )
{
    var names = ( source.Reporting?.Reports ?? new List<ReportDefinition>() ).ToDictionary( r => r.Guid, r => r.Key );
    var lines = RockRunner.Run( rock, sql, database, "reports" ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
    var problems = lines.Select( l => l.Split( '\t' ) ).Where( p => p[0] != "ok" )
        .Select( p => $"{( Guid.TryParse( p.ElementAtOrDefault( 1 ), out var g ) && names.TryGetValue( g, out var key ) ? key : $"Rock's report {p.ElementAtOrDefault( 1 )}" )}: {p.ElementAtOrDefault( 2 )}" )
        .ToList();
    return CheckResult.From( "reports", problems.Count == 0 ? $"Rock reads every column of all {lines.Length} reports, and renders every Lava column" : "Rock cannot read every report column", problems );
}

static CheckResult AccessCheck( RockRelease rock, SqlServer sql, string database, SampleDataSource source )
{
    var access = source.Reporting?.Access ?? new List<AccessExpectation>();
    if ( access.Count == 0 )
    {
        return CheckResult.From( "access", "This package has no access expectations", new List<string>() );
    }

    var categories = source.Categories.Categories.ToDictionary( c => c.Key, c => c.Guid );
    var views = source.Reporting.DataViews.Concat( source.GroupSyncs?.DataViews ?? new List<DataViewDefinition>() )
        .Concat( source.Registrations?.DataViews ?? new List<DataViewDefinition>() ).ToDictionary( v => v.Key, v => v.Guid );

    // Alisha Marble is Rock's own admin person, so her Guid is the one Rock gave Person 1 at install (decision 177).
    CastPersonGuids people;
    using ( var db = new RockDatabase( sql.ConnectionString( database ) ) )
    {
        people = new CastPersonGuids( db, source );
    }

    var reports = source.Reporting.Reports.ToDictionary( r => r.Key, r => r.Guid );
    var questions = access.Select( a => ( a.Category != null ? $"Category\t{categories[a.Category]}" : a.Report != null ? $"Report\t{reports[a.Report]}" : $"DataView\t{views[a.DataView]}" )
        + $"\t{a.Action}\t{people.Rock( a.Person )}" );
    var answers = RockRunner.Run( rock, sql, database, "authorize", questions ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
    var problems = new List<string>();
    for ( var i = 0; i < access.Count; i++ )
    {
        var expected = access[i].Allowed.ToString();
        var actual = i < answers.Length ? answers[i] : "no answer";
        if ( actual != expected )
        {
            problems.Add( $"{access[i].Name} {access[i].Action} {access[i].Category ?? access[i].DataView ?? access[i].Report}: expected {expected}, but Rock answered {actual}." );
        }
    }

    return CheckResult.From( "access", problems.Count == 0 ? $"Rock answered all {access.Count} security walkthrough questions as expected" : "Rock's security does not match the walkthrough", problems );
}

/// <summary>
/// Runs Rock's own group sync on every sync the load wrote and expects it to add, remove, and refuse nobody (decision
/// 187). Nothing changing proves the loader filled each group exactly as Rock's data views pick, and that the
/// syncs' welcome and exit emails and logins wait for a later change. The views count from today, so like Rock Cleanup
/// the check is judged only when today is the loads' anchor.
/// </summary>
static CheckResult GroupSyncCheck( RockRelease rock, SqlServer sql, string database, string foreignKey, DateTime anchor )
{
    var expected = Convert.ToInt32( sql.Scalar( database, "SELECT COUNT(*) FROM [GroupSync] WHERE [ForeignKey] = @ForeignKey", new SqlParameter( "@ForeignKey", foreignKey ) ) );
    if ( sql.Today() != anchor.Date || DateTime.Today != anchor.Date )
    {
        return CheckResult.From( "group-sync", "Group sync was not judged: today's date is not the loads' anchor", new List<string>
        {
            $"The loads were anchored to {anchor:yyyy-MM-dd}, but today is {DateTime.Today:yyyy-MM-dd}. The sync data views count from today, so run it again on one day."
        } );
    }

    var lines = RockRunner.Run( rock, sql, database, "group-sync" ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
    int Count( string label ) => lines.Where( l => l.StartsWith( label + " ", StringComparison.Ordinal ) ).Select( l => int.Parse( l.Substring( label.Length + 1 ) ) ).FirstOrDefault( -1 );
    var problems = new List<string>();
    if ( Count( "syncs" ) != expected )
    {
        problems.Add( $"Rock synced {Count( "syncs" )} groups, but the load wrote {expected} syncs." );
    }

    foreach ( var label in new[] { "added", "removed", "not added" } )
    {
        if ( Count( label ) != 0 )
        {
            problems.Add( $"Rock's group sync {label} {Count( label )} members. The loader must fill each synced group exactly as its data view picks." );
        }
    }

    problems.AddRange( lines.Where( l => l.StartsWith( "warning ", StringComparison.Ordinal ) ).Select( l => $"Rock's group sync warned: {l.Substring( 8 )}" ) );
    return CheckResult.From( "group-sync", problems.Count == 0 ? $"Rock's group sync changed nothing in the {expected} synced groups" : "Rock's group sync would change the synced groups", problems );
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
