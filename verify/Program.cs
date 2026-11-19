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
using RockSampleData.Loader.Loading;
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
      rock-sample-data-verify --rock <built Rock checkout> --anchor-sweep [days] [--dataset core-1] [--anchor first-day] [--packages <folder>]
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

    // --anchor-sweep only sweeps, for as many install days as asked (a normal run sweeps Core 1 too), and writes no receipt.
    if ( options.TryGetValue( "anchor-sweep", out var sweepDays ) )
    {
        var sweepDataset = datasetOption == "all" ? "core-1" : datasetOption;
        var sweepPackage = SampleDataPackage.Open( Path.Combine( packagesFolder, $"{sweepDataset}.zip" ) );
        var days = sweepDays == "true" ? AnchorSweep.DefaultDays : int.Parse( sweepDays );
        var sweep = AnchorSweep.Run( sql, baseDatabase, $"{runPrefix}-sweep", sweepPackage, rock, builtRockVersion, anchor, days, seed, options.ContainsKey( "keep" ) );
        sweep.Print();
        return sweep.Passed ? 0 : 1;
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
                    IsCastExpectationsChecked = true,
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
            var source = new SampleDataSource( package );
            using ( var db = new RockDatabase( connection ) )
            {
                var (expectations, failures) = CastExpectations.Evaluate( db, source, SampleDataParts.All.Select( p => p.Part ).ToList(), anchor );
                checks.Add( CheckResult.From( stage, $"{expectations - failures.Count} of {expectations} Cast expectations hold", failures ) );
            }

            stage = "invariants";
            // The dataset card was measured at the default seed, so only a default-seed load is held to its numbers.
            var card = seed == new DatasetLoadOptions().Seed ? SampleDataDatasets.Get( dataset ) : null;
            checks.Add( Invariants.Run( sql, database, foreignKey, card, source.Phones, Path.Combine( root, "validation", "invariants.sql" ) ) );
            stage = "rock-cleanup";
            // Rock Cleanup recomputes ages and daily counts for "today", so it can only judge a load anchored to today.
            // Checked before and after, because the job itself takes minutes and the date can change while it runs.
            var cleanup = ClockCheck( sql, anchor ) ?? referee.Referee( database, baseDatabase );
            checks.Add( ClockCheck( sql, anchor ) ?? cleanup );

            // After the referee, because a sign-in can rewrite a password hash.
            stage = "sign-in";
            checks.Add( SignIn( rock, sql, database, foreignKey, TestLoginPassword ) );

            stage = "data-views";
            var (dataViews, counts) = DataViewsCheck( rock, sql, database, source );
            checks.Add( dataViews );
            stage = "metrics";
            checks.Add( MetricsCheck( sql, database, source, counts ) );
            stage = "flow-audiences";
            checks.Add( FlowAudienceCheck( rock, sql, database, connection, source, anchor, foreignKey, seed ) );
            stage = "access";
            checks.Add( AccessCheck( rock, sql, database, source ) );
            stage = "reports";
            checks.Add( ReportsCheck( rock, sql, database, source ) );
            stage = "assessment-scores";
            checks.Add( AssessmentScoresCheck( rock, sql, database, connection ) );
            stage = "adaptive-messages";
            checks.Add( AdaptiveMessagesCheck( rock, sql, database, source ) );
            stage = "email-sections";
            checks.Add( EmailSectionsCheck( rock, sql, database, source ) );
            stage = "segments";
            checks.Add( SegmentsCheck( rock, sql, database, foreignKey, anchor ) );
            stage = "signals";
            checks.Add( SignalsCheck( rock, sql, database, foreignKey, anchor ) );
            stage = "personal-links";
            checks.Add( PersonalLinksCheck( rock, sql, database, source ) );
            stage = "notifications";
            checks.Add( NotificationsCheck( rock, sql, database, source, foreignKey, anchor ) );
            stage = "process-reminders";
            checks.Add( RemindersCheck( rock, sql, database, foreignKey, anchor ) );
            stage = "step-workflow-launch";
            checks.Add( StepWorkflowLaunchCheck( rock, sql, database, foreignKey ) );
            stage = "process-workflows";
            checks.Add( WorkflowsCheck( rock, sql, database, foreignKey ) );

            // Last, because a failing sync adds or removes members, and could make logins.
            stage = "group-sync";
            checks.Add( GroupSyncCheck( rock, sql, database, foreignKey, anchor ) );

            // After group sync, because Rock's requirement job reads the members a sync could have changed.
            stage = "group-requirements";
            checks.Add( GroupRequirementsCheck.Run( rock, sql, database, anchor ) );

            // The Cast giving stories must hold on every install day, not only this run's (decision 218). Core 1
            // holds every main-campus Cast giver, so it is the one swept.
            if ( dataset == "core-1" )
            {
                stage = "anchor-sweep";
                checks.Add( AnchorSweep.Run( sql, baseDatabase, $"{runPrefix}-sweep", package, rock, builtRockVersion, anchor, AnchorSweep.DefaultDays, seed, options.ContainsKey( "keep" ) ) );
            }
        }
        catch ( Exception ex ) when ( ex is not InvalidDataException )
        {
            var problems = new List<string> { ex.Message };
            if ( ex.Data[DatasetLoader.RollbackErrorKey] is { } rollbackError )
            {
                problems.Add( $"The rollback reported an error too: {( rollbackError as Exception )?.Message ?? rollbackError}" );
            }

            checks.Add( CheckResult.From( stage, "Could not finish", problems ) );
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
/// cannot read fails here, the way it would for a person opening the view. Prints each church view's count and time, and
/// returns the counts by key.
/// </summary>
/*
    10/30/26 - CLAUDE

    A recurring flow's past runs come from the loader's copy of its audience view's rule, since Rock cannot run a
    data view as of a past day. So the loader's rule, run as of install day, must find the same people as Rock's own
    engine running the guarded view, or the two copies have drifted apart. The views count from today, so it is
    judged only when the loads are anchored to today.

    Reason: the coordinator's guardrail for decision 237.
*/
static CheckResult FlowAudienceCheck( RockRelease rock, SqlServer sql, string database, string connection, SampleDataSource source, DateTime anchor, string foreignKey, string seed )
{
    if ( sql.Today() != anchor.Date || DateTime.Today != anchor.Date )
    {
        return CheckResult.From( "flow-audiences", "Flow audiences were not judged: today's date is not the loads' anchor", new List<string>
        {
            $"The loads were anchored to {anchor:yyyy-MM-dd}, but today is {DateTime.Today:yyyy-MM-dd}. The audience views count from today, so run it again on one day."
        } );
    }

    Dictionary<Guid, HashSet<int>> expected;
    using ( var db = new RockDatabase( connection ) )
    {
        expected = FlowLoader.AudiencesOnInstallDay( db, source, anchor, foreignKey, seed );
    }

    if ( expected.Count == 0 )
    {
        return CheckResult.From( "flow-audiences", "No recurring or one-time flow has an audience view in this load", new List<string>() );
    }

    var lines = RockRunner.Run( rock, sql, database, "data-view-people", expected.Keys.Select( g => g.ToString() ) )
        .Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
    var problems = new List<string>();
    foreach ( var (guid, loaderIds) in expected )
    {
        var name = source.Reporting.DataViews.First( v => v.Guid == guid ).Name;
        var line = lines.FirstOrDefault( l => l.StartsWith( guid.ToString(), StringComparison.OrdinalIgnoreCase ) );
        if ( line == null )
        {
            problems.Add( $"Rock did not run {name}." );
            continue;
        }

        var rockIds = ( line.Split( '\t' ).ElementAtOrDefault( 1 ) ?? string.Empty ).Split( ',', StringSplitOptions.RemoveEmptyEntries ).Select( int.Parse ).ToHashSet();
        var onlyLoader = loaderIds.Except( rockIds ).OrderBy( i => i ).ToList();
        var onlyRock = rockIds.Except( loaderIds ).OrderBy( i => i ).ToList();
        if ( onlyLoader.Count + onlyRock.Count > 0 )
        {
            problems.Add( $"{name}: the loader's rule finds {loaderIds.Count:N0} people and Rock finds {rockIds.Count:N0}. Only the loader: {onlyLoader.Count:N0} (such as {string.Join( ", ", onlyLoader.Take( 5 ) )}). Only Rock: {onlyRock.Count:N0} (such as {string.Join( ", ", onlyRock.Take( 5 ) )})." );
        }
    }

    return CheckResult.From( "flow-audiences", problems.Count == 0
        ? $"The loader's rule and Rock's own engine find the same {expected.Values.Sum( v => v.Count ):N0} people in {expected.Count} flow audiences"
        : "The loader's audience rules and Rock's views disagree", problems );
}

static (CheckResult Check, Dictionary<string, int> Counts) DataViewsCheck( RockRelease rock, SqlServer sql, string database, SampleDataSource source )
{
    var counts = new Dictionary<string, int>();
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
            counts[name] = int.Parse( parts[2] );
            Console.WriteLine( $"    {name}: {counts[name]:N0} in {parts[3]} ms" );
        }
    }

    return (CheckResult.From( "data-views", problems.Count == 0 ? $"Rock ran all {lines.Length} data views, {church} of them the church's" : "Rock could not run every data view", problems ), counts);
}

/// <summary>
/// Checks each path metric (decision 234) against Rock. Its stage view counts active people with a completed
/// step of the stage, so the people whose first such step came after the newest value were not counted then. The newest
/// value must be Rock's count of the view today less those people. The same rule read in SQL must also give Rock's count,
/// so the subtraction uses the view's own rule.
/// </summary>
static CheckResult MetricsCheck( SqlServer sql, string database, SampleDataSource source, Dictionary<string, int> counts )
{
    var problems = new List<string>();
    var metrics = source.Reporting?.Metrics ?? new List<MetricDefinition>();
    foreach ( var metric in metrics )
    {
        var latest = sql.Query( database, @"
SELECT TOP 1 [mv].[YValue], [mv].[MetricValueDateTime]
FROM [MetricValue] AS [mv]
INNER JOIN [Metric] AS [m] ON [m].[Id] = [mv].[MetricId]
WHERE [m].[Guid] = @Guid
ORDER BY [mv].[MetricValueDateTime] DESC", new SqlParameter( "@Guid", metric.Guid ) ).FirstOrDefault();
        if ( latest == null || !counts.TryGetValue( metric.DataView, out var rockCount ) )
        {
            problems.Add( $"{metric.Key}: no values, or Rock did not run {metric.DataView}" );
            continue;
        }

        var view = source.Reporting.DataViews.First( v => v.Key == metric.DataView );
        var matches = string.Join( " OR ", Leaves( view.Filter ).Where( f => f.Steps != null ).Select( f =>
        {
            var program = source.Steps.Programs.First( p => p.Key == f.Steps.Program );
            var types = program.StepTypes.Where( t => f.Steps.StepType != null ? t.Key == f.Steps.StepType : f.Steps.Stage == null || t.Stage == f.Steps.Stage );
            return $"( [st].[Guid] = '{program.Statuses.Complete}' AND [t].[Guid] IN ( {string.Join( ", ", types.Select( t => $"'{t.Guid}'" ) )} ) )";
        } ) );
        var row = sql.Query( database, $@"
SELECT COUNT(*) AS [Now], SUM( CASE WHEN [First] > @Taken THEN 1 ELSE 0 END ) AS [Later]
FROM (
    SELECT MIN( COALESCE( [s].[CompletedDateTime], [s].[StartDateTime], [s].[CreatedDateTime] ) ) AS [First]
    FROM [Step] AS [s]
    INNER JOIN [StepType] AS [t] ON [t].[Id] = [s].[StepTypeId]
    INNER JOIN [StepStatus] AS [st] ON [st].[Id] = [s].[StepStatusId]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
    INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
    INNER JOIN [DefinedValue] AS [status] ON [status].[Id] = [p].[RecordStatusValueId]
    LEFT JOIN [DefinedValue] AS [type] ON [type].[Id] = [p].[RecordTypeValueId]
    WHERE ( {matches} ) AND [p].[IsDeceased] = 0 AND [status].[Guid] = '618F906C-C33D-4FA3-8AEF-E58CB7B63F1E'
        AND ISNULL( [type].[Guid], '00000000-0000-0000-0000-000000000000' ) <> '721300ED-1267-4DA0-B4F2-6C6B5B17B1C5'
    GROUP BY [pa].[PersonId] ) AS [people]", new SqlParameter( "@Taken", latest["MetricValueDateTime"] ) ).Single();
        var now = Convert.ToInt32( row["Now"] );
        var later = Convert.ToInt32( row["Later"] ?? 0 );
        var value = Convert.ToInt32( latest["YValue"] );
        if ( now != rockCount )
        {
            problems.Add( $"{metric.Key}: the stage rule finds {now:N0} people, but Rock's {metric.DataView} finds {rockCount:N0}" );
        }
        else if ( value != rockCount - later )
        {
            problems.Add( $"{metric.Key}: its newest value, {value:N0} on {latest["MetricValueDateTime"]:g}, should be {rockCount - later:N0}: Rock's {rockCount:N0} less {later:N0} who reached the stage since" );
        }
        else
        {
            Console.WriteLine( $"    {metric.Key}: {value:N0} on {latest["MetricValueDateTime"]:g}, and {rockCount:N0} today" );
        }
    }

    return CheckResult.From( "metrics", problems.Count == 0 ? $"Each of the {metrics.Count} path metrics' newest value matches Rock's count of its stage view" : "A path metric does not match its stage view", problems );

    static IEnumerable<DataViewFilterDefinition> Leaves( DataViewFilterDefinition filter ) =>
        filter.Component != null ? new[] { filter } : filter.Children.SelectMany( Leaves );
}

/// <summary>
/// Asks Rock's own security check each question in the security walkthrough (configuration/reporting.json access): may
/// this Cast person take this action on this category or data view. Rock walks each item's categories as it does on a page.
/// </summary>
static CheckResult ReportsCheck( RockRelease rock, SqlServer sql, string database, SampleDataSource source )
{
    var names = ( source.Reporting?.Reports ?? new List<ReportDefinition>() ).ToDictionary( r => r.Guid, r => r.Key );
    var all = RockRunner.Run( rock, sql, database, "reports" ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).Select( l => l.Split( '\t' ) ).ToList();
    var webOnly = all.Where( p => p[0] == "web-only" ).Select( p => p[1] ).FirstOrDefault() ?? "0";
    var lines = all.Where( p => p[0] != "web-only" ).ToList();
    var problems = lines.Where( p => p[0] != "ok" )
        .Select( p => $"{( Guid.TryParse( p.ElementAtOrDefault( 1 ), out var g ) && names.TryGetValue( g, out var key ) ? key : $"Rock's report {p.ElementAtOrDefault( 1 )}" )}: {p.ElementAtOrDefault( 2 )}" )
        .ToList();

    // A link column (Person Link, Group Link) builds its URL from the web site's root, which only IIS has.
    return CheckResult.From( "reports", problems.Count == 0 ? $"Rock reads every column of all {lines.Count} reports and renders every Lava column ({webOnly} link columns need the web site)" : "Rock cannot read every report column", problems );
}

/// <summary>
/// Scores every assessment's saved answers with Rock's own scoring services (decision 265). Each person's latest
/// result of each type must give exactly the person attribute values the load wrote, and Rock must write the same answer
/// data. A legacy DISC result has no answers, so the Cast expectations check it instead.
/// </summary>
static CheckResult AssessmentScoresCheck( RockRelease rock, SqlServer sql, string database, string connection )
{
    var problems = new List<string>();
    var scored = new Dictionary<int, Dictionary<string, string>>();
    foreach ( var parts in RockRunner.Run( rock, sql, database, "assessment-scores" ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).Select( l => l.Split( '\t' ) ) )
    {
        var id = int.Parse( parts[0] );
        if ( parts[1] == "error" )
        {
            problems.Add( $"Rock could not score assessment {id}: {parts.ElementAtOrDefault( 2 )}" );
            continue;
        }

        if ( parts[1] != "same" )
        {
            problems.Add( $"Assessment {id}'s answer data is not what Rock's block writes for the same answers" );
        }

        scored[id] = parts.Skip( 2 ).Select( p => p.Split( '=', 2 ) ).ToDictionary( p => p[0], p => p[1] );
    }

    using var db = new RockDatabase( connection );
    var completed = db.Query( @"
SELECT [a].[Id], [pa].[PersonId], [a].[AssessmentTypeId], [a].[CompletedDateTime], CASE WHEN [a].[AssessmentResultData] IS NULL THEN 0 ELSE 1 END AS [HasAnswers]
FROM [Assessment] AS [a]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
WHERE [a].[Status] = 1" );
    var withAnswers = completed.Count( r => Convert.ToInt32( r["HasAnswers"] ) == 1 );
    if ( withAnswers != scored.Count + problems.Count( p => p.StartsWith( "Rock could not score", StringComparison.Ordinal ) ) )
    {
        problems.Add( $"Rock scored {scored.Count} answer sets, but {withAnswers} completed assessments have answers" );
    }

    var stored = db.Query( @"
SELECT [av].[EntityId], [a].[Key], [av].[Value]
FROM [AttributeValue] AS [av]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
INNER JOIN [EntityType] AS [et] ON [et].[Id] = [a].[EntityTypeId] AND [et].[Name] = 'Rock.Model.Person'
WHERE [a].[Key] IN ( 'AdaptiveD', 'AdaptiveI', 'AdaptiveS', 'AdaptiveC', 'NaturalD', 'NaturalI', 'NaturalS', 'NaturalC', 'PersonalityType', 'core_DISCDISCProfile' )
    OR [a].[Key] LIKE 'core[_]%Gifts' OR [a].[Key] LIKE 'core[_]Conflict%' OR [a].[Key] LIKE 'core[_]EQ%' OR [a].[Key] LIKE 'core[_]Motivator%'" )
        .ToDictionary( r => (( int ) r["EntityId"], ( string ) r["Key"]), r => r["Value"] as string ?? string.Empty );
    var latest = completed.GroupBy( r => (( int ) r["PersonId"], ( int ) r["AssessmentTypeId"]) )
        .Select( g => g.OrderByDescending( r => ( DateTime ) r["CompletedDateTime"] ).ThenByDescending( r => ( int ) r["Id"] ).First() )
        .Where( r => Convert.ToInt32( r["HasAnswers"] ) == 1 && scored.ContainsKey( ( int ) r["Id"] ) )
        .ToList();
    foreach ( var row in latest )
    {
        var personId = ( int ) row["PersonId"];
        foreach ( var (key, value) in scored[( int ) row["Id"]] )
        {
            // Rock saves no empty value it had not saved before.
            var saved = stored.TryGetValue( (personId, key), out var text ) ? text : string.Empty;
            if ( saved != value && problems.Count < 25 )
            {
                problems.Add( $"Person {personId}: Rock scores {key} as \"{value}\" from assessment {row["Id"]}'s answers, the load saved \"{saved}\"" );
            }
        }
    }

    return CheckResult.From( "assessment-scores", problems.Count == 0
        ? $"Rock's own scoring gives all {scored.Count:N0} saved answer sets the answer data the load wrote, and the latest {latest.Count:N0} results the person attribute values it wrote"
        : "Rock's own scoring does not agree with the assessments the load wrote", problems );
}

static CheckResult AccessCheck( RockRelease rock, SqlServer sql, string database, SampleDataSource source )
{
    var access = ( source.Reporting?.Access ?? new List<AccessExpectation>() ).Concat( source.AdaptiveMessages?.Access ?? new List<AccessExpectation>() )
        .Concat( source.ReadSnippetLibrary()?.Access ?? new List<AccessExpectation>() )
        .Concat( source.Signals?.Access ?? new List<AccessExpectation>() )
        .Concat( source.Tags?.Access ?? new List<AccessExpectation>() )
        .Concat( source.Benevolence?.Access ?? new List<AccessExpectation>() ).ToList();
    if ( access.Count == 0 )
    {
        return CheckResult.From( "access", "This package has no access expectations", new List<string>() );
    }

    var categories = source.Categories.Categories.ToDictionary( c => c.Key, c => c.Guid );
    var views = source.Reporting.DataViews.Concat( source.GroupSyncs?.DataViews ?? new List<DataViewDefinition>() )
        .Concat( source.Registrations?.DataViews ?? new List<DataViewDefinition>() ).ToDictionary( v => v.Key, v => v.Guid );
    var segments = ( source.AdaptiveMessages?.Segments ?? new List<PersonalizationSegmentDefinition>() ).ToDictionary( s => s.Key, s => s.Guid );

    // Alisha Marble is Rock's own admin person, so her Guid is the one Rock gave Person 1 at install (decision 177).
    CastPersonGuids people;
    using ( var db = new RockDatabase( sql.ConnectionString( database ) ) )
    {
        people = new CastPersonGuids( db, source );
    }

    var reports = source.Reporting.Reports.ToDictionary( r => r.Key, r => r.Guid );
    var signalTypes = ( source.Signals?.Types ?? new List<SignalTypeDefinition>() ).ToDictionary( t => t.Key, t => t.Guid );
    var tags = ( source.Tags?.Tags ?? new List<TagDefinition>() ).ToDictionary( t => t.Key, t => t.Guid );
    var benevolenceTypes = ( source.Benevolence?.Types ?? new List<BenevolenceTypeDefinition>() ).ToDictionary( t => t.Key, t => t.Guid );
    var questions = access.Select( a => ( a.Snippet != null ? $"Snippet\t{a.Snippet}"
            : a.BenevolenceType != null ? $"BenevolenceType\t{benevolenceTypes[a.BenevolenceType]}"
            : a.Category != null ? $"Category\t{categories[a.Category]}"
            : a.Tag != null ? $"Tag\t{tags[a.Tag]}"
            : a.Report != null ? $"Report\t{reports[a.Report]}"
            : a.Segment != null ? $"PersonalizationSegment\t{segments[a.Segment]}"
            : a.Page != null ? $"Page\t{a.Page}"
            : a.SignalType != null ? $"SignalType\t{signalTypes[a.SignalType]}"
            : $"DataView\t{views[a.DataView]}" )
        + $"\t{a.Action}\t{people.Rock( a.Person )}" );
    var answers = RockRunner.Run( rock, sql, database, "authorize", questions ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
    var problems = new List<string>();
    for ( var i = 0; i < access.Count; i++ )
    {
        var expected = access[i].Allowed.ToString();
        var actual = i < answers.Length ? answers[i] : "no answer";
        if ( actual != expected )
        {
            problems.Add( $"{access[i].Name} {access[i].Action} {access[i].Category ?? access[i].DataView ?? access[i].Report ?? access[i].Segment ?? access[i].SignalType ?? access[i].BenevolenceType ?? ( access[i].Tag != null ? $"tag {access[i].Tag}" : access[i].Snippet != null ? $"snippet {access[i].Snippet}" : $"page {access[i].Page}" )}: expected {expected}, but Rock answered {actual}." );
        }
    }

    return CheckResult.From( "access", problems.Count == 0 ? $"Rock answered all {access.Count} security walkthrough questions as expected" : "Rock's security does not match the walkthrough", problems );
}

/// <summary>
/// Rock's own save rules accept every email section the load wrote, as they are: the model's validation, and the string
/// validation Rock's email editor runs on a section's markup when staff save it (decision 301).
/// </summary>
static CheckResult EmailSectionsCheck( RockRelease rock, SqlServer sql, string database, SampleDataSource source )
{
    var names = ( source.ReadEmailSectionLibrary()?.Sections ?? new List<EmailSectionDefinition>() ).ToDictionary( s => s.Guid, s => s.Name );
    if ( names.Count == 0 )
    {
        return CheckResult.From( "email-sections", "This package has no email sections", new List<string>() );
    }

    var lines = RockRunner.Run( rock, sql, database, "email-sections" ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).Select( l => l.Split( '\t' ) ).ToList();
    var found = lines.Select( p => Guid.TryParse( p.ElementAtOrDefault( 1 ), out var guid ) ? guid : Guid.Empty ).ToHashSet();
    var problems = lines.Where( p => p[0] != "ok" )
        .Select( p => $"{( Guid.TryParse( p.ElementAtOrDefault( 1 ), out var guid ) && names.TryGetValue( guid, out var name ) ? name : $"Email section {p.ElementAtOrDefault( 1 )}" )}: {p.ElementAtOrDefault( 2 )}" )
        .Concat( names.Where( n => !found.Contains( n.Key ) ).Select( n => $"{n.Value}: the load did not write it." ) )
        .ToList();
    return CheckResult.From( "email-sections", problems.Count == 0 ? $"Rock's email editor would save all {lines.Count} email sections as they are" : "Rock's email editor would refuse an email section", problems );
}

/// <summary>
/// Renders Rock's adaptivemessage Lava command for each Cast person in configuration/adaptive-messages.json and expects
/// the adaptation their story gives them, with its call to action. It runs through Rock's own Lava engine and segment
/// lookup, so it proves the loader's segments, adaptations, and attribute values work the way a page shows them.
/// </summary>
static CheckResult AdaptiveMessagesCheck( RockRelease rock, SqlServer sql, string database, SampleDataSource source )
{
    var cast = source.AdaptiveMessages?.Cast ?? new List<CastAdaptation>();
    if ( cast.Count == 0 )
    {
        return CheckResult.From( "adaptive-messages", "This package has no adaptive message expectations", new List<string>() );
    }

    CastPersonGuids people;
    using ( var db = new RockDatabase( sql.ConnectionString( database ) ) )
    {
        people = new CastPersonGuids( db, source );
    }

    var messages = source.AdaptiveMessages.Messages.ToDictionary( m => m.Key );
    var adaptationIds = sql.Query( database, "SELECT [Id], [Guid] FROM [AdaptiveMessageAdaptation]" ).ToDictionary( r => ( int ) r["Id"], r => ( Guid ) r["Guid"] );

    // The page block, when the load added one, is rendered for each Cast person too, as the homepage shows it to them.
    var display = source.AdaptiveMessages.Display;
    var input = cast.Select( c => $"{people.Rock( c.Person )}\t{c.Message}" ).ToList();
    if ( display != null )
    {
        input.AddRange( cast.Select( c => $"{people.Rock( c.Person )}\tblock:{display.BlockGuid}" ) );
    }

    var lines = RockRunner.Run( rock, sql, database, "adaptive-messages", input ).Split( '\n' );
    var problems = new List<string>();
    for ( var i = 0; i < cast.Count; i++ )
    {
        var message = messages[cast[i].Message];
        var expected = cast[i].Adaptation == null ? null : message.Adaptations.First( a => a.Key == cast[i].Adaptation );
        var parts = ( i < lines.Length ? lines[i].Trim() : string.Empty ).Split( '\t' );
        if ( parts[0] == "error" )
        {
            problems.Add( $"{cast[i].Name}: Rock's Lava command failed for {message.Name}: {parts.ElementAtOrDefault( 1 )}" );
            continue;
        }

        Guid? shown = int.TryParse( parts[0], out var id ) && adaptationIds.TryGetValue( id, out var guid ) ? guid : null;
        if ( display != null && expected != null )
        {
            var page = cast.Count + i < lines.Length ? lines[cast.Count + i].Trim() : string.Empty;
            var callToAction = source.ReadAdaptiveMessageText().Messages[message.Key][expected.Key].CallToAction;
            if ( page.StartsWith( "error", StringComparison.Ordinal ) || !page.Contains( $">{callToAction}<", StringComparison.Ordinal ) )
            {
                problems.Add( $"{cast[i].Name}: the {display.Name} block did not show \"{callToAction}\" from {expected.Name}. Rock rendered: {( page.Length > 300 ? page.Substring( 0, 300 ) + "..." : page )}" );
            }
        }

        if ( shown != expected?.Guid )
        {
            var shownName = shown == null ? "no adaptation" : message.Adaptations.FirstOrDefault( a => a.Guid == shown )?.Name ?? shown.ToString();
            problems.Add( $"{cast[i].Name}: expected {message.Name} to show {expected?.Name ?? "no adaptation"} ({cast[i].Reason}), but Rock's Lava command showed {shownName}." );
        }
        else if ( shown != null && ( parts.Length < 2 || parts[1].Trim().Length == 0 ) )
        {
            problems.Add( $"{cast[i].Name}: Rock's Lava command showed {expected.Name} with no call to action." );
        }
    }

    var onPage = display == null ? string.Empty : $", and the {display.Name} block showed each one on the page";
    return CheckResult.From( "adaptive-messages", problems.Count == 0 ? $"Rock's adaptivemessage command showed each of the {cast.Count} Cast people their adaptation{onPage}" : "Rock's adaptivemessage command did not show the Cast their adaptations", problems );
}

/// <summary>
/// Runs Rock's own segment update on every personalization segment the load wrote and expects it to add and remove
/// nobody. Nothing changing proves the loader put exactly the people Rock's Update Personalization Data job would in each
/// segment. The views count from today, so the check is judged only when today is the loads' anchor.
/// </summary>
static CheckResult SegmentsCheck( RockRelease rock, SqlServer sql, string database, string foreignKey, DateTime anchor )
{
    var expected = sql.Query( database, "SELECT [Guid], [Name] FROM [PersonalizationSegment] WHERE [ForeignKey] = @ForeignKey", new SqlParameter( "@ForeignKey", foreignKey ) )
        .ToDictionary( r => ( Guid ) r["Guid"], r => ( string ) r["Name"] );
    if ( expected.Count == 0 )
    {
        return CheckResult.From( "segments", "This load wrote no personalization segments", new List<string>() );
    }

    if ( sql.Today() != anchor.Date || DateTime.Today != anchor.Date )
    {
        return CheckResult.From( "segments", "Segments were not judged: today's date is not the loads' anchor", new List<string>
        {
            $"The loads were anchored to {anchor:yyyy-MM-dd}, but today is {DateTime.Today:yyyy-MM-dd}. The segment data views count from today, so run it again on one day."
        } );
    }

    var lines = RockRunner.Run( rock, sql, database, "segments" ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).Select( l => l.Split( '\t' ) ).ToList();
    var problems = new List<string>();
    foreach ( var line in lines.Where( l => l.Length == 3 && Guid.TryParse( l[0], out var guid ) && expected.ContainsKey( guid ) ) )
    {
        if ( line[1] != "0" || line[2] != "0" )
        {
            problems.Add( $"Rock's segment update added {line[1]} and removed {line[2]} in {expected[Guid.Parse( line[0] )]}. The loader must put exactly the people its data view finds in each segment." );
        }
    }

    var updated = lines.Count( l => l.Length == 3 && Guid.TryParse( l[0], out var guid ) && expected.ContainsKey( guid ) );
    if ( updated != expected.Count )
    {
        problems.Add( $"Rock updated {updated} of the load's {expected.Count} segments." );
    }

    return CheckResult.From( "segments", problems.Count == 0 ? $"Rock's segment update changed nothing in the {expected.Count} personalization segments" : "Rock's segment update would change the segments", problems );
}

/// <summary>
/// Runs Rock's own Person.CalculateSignals for everyone with a signal and expects it to change no one's top signal, so the
/// Top Person Signal badge shows each person the flag Rock would. An ended signal counts from now, so like the segments the
/// check is judged only when today is the loads' anchor.
/// </summary>
static CheckResult SignalsCheck( RockRelease rock, SqlServer sql, string database, string foreignKey, DateTime anchor )
{
    var expected = Convert.ToInt32( sql.Scalar( database, "SELECT COUNT( DISTINCT [PersonId] ) FROM [PersonSignal] WHERE [ForeignKey] = @ForeignKey", new SqlParameter( "@ForeignKey", foreignKey ) ) );
    if ( expected == 0 )
    {
        return CheckResult.From( "signals", "This load wrote no person signals", new List<string>() );
    }

    if ( sql.Today() != anchor.Date || DateTime.Today != anchor.Date )
    {
        return CheckResult.From( "signals", "Signals were not judged: today's date is not the loads' anchor", new List<string>
        {
            $"The loads were anchored to {anchor:yyyy-MM-dd}, but today is {DateTime.Today:yyyy-MM-dd}. A signal's end counts from today, so run it again on one day."
        } );
    }

    var lines = RockRunner.Run( rock, sql, database, "signals" ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
    var problems = lines.Where( l => l.Contains( '\t' ) ).Select( l => l.Split( '\t' ) )
        .Select( p => $"Rock would change person {p[0]}'s top signal from \"{p[1]}\" to \"{p[2]}\" (Id|color|icon)." ).ToList();
    var checkedLine = lines.FirstOrDefault( l => l.StartsWith( "people ", StringComparison.Ordinal ) );
    if ( checkedLine != $"people {expected}" )
    {
        problems.Add( $"Rock checked {checkedLine ?? "no people"}, but the load gave {expected} people signals." );
    }

    return CheckResult.From( "signals", problems.Count == 0 ? $"Rock's own top signal rule agrees with all {expected} people the load gave signals" : "Rock would change people's top signals", problems );
}

/// <summary>
/// Asks Rock's own bookmark menu data for each person in configuration/personal-links.json's menus and expects their
/// sections in order, each with its links from the file. Rock must find no section order missing for anyone who owns a
/// section, and the
/// version Rock caches the menu by must come from the links' own dates, not midnight today, which is the version of a
/// database with no links, so a browser that cached an empty menu refreshes it.
/// </summary>
static CheckResult PersonalLinksCheck( RockRelease rock, SqlServer sql, string database, SampleDataSource source )
{
    var configuration = source.PersonalLinks;
    if ( configuration == null || configuration.Menus.Count == 0 )
    {
        return CheckResult.From( "personal-links", "This package has no bookmark menus to check", new List<string>() );
    }

    CastPersonGuids people;
    using ( var db = new RockDatabase( sql.ConnectionString( database ) ) )
    {
        people = new CastPersonGuids( db, source );
    }

    // Each section's links by section name. A campus pastor's section takes their campus's name in its links.
    var campusNames = source.Campuses.Campuses.ToDictionary( c => c.Key, c => c.Name );
    var pastorCampus = source.Staff.CampusPastors.ToDictionary( p => source.Staff.Staff.First( s => s.Key == p.Value ).Person, p => p.Key );
    var links = configuration.SharedSections.ToDictionary( s => s.Name, s => s.Links.Select( l => l.Name ).ToList() );
    var ownSections = configuration.PersonalSections.ToDictionary( s => (s.Owner, s.Name), s => s.Links.Select( l => l.Name ).ToList() );
    List<string> Expected( Guid person, string section ) =>
        ownSections.TryGetValue( (person, section), out var own ) ? own
        : section == configuration.CampusPastorSection.Name && pastorCampus.TryGetValue( person, out var campus ) ? configuration.CampusPastorSection.Links.Select( l => l.Name.Replace( "{campus}", campusNames[campus] ) ).ToList()
        : links.TryGetValue( section, out var shared ) ? shared : new List<string>();

    var owners = configuration.PersonalSections.Select( s => s.Owner ).Concat( pastorCampus.Keys ).ToHashSet();
    var lines = RockRunner.Run( rock, sql, database, "personal-links", configuration.Menus.Select( m => people.Rock( m.Person ).ToString() ) ).Split( '\n' ).Select( l => l.TrimEnd( '\r' ) ).ToList();
    var problems = new List<string>();
    var version = lines.FirstOrDefault( l => l.StartsWith( "shared\t", StringComparison.Ordinal ) )?.Substring( 7 );
    var today = lines.FirstOrDefault( l => l.StartsWith( "today\t", StringComparison.Ordinal ) )?.Substring( 6 );
    if ( version == null || version == today )
    {
        problems.Add( $"Rock's version of the shared links is {version ?? "missing"}, midnight today, the version of a database with no links. Each shared section and link needs a ModifiedDateTime." );
    }

    var menus = lines.Skip( 2 ).ToList();
    for ( var i = 0; i < configuration.Menus.Count; i++ )
    {
        var menu = configuration.Menus[i];
        var parts = i < menus.Count ? menus[i].Split( '\t' ) : new[] { "missing" };
        if ( parts.Length < 2 )
        {
            problems.Add( $"{menu.Name}: Rock gave no bookmark menu ({parts[0]})." );
            continue;
        }

        // Rock adds orders for anyone the first time they open their links page, so only an owner, who arranged their
        // sections, must have none missing.
        if ( owners.Contains( menu.Person ) && parts[0] != "False" )
        {
            problems.Add( $"{menu.Name}: Rock would add a missing section order when they open their links." );
        }

        if ( owners.Contains( menu.Person ) && parts[1] == today )
        {
            problems.Add( $"{menu.Name}: Rock's version of their own links is midnight today. Their sections, links, and orders need a ModifiedDateTime." );
        }

        var sections = parts.Skip( 2 ).Select( s => s.Split( '>' ) ).ToList();
        var names = sections.Select( s => s[0] ).ToList();
        if ( !names.SequenceEqual( menu.Sections ) )
        {
            problems.Add( $"{menu.Name}: expected the sections {string.Join( ", ", menu.Sections.DefaultIfEmpty( "(none)" ) )}, Rock lists {string.Join( ", ", names.DefaultIfEmpty( "(none)" ) )}." );
            continue;
        }

        foreach ( var section in sections )
        {
            var expected = Expected( menu.Person, section[0] );
            if ( !section.Skip( 1 ).SequenceEqual( expected ) )
            {
                problems.Add( $"{menu.Name}: expected {section[0]} to list {string.Join( ", ", expected )}, Rock lists {string.Join( ", ", section.Skip( 1 ) )}." );
            }
        }
    }

    return CheckResult.From( "personal-links", problems.Count == 0 ? $"Rock's bookmark menu shows each of the {configuration.Menus.Count} people their sections and links in order, and finds no section order missing" : "Rock's bookmark menus do not match the file", problems );
}

/// <summary>
/// Asks Rock what each note author's top-bar bell shows (decision 307): the unread count Rock reads must be every
/// staff mention in the last three days' notes, found from the notes themselves, and the newest mention's link must open
/// the note on the person's profile. It is judged only when today is the loads' anchor, because the bell counts from now.
/// </summary>
static CheckResult NotificationsCheck( RockRelease rock, SqlServer sql, string database, SampleDataSource source, string foreignKey, DateTime anchor )
{
    var foreignKeyParameter = new SqlParameter( "@ForeignKey", foreignKey );
    if ( source.Notes == null || Convert.ToInt32( sql.Scalar( database, "SELECT COUNT(*) FROM [NotificationMessage] WHERE [ForeignKey] = @ForeignKey", foreignKeyParameter ) ) == 0 )
    {
        return CheckResult.From( "notifications", "This load wrote no mention notifications", new List<string>() );
    }

    if ( sql.Today() != anchor.Date || DateTime.Today != anchor.Date )
    {
        return CheckResult.From( "notifications", "Notifications were not judged: today's date is not the loads' anchor", new List<string>
        {
            $"The loads were anchored to {anchor:yyyy-MM-dd}, but today is {DateTime.Today:yyyy-MM-dd}. The bell counts unread mentions from now, so run it again on one day."
        } );
    }

    CastPersonGuids people;
    using ( var db = new RockDatabase( sql.ConnectionString( database ) ) )
    {
        people = new CastPersonGuids( db, source );
    }

    // From the notes alone: each person's mentions in the last three days by someone else, and their newest mention.
    var staff = source.Notes.Staff.Select( s => (s.Name, Guid: people.Rock( s.Guid )) ).ToList();
    var expected = staff.Select( s => sql.Query( database, $@"
SELECT COUNT( [n].[Id] ) AS [Unread],
    ( SELECT TOP 1 CONCAT( [nm].[Guid], '|/person/', [n2].[EntityId], '#NoteRef-', LOWER( REPLACE( CONVERT( nvarchar( 36 ), [n2].[Guid] ), '-', '' ) ) )
        FROM [NotificationMessage] AS [nm]
        INNER JOIN [Note] AS [n2] ON [nm].[Key] = 'note-' + CAST( [n2].[Id] AS nvarchar( 20 ) )
        WHERE [nm].[PersonAliasId] = [p].[PrimaryAliasId] AND [nm].[ForeignKey] = @ForeignKey
        ORDER BY [nm].[MessageDateTime] DESC ) AS [Newest]
FROM [Person] AS [p]
LEFT JOIN [Note] AS [n] ON [n].[ForeignKey] = @ForeignKey AND [n].[CreatedDateTime] >= DATEADD( DAY, -{MentionNotificationsWriter.UnreadDays}, CAST( GETDATE() AS DATE ) )
    AND [n].[CreatedByPersonAliasId] <> [p].[PrimaryAliasId]
    AND [n].[Text] LIKE '%class=""mention""%>@' + [p].[NickName] + ' ' + [p].[LastName] + '</span>%'
WHERE [p].[Guid] = @Person
GROUP BY [p].[PrimaryAliasId]", new SqlParameter( "@ForeignKey", foreignKey ), new SqlParameter( "@Person", s.Guid ) ).FirstOrDefault() ).ToList();

    var loaded = staff.Zip( expected ).Where( x => x.Second != null ).ToList();
    var withLinks = loaded.Where( x => x.Second["Newest"] is string ).ToList();
    var questions = loaded.Select( x => $"count\t{x.First.Guid}" ).Concat( withLinks.Select( x => $"link\t{( ( string ) x.Second["Newest"] ).Split( '|' )[0]}" ) );
    var answers = RockRunner.Run( rock, sql, database, "notifications", questions ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
    var problems = new List<string>();
    for ( var i = 0; i < loaded.Count; i++ )
    {
        var want = Convert.ToInt32( loaded[i].Second["Unread"] ).ToString();
        var got = i < answers.Length ? answers[i] : "no answer";
        if ( got != want )
        {
            problems.Add( $"{loaded[i].First.Name}: the last three days' notes mention them {want} times, but Rock's bell counts {got} unread." );
        }
    }

    for ( var i = 0; i < withLinks.Count; i++ )
    {
        var want = ( ( string ) withLinks[i].Second["Newest"] ).Split( '|' )[1];
        var got = loaded.Count + i < answers.Length ? answers[loaded.Count + i] : "no answer";
        if ( got != want )
        {
            problems.Add( $"{withLinks[i].First.Name}: their newest mention should open {want}, but Rock's link is {got}." );
        }
    }

    var total = loaded.Sum( x => Convert.ToInt32( x.Second["Unread"] ) );
    return CheckResult.From( "notifications", problems.Count == 0
        ? $"Rock's bell counts each of {loaded.Count} note authors' unread mentions ({total} in all), and each newest mention opens its note"
        : "Rock's bell does not show the mentions the notes hold", problems );
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
/// Runs Rock's own Process Reminders job, as Rock's scheduler runs it every weekday morning, and expects it to change no
/// reminder and nobody's reminder count, the number Rock's bell shows (issue 22). The job also emails each owner the
/// reminders due, and the trusted base has no mail server, so a send Rock could not make is noted, not failed. Any other
/// error, such as a reminder whose person or group is missing, fails. Reminders come due by the day, so like Rock Cleanup
/// the check is judged only when today is the loads' anchor, checked before and after the job.
/// </summary>
static CheckResult RemindersCheck( RockRelease rock, SqlServer sql, string database, string foreignKey, DateTime anchor )
{
    var written = Convert.ToInt32( sql.Scalar( database, "SELECT COUNT(*) FROM [Reminder] WHERE [ForeignKey] = @ForeignKey", new SqlParameter( "@ForeignKey", foreignKey ) ) );
    if ( written == 0 )
    {
        return CheckResult.From( "process-reminders", "This load wrote no reminders", new List<string>() );
    }

    if ( sql.Today() != anchor.Date || DateTime.Today != anchor.Date )
    {
        return CheckResult.From( "process-reminders", "Process Reminders was not judged: today's date is not the loads' anchor", new List<string>
        {
            $"The loads were anchored to {anchor:yyyy-MM-dd}, but today is {DateTime.Today:yyyy-MM-dd}. Reminders come due by the day, so run it again on one day."
        } );
    }

    Dictionary<int, string> Reminders() => sql.Query( database, "SELECT [Id], [ReminderDate], [IsComplete], [RenewCurrentCount], [RenewMaxCount], [ModifiedDateTime] FROM [Reminder]" )
        .ToDictionary( r => ( int ) r["Id"], r => string.Join( "|", r.Values.Select( v => v is DateTime d ? d.ToString( "o" ) : v?.ToString() ) ) );
    Dictionary<int, int> Counts() => sql.Query( database, "SELECT [Id], [ReminderCount] FROM [Person] WHERE [ReminderCount] IS NOT NULL" )
        .ToDictionary( r => ( int ) r["Id"], r => ( int ) r["ReminderCount"] );

    var remindersBefore = Reminders();
    var countsBefore = Counts();
    var lines = RockRunner.Run( rock, sql, database, "process-reminders" ).Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
    var remindersAfter = Reminders();
    var countsAfter = Counts();

    // A run that crosses midnight lets reminders dated the next day come due while the job runs.
    if ( sql.Today() != anchor.Date || DateTime.Today != anchor.Date )
    {
        return CheckResult.From( "process-reminders", "Process Reminders was not judged: the date changed while the job ran", new List<string>
        {
            $"The loads were anchored to {anchor:yyyy-MM-dd}, but today is now {DateTime.Today:yyyy-MM-dd}. Reminders come due by the day, so run it again on one day."
        } );
    }

    var problems = new List<string>();
    var changedReminders = remindersBefore.Keys.Union( remindersAfter.Keys ).Where( id => remindersBefore.GetValueOrDefault( id ) != remindersAfter.GetValueOrDefault( id ) ).ToList();
    if ( changedReminders.Count > 0 )
    {
        problems.Add( $"Rock's Process Reminders job changed {changedReminders.Count} reminders, such as reminder {changedReminders[0]}. The loader must write each reminder as Rock's blocks leave it." );
    }

    foreach ( var personId in countsBefore.Keys.Union( countsAfter.Keys ).Where( id => countsBefore.GetValueOrDefault( id, 0 ) != countsAfter.GetValueOrDefault( id, 0 ) ).Take( 10 ) )
    {
        problems.Add( $"Rock's Process Reminders job changed person {personId}'s reminder count from {countsBefore.GetValueOrDefault( personId, 0 )} to {countsAfter.GetValueOrDefault( personId, 0 )}. The loader must count each owner's reminders due as the job does." );
    }

    var errors = lines.Where( l => l.StartsWith( "error ", StringComparison.Ordinal ) ).Select( l => l.Substring( 6 ) ).ToList();
    bool IsSend( string error ) => error.StartsWith( "Failed to send SystemCommunication", StringComparison.Ordinal ) || error.StartsWith( "Failed to create SystemCommunication", StringComparison.Ordinal );
    problems.AddRange( errors.Where( e => !IsSend( e ) ).Select( e => $"Rock's Process Reminders job reported: {e}" ) );
    var notes = errors.Count( IsSend ) == 0 ? new List<string>() : new List<string> { $"Rock could not email {errors.Count( IsSend )} owners their reminders due: the trusted base has no mail server. First: {errors.First( IsSend )}" };
    var processed = lines.FirstOrDefault( l => l.StartsWith( "processed ", StringComparison.Ordinal ) )?.Substring( 10 ) ?? "0";
    return CheckResult.From( "process-reminders", problems.Count == 0
        ? $"Rock's Process Reminders job changed none of the {written} reminders and no reminder count ({processed} reminders notified)"
        : "Rock's Process Reminders job would change the reminders", problems, notes );
}

/// <summary>
/// Launches step workflows with Rock's own code on steps the load wrote workflows for, without saving them, and expects
/// the workflow the loader wrote: its name, the person it went to, its person, and waiting at its form (issue
/// 18). This proves the workflow types work in Rock: their Lava finds the step's person and its campus's leader, and their
/// actions assign the task. It launches every open step workflow's step and up to ten completed ones for each trigger.
/// </summary>
static CheckResult StepWorkflowLaunchCheck( RockRelease rock, SqlServer sql, string database, string foreignKey )
{
    // Each type's person attribute is its first attribute, which the type's first action sets from the step.
    var loaded = sql.Query( database, @"
WITH [ranked] AS (
    SELECT [sw].[StepId], [sw].[StepWorkflowTriggerId], [w].[Name], [w].[CompletedDateTime], [ap].[Guid] AS [AssigneeGuid], [pa].[Key] AS [PersonKey], [pv].[Value] AS [PersonValue],
        ROW_NUMBER() OVER ( PARTITION BY [sw].[StepWorkflowTriggerId], CASE WHEN [w].[CompletedDateTime] IS NULL THEN 0 ELSE 1 END ORDER BY [sw].[StepId] ) AS [Rank]
    FROM [StepWorkflow] AS [sw]
    INNER JOIN [Workflow] AS [w] ON [w].[Id] = [sw].[WorkflowId]
    INNER JOIN [WorkflowActivity] AS [act] ON [act].[WorkflowId] = [w].[Id]
    LEFT JOIN [PersonAlias] AS [aa] ON [aa].[Id] = [act].[AssignedPersonAliasId]
    LEFT JOIN [Person] AS [ap] ON [ap].[Id] = [aa].[PersonId]
    INNER JOIN [Attribute] AS [pa] ON [pa].[EntityTypeQualifierColumn] = 'WorkflowTypeId' AND [pa].[EntityTypeQualifierValue] = CAST( [w].[WorkflowTypeId] AS NVARCHAR( 50 ) ) AND [pa].[Order] = 0
    LEFT JOIN [AttributeValue] AS [pv] ON [pv].[AttributeId] = [pa].[Id] AND [pv].[EntityId] = [w].[Id]
    WHERE [sw].[ForeignKey] = @ForeignKey )
SELECT * FROM [ranked] WHERE [CompletedDateTime] IS NULL OR [Rank] <= 10
ORDER BY [StepWorkflowTriggerId], [StepId]", new SqlParameter( "@ForeignKey", foreignKey ) );
    if ( loaded.Count == 0 )
    {
        return CheckResult.From( "step-workflow-launch", "This load wrote no step workflows", new List<string>() );
    }

    var launched = RockRunner.Run( rock, sql, database, "launch-step-workflows", loaded.Select( r => $"{r["StepId"]}\t{r["StepWorkflowTriggerId"]}\t{r["PersonKey"]}" ) )
        .Split( '\n', StringSplitOptions.RemoveEmptyEntries )
        .Select( l => l.TrimEnd( '\r' ).Split( '\t' ) )
        .Where( p => p.Length >= 6 )
        .GroupBy( p => p[0] )
        .ToDictionary( g => g.Key, g => g.First() );

    var problems = new List<string>();
    foreach ( var row in loaded )
    {
        var stepId = row["StepId"].ToString();
        if ( !launched.TryGetValue( stepId, out var launch ) )
        {
            problems.Add( $"Rock launched nothing for step {stepId}." );
            continue;
        }

        var differences = new List<string>();
        if ( launch[1] != ( string ) row["Name"] )
        {
            differences.Add( $"Rock names it \"{launch[1]}\", the load \"{row["Name"]}\"" );
        }

        if ( !Guid.TryParse( launch[2], out var assignee ) || assignee != row["AssigneeGuid"] as Guid? )
        {
            differences.Add( $"Rock assigns it to {( launch[2].Length == 0 ? "nobody" : launch[2] )}, the load to {row["AssigneeGuid"]}" );
        }

        if ( !string.Equals( launch[3], row["PersonValue"] as string, StringComparison.OrdinalIgnoreCase ) )
        {
            differences.Add( $"Rock sets {row["PersonKey"]} to {launch[3]}, the load to {row["PersonValue"]}" );
        }

        if ( launch[4] != "True" )
        {
            differences.Add( "Rock's workflow does not wait at its form" );
        }

        if ( launch[5].Trim().Length > 0 )
        {
            differences.Add( $"Rock reported: {launch[5].Trim()}" );
        }

        if ( differences.Count > 0 )
        {
            problems.Add( $"Step {stepId}: {string.Join( "; ", differences )}." );
        }
    }

    return CheckResult.From( "step-workflow-launch", problems.Count == 0
        ? $"Rock's own launch gave the loaded workflow for all {loaded.Count} steps tried ({loaded.Count( r => r["CompletedDateTime"] == null )} with open workflows)"
        : "Rock's own launch gives a different workflow than the load wrote", problems );
}

/// <summary>
/// Runs Rock's own Process Workflows job, as Rock's scheduler runs it every ten minutes, and expects it to leave every
/// workflow the load wrote as it is (issue 18). Rock processes each open workflow again, so its processed times move, but
/// an open step workflow must still wait at its form for the same person, and a completed one must not change at all. Any
/// error or exception the job reports fails. Rock Cleanup's handling of the same rows is the rock-cleanup check's.
/// </summary>
static CheckResult WorkflowsCheck( RockRelease rock, SqlServer sql, string database, string foreignKey )
{
    var stepWorkflows = Convert.ToInt32( sql.Scalar( database, "SELECT COUNT(*) FROM [StepWorkflow] WHERE [ForeignKey] = @ForeignKey", new SqlParameter( "@ForeignKey", foreignKey ) ) );
    if ( stepWorkflows == 0 )
    {
        return CheckResult.From( "process-workflows", "This load wrote no step workflows", new List<string>() );
    }

    // Each workflow's state, without the processed and modified times Rock moves on an open workflow. A completed one keeps them.
    Dictionary<int, string> Workflows() => sql.Query( database, @"
SELECT [w].[Id], CONCAT( [w].[Status], '|', [w].[Name], '|', CONVERT( NVARCHAR( 30 ), [w].[CompletedDateTime], 126 ), '|',
    CASE WHEN [w].[CompletedDateTime] IS NULL THEN '' ELSE CONCAT( CONVERT( NVARCHAR( 30 ), [w].[LastProcessedDateTime], 126 ), '|', CONVERT( NVARCHAR( 30 ), [w].[ModifiedDateTime], 126 ) ) END, '|',
    ( SELECT STRING_AGG( CONCAT( [act].[Id], ':', [act].[AssignedPersonAliasId], ':', CONVERT( NVARCHAR( 30 ), [act].[CompletedDateTime], 126 ) ), ',' ) FROM [WorkflowActivity] AS [act] WHERE [act].[WorkflowId] = [w].[Id] ), '|',
    ( SELECT STRING_AGG( CONCAT( [ac].[ActionTypeId], ':', CONVERT( NVARCHAR( 30 ), [ac].[CompletedDateTime], 126 ), ':', [ac].[FormAction] ), ',' ) WITHIN GROUP ( ORDER BY [ac].[ActionTypeId] )
        FROM [WorkflowAction] AS [ac] INNER JOIN [WorkflowActivity] AS [act] ON [act].[Id] = [ac].[ActivityId] WHERE [act].[WorkflowId] = [w].[Id] ), '|',
    ( SELECT COUNT(*) FROM [StepWorkflow] AS [sw] WHERE [sw].[WorkflowId] = [w].[Id] ) ) AS [State]
FROM [Workflow] AS [w]
WHERE [w].[ForeignKey] = @ForeignKey", new SqlParameter( "@ForeignKey", foreignKey ) )
        .ToDictionary( r => ( int ) r["Id"], r => ( string ) r["State"] );

    var before = Workflows();
    var open = Convert.ToInt32( sql.Scalar( database, "SELECT COUNT(*) FROM [Workflow] WHERE [ForeignKey] = @ForeignKey AND [CompletedDateTime] IS NULL", new SqlParameter( "@ForeignKey", foreignKey ) ) );
    var result = RockRunner.Run( rock, sql, database, "process-workflows" ).Trim();
    var after = Workflows();

    var problems = new List<string>();
    var changed = before.Keys.Union( after.Keys ).Where( id => before.GetValueOrDefault( id ) != after.GetValueOrDefault( id ) ).ToList();
    foreach ( var id in changed.Take( 5 ) )
    {
        problems.Add( $"Rock's Process Workflows job changed workflow {id}: {before.GetValueOrDefault( id ) ?? "none"} became {after.GetValueOrDefault( id ) ?? "none"}" );
    }

    if ( changed.Count > 5 )
    {
        problems.Add( $"... {changed.Count} workflows changed in all." );
    }

    var firstLine = result.Split( '\n' )[0].Trim();
    if ( firstLine.Contains( "error", StringComparison.OrdinalIgnoreCase ) || firstLine.Contains( "exception", StringComparison.OrdinalIgnoreCase ) )
    {
        problems.Add( $"Rock's Process Workflows job reported: {result.Replace( '\n', ' ' )}" );
    }

    return CheckResult.From( "process-workflows", problems.Count == 0
        ? $"Rock's Process Workflows job left all {before.Count} workflows as they were, with {open} still open, {stepWorkflows} of them from steps ({firstLine})"
        : "Rock's Process Workflows job would change the workflows", problems );
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
