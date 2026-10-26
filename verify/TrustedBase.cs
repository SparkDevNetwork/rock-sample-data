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
using System.Security.Cryptography;
using System.Text;

namespace RockSampleData.Verify
{
    /// <summary>
    /// The trusted base: an empty Rock database made from one Rock release by Rock's own migrations, and
    /// nothing else. Every other check runs on a copy of it, so a finding is about our data, not a stale
    /// or dirty database.
    /// </summary>
    internal static class TrustedBase
    {
        private const string VersionProperty = "RockSampleVerify.RockVersion";

        /// <summary>
        /// What the base was, recorded in every receipt.
        /// </summary>
        internal sealed class Certificate
        {
            public string Database { get; set; }

            public string RockVersion { get; set; }

            public int Migrations { get; set; }

            public string NewestMigration { get; set; }

            /// <summary>
            /// SHA-256 of every column in the database (table, column, type, length, nullability), in order.
            /// Two bases with the same hash have the same schema.
            /// </summary>
            public string SchemaHash { get; set; }

            public int ExceptionLogRows { get; set; }
        }

        /// <summary>
        /// Checks that a database is a trusted base for this Rock: exactly this Rock's migrations, Rock's start-up steps, one-time
        /// update jobs, and post-install job run, no sample data, and an empty Exception Log.
        /// </summary>
        public static (CheckResult Result, Certificate Certificate) Certify( SqlServer sql, string database, RockRelease rock )
        {
            var problems = new List<string>();
            if ( !sql.Exists( database ) )
            {
                problems.Add( $"{database} does not exist." );
                return (CheckResult.From( "trusted-base", $"No trusted base for Rock {rock.Version}", problems ), null);
            }

            var applied = sql.Query( database, "SELECT [MigrationId] FROM [__MigrationHistory]" ).Select( r => ( string ) r["MigrationId"] ).ToHashSet( StringComparer.Ordinal );
            var missing = rock.MigrationIds.Where( id => !applied.Contains( id ) ).ToList();
            var extra = applied.Where( id => !rock.MigrationIds.Contains( id ) ).OrderBy( id => id, StringComparer.Ordinal ).ToList();
            if ( missing.Count > 0 )
            {
                problems.Add( $"{missing.Count} of Rock's migrations are not applied, for example {string.Join( ", ", missing.Take( 3 ) )}." );
            }

            if ( extra.Count > 0 )
            {
                problems.Add( $"{extra.Count} applied migrations are not in this Rock checkout, for example {string.Join( ", ", extra.Take( 3 ) )}." );
            }

            // Patch releases often share their EF migrations and differ only in hotfixes, so those must match too.
            var hotfixes = sql.Query( database, "SELECT [MigrationNumber] FROM [PluginMigration] WHERE [PluginAssemblyName] = 'Rock'" ).Select( r => Convert.ToInt32( r["MigrationNumber"] ) ).ToHashSet();
            var missingHotfixes = rock.HotfixNumbers.Where( n => !hotfixes.Contains( n ) ).ToList();
            var extraHotfixes = hotfixes.Where( n => !rock.HotfixNumbers.Contains( n ) ).OrderBy( n => n ).ToList();
            if ( missingHotfixes.Count > 0 || extraHotfixes.Count > 0 )
            {
                problems.Add( $"Its hotfix migrations do not match the checkout: {missingHotfixes.Count} missing, {extraHotfixes.Count} extra (for example {string.Join( ", ", missingHotfixes.Concat( extraHotfixes ).Take( 3 ) )})." );
            }

            var stamp = sql.Scalar( database, $"SELECT CAST( [value] AS NVARCHAR(100) ) FROM sys.extended_properties WHERE [class] = 0 AND [name] = '{VersionProperty}'" ) as string;
            if ( stamp != rock.Version )
            {
                problems.Add( $"It was made from Rock {stamp ?? "(unknown)"}, not {rock.Version}." );
            }

            var sampleRows = Convert.ToInt32( sql.Scalar( database, @"
SELECT ( SELECT COUNT(*) FROM [Person] WHERE [ForeignKey] LIKE 'SampleData:%' )
    + ( SELECT COUNT(*) FROM [Group] WHERE [ForeignKey] LIKE 'SampleData:%' )
    + ( SELECT COUNT(*) FROM [FinancialTransaction] WHERE [ForeignKey] LIKE 'SampleData:%' )" ) );
            if ( sampleRows > 0 )
            {
                problems.Add( $"It already holds {sampleRows:N0} sample data rows." );
            }

            if ( sql.Scalar( database, "SELECT OBJECT_ID( 'dbo.RockGetDate' )" ) == null )
            {
                problems.Add( "Rock's start-up steps have not run on it (it has no RockGetDate function)." );
            }
            else if ( Convert.ToInt32( sql.Scalar( database, "SELECT COUNT(*) FROM [AnalyticsSourceDate]" ) ) == 0 )
            {
                problems.Add( "Rock's post-install job has not run on it (AnalyticsSourceDate is empty)." );
            }
            else if ( Convert.ToInt32( sql.Scalar( database, "SELECT COUNT(*) FROM [AnalyticsSourcePostalCode]" ) ) == 0 )
            {
                problems.Add( "Rock's post-install job did not find its census data (AnalyticsSourcePostalCode is empty)." );
            }

            // A new install runs Rock's one-time update jobs on its first evening, and each deletes itself (decision D-verify-housekeeping-1).
            var waitingJobs = Convert.ToInt32( sql.Scalar( database, "SELECT COUNT(*) FROM [ServiceJob] WHERE [Class] LIKE 'Rock.Jobs.Post%' AND [Class] <> 'Rock.Jobs.PostInstallDataMigrations'" ) );
            if ( waitingJobs > 0 )
            {
                problems.Add( $"Rock's one-time update jobs have not run on it ({waitingJobs:N0} are waiting), so it still has WebForms blocks that a Rock that has run a night has swapped for Obsidian ones." );
            }

            var exceptions = Convert.ToInt32( sql.Scalar( database, "SELECT COUNT(*) FROM [ExceptionLog]" ) );
            if ( exceptions > 0 )
            {
                problems.Add( $"Its Exception Log has {exceptions:N0} rows. A base made only by migrations has none." );
            }

            var certificate = new Certificate
            {
                Database = database,
                RockVersion = rock.Version,
                Migrations = applied.Count,
                NewestMigration = applied.Max( StringComparer.Ordinal ),
                SchemaHash = SchemaHash( sql, database ),
                ExceptionLogRows = exceptions
            };

            var summary = problems.Count == 0
                ? $"{database} has exactly Rock {rock.Version}'s {rock.MigrationIds.Count} migrations and {rock.HotfixNumbers.Count} hotfixes, no sample data, and no exceptions"
                : $"{database} is not a trusted base for Rock {rock.Version}";
            return (CheckResult.From( "trusted-base", summary, problems, new List<string> { $"Schema hash {certificate.SchemaHash}" } ), certificate);
        }

        /// <summary>
        /// Makes the base from nothing: an empty database, every Rock migration applied by EF's own tool (the way Rock's
        /// developers run Update-Database), then Rock's own start-up steps and first-night jobs, and a stamp of the Rock version.
        /// About 10 minutes.
        /// </summary>
        public static void Create( SqlServer sql, string database, RockRelease rock )
        {
            Console.WriteLine( $"Creating the trusted base {database} from Rock {rock.Version} ({rock.MigrationIds.Count} migrations). This takes about 10 minutes." );
            sql.Drop( database );
            sql.Execute( "master", $"CREATE DATABASE [{database}];" );

            var work = Directory.CreateTempSubdirectory( "rock-sample-data-verify-" );
            try
            {
                var config = Path.Combine( work.FullName, "web.config" );
                rock.WriteWebConfig( sql.RockConnectionString( database ), config );

                var start = new ProcessStartInfo( rock.FindEf6() )
                {
                    WorkingDirectory = rock.BinPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach ( var argument in new[]
                {
                    "database", "update",
                    "--assembly", Path.Combine( rock.BinPath, "Rock.Migrations.dll" ),
                    "--migrations-config", "Rock.Migrations.Configuration",
                    "--config", config,
                    "--connection-string-name", "RockContext",
                    "--project-dir", rock.BinPath,
                    "--data-dir", Path.Combine( rock.CheckoutPath, "RockWeb", "App_Data" )
                } )
                {
                    start.ArgumentList.Add( argument );
                }

                using var process = Process.Start( start );
                var applied = 0;
                process.OutputDataReceived += ( _, e ) =>
                {
                    if ( e.Data?.StartsWith( "Applying explicit migration" ) == true && ++applied % 100 == 0 )
                    {
                        Console.WriteLine( $"  {applied} of {rock.MigrationIds.Count} migrations" );
                    }
                };
                var errors = new StringBuilder();
                process.ErrorDataReceived += ( _, e ) => errors.AppendLine( e.Data );
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                if ( process.ExitCode != 0 )
                {
                    throw new InvalidOperationException( $"Rock's migrations failed on {database}: {errors.ToString().Trim()}" );
                }
            }
            finally
            {
                work.Delete( true );
            }

            // A real install has also started once: hotfix migrations, the RockGetDate function, and the attribute value views.
            // It has also run its one-time post-install job, the first night.
            Console.WriteLine( "  Running Rock's start-up steps" );
            Console.WriteLine( $"  {RockRunner.Run( rock, sql, database, "startup" ).Trim()}" );

            // A Rock database stores no version number, so the base carries one: the Rock it was made from.
            sql.Execute( database, $"EXEC sys.sp_addextendedproperty @name = N'{VersionProperty}', @value = N'{rock.Version.Replace( "'", "''" )}';" );
        }

        private static string SchemaHash( SqlServer sql, string database )
        {
            var lines = sql.Query( database, @"
SELECT [TABLE_NAME], [COLUMN_NAME], [DATA_TYPE], [CHARACTER_MAXIMUM_LENGTH], [NUMERIC_PRECISION], [NUMERIC_SCALE], [IS_NULLABLE]
FROM [INFORMATION_SCHEMA].[COLUMNS]
WHERE [TABLE_SCHEMA] = 'dbo'
ORDER BY [TABLE_NAME], [COLUMN_NAME]" )
                .Select( r => string.Join( "|", r.Values.Select( v => v?.ToString() ?? string.Empty ) ) );
            var bytes = SHA256.HashData( Encoding.UTF8.GetBytes( string.Join( "\n", lines ) ) );
            return Convert.ToHexString( bytes ).ToLowerInvariant();
        }
    }
}
