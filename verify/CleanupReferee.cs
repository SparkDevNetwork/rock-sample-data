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

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RockSampleData.Verify
{
    /// <summary>
    /// Rock Cleanup as a referee. Rock's nightly Rock Cleanup job repairs what Rock's save hooks would have set:
    /// missing aliases, ages, salutations, and more. The loader skips those hooks, so after a load we run Rock's
    /// own job and compare the database before and after. Whatever Rock "fixes" in our data, the loader missed.
    ///
    /// Rock's job also tidies things in any fresh database. So it runs once on a plain copy of the trusted base
    /// too. Both databases are copies of that base, so a base row has the same Id in both: a change Rock makes to
    /// exactly the same base rows on the plain copy is background, not a finding. A change to any row the load
    /// added (an Id past the base's largest) is always a finding.
    /// </summary>
    internal sealed class CleanupReferee
    {
        /// <summary>
        /// Columns that change along with any real change. Reporting them would only repeat the real one.
        /// </summary>
        private static readonly HashSet<string> Followers = new HashSet<string>( StringComparer.Ordinal ) { "ModifiedDateTime", "ModifiedByPersonAliasId" };

        /// <summary>
        /// Rock Cleanup tasks that cannot run outside a web server, because they need services only Rock's web start-up
        /// registers. An error from any other task is a finding.
        /// </summary>
        private static readonly string[] WebServerTasks = { "orphaned entity metadata", "connected services manifest" };

        private readonly SqlServer _sql;
        private readonly RockRelease _rock;
        private readonly string _acceptedPath;
        private string _baseDatabase;
        private Measurement _baseline;

        public CleanupReferee( SqlServer sql, RockRelease rock, string acceptedPath )
        {
            _sql = sql;
            _rock = rock;
            _acceptedPath = acceptedPath;
        }

        /// <summary>
        /// One kind of change Rock Cleanup made: rows changed in "Table.Column", or rows added to or removed from a table.
        /// </summary>
        internal sealed class Change
        {
            public long Rows { get; set; }

            /// <summary>
            /// Changed rows the load added: their Id is past the base's largest Id.
            /// </summary>
            public long LoadedRows { get; set; }

            /// <summary>
            /// Ids of changed rows that came from the base.
            /// </summary>
            public HashSet<long> BaseIds { get; } = new HashSet<long>();
        }

        internal sealed class Measurement
        {
            public Dictionary<string, Change> Changes { get; } = new Dictionary<string, Change>( StringComparer.Ordinal );

            public List<string> TaskErrors { get; } = new List<string>();

            public List<string> TaskCounts { get; } = new List<string>();
        }

        /// <summary>
        /// Runs Rock Cleanup on a loaded database and reports every change it made that it does not also make to the
        /// same rows of a fresh base. Changes listed in validation/cleanup-accepted.json are reported but pass.
        /// </summary>
        public CheckResult Referee( string database, string baseDatabase )
        {
            _baseDatabase = baseDatabase;
            if ( _baseline == null )
            {
                var baselineDatabase = $"{SqlServer.DatabasePrefix}-cleanup-baseline";
                _sql.Copy( baseDatabase, baselineDatabase );
                try
                {
                    _baseline = Measure( baselineDatabase );
                }
                finally
                {
                    _sql.Drop( baselineDatabase );
                }
            }

            var measured = Measure( database );
            var accepted = ReadAccepted();
            var problems = new List<string>();
            var notes = new List<string>();
            foreach ( var (key, change) in measured.Changes.OrderBy( c => c.Key, StringComparer.Ordinal ) )
            {
                var baseline = _baseline.Changes.GetValueOrDefault( key );
                var isBackground = IsBackground( key, change, baseline );
                var detail = new List<string>();
                if ( change.LoadedRows > 0 )
                {
                    detail.Add( $"{change.LoadedRows:N0} of them loaded" );
                }

                if ( baseline != null && !isBackground )
                {
                    detail.Add( $"{baseline.Rows:N0} in a fresh database" );
                }

                var text = $"{key}: {change.Rows:N0} rows{( detail.Count > 0 ? $" ({string.Join( ", ", detail )})" : string.Empty )}";
                if ( isBackground )
                {
                    notes.Add( $"Background (Rock does this to the same rows of a fresh database): {text}" );
                }
                else if ( accepted.TryGetValue( key, out var reason ) )
                {
                    notes.Add( $"Accepted: {text}. {reason}" );
                }
                else
                {
                    problems.Add( $"Rock Cleanup changed {text}." );
                }
            }

            var baselineErrors = _baseline.TaskErrors.ToList();
            foreach ( var error in measured.TaskErrors )
            {
                var isWebServerTask = WebServerTasks.Any( t => error.StartsWith( $"{t}:", StringComparison.OrdinalIgnoreCase ) );
                if ( isWebServerTask && baselineErrors.Remove( error ) )
                {
                    notes.Add( $"Rock Cleanup task needs a web server, so it cannot run here: {error}" );
                }
                else
                {
                    problems.Add( $"Rock Cleanup task error: {error}" );
                }
            }

            notes.AddRange( measured.TaskCounts.Select( c => $"Rock Cleanup reported: {c}" ) );
            var summary = problems.Count == 0
                ? "Rock's own Rock Cleanup job found nothing to fix in the sample data"
                : $"Rock's own Rock Cleanup job fixed {problems.Count} things in the sample data that the loader should have set";
            return CheckResult.From( "rock-cleanup", summary, problems, notes );
        }

        /// <summary>
        /// A change is background when Rock made it to exactly the same base rows of a fresh database and to no loaded row.
        /// Added rows and tables without an Id cannot be matched row by row, so for them it is enough that Rock did no more.
        /// </summary>
        private static bool IsBackground( string key, Change change, Change baseline )
        {
            if ( baseline == null || change.LoadedRows > 0 )
            {
                return false;
            }

            var isMatchedById = !key.EndsWith( ": rows added" ) && !key.EndsWith( "(no Id column)" );
            return isMatchedById ? change.BaseIds.IsSubsetOf( baseline.BaseIds ) : change.Rows <= baseline.Rows;
        }

        /// <summary>
        /// Snapshots the database, runs Rock Cleanup on it, and compares every table with the snapshot.
        /// </summary>
        private Measurement Measure( string database )
        {
            var snapshot = $"{database}-before-cleanup";
            _sql.Snapshot( database, snapshot );
            try
            {
                var measurement = new Measurement();
                RunCleanup( database, measurement );
                Compare( database, snapshot, measurement );
                return measurement;
            }
            finally
            {
                _sql.Drop( snapshot );
            }
        }

        private void RunCleanup( string database, Measurement measurement )
        {
            var output = RockRunner.Run( _rock, _sql, database, "cleanup" );
            foreach ( var line in output.Split( '\n' ).Select( l => Regex.Replace( l, "<[^>]+>", string.Empty ).Trim() ).Where( l => l.Length > 0 ) )
            {
                if ( line.StartsWith( "Task error: " ) )
                {
                    measurement.TaskErrors.Add( line.Substring( "Task error: ".Length ) );
                }
                else if ( Regex.IsMatch( line, @"^[1-9][\d,]* " ) )
                {
                    measurement.TaskCounts.Add( Regex.Replace( line, @" \([\d,]+ms\)$", string.Empty ) );
                }
            }
        }

        /// <summary>
        /// Compares every dbo table that has rows with its snapshot. Tables with an Id are compared row by row and column
        /// by column; others as whole rows. Every value is compared as bytes, so a change in letter case counts.
        /// </summary>
        private void Compare( string database, string snapshot, Measurement measurement )
        {
            var tables = _sql.Query( database, @"
SELECT [t].[name] AS [Table], [c].[name] AS [Column], TYPE_NAME( [c].[user_type_id] ) AS [Type], [c].[is_computed] AS [IsComputed]
FROM sys.tables AS [t]
INNER JOIN sys.columns AS [c] ON [c].[object_id] = [t].[object_id]
WHERE SCHEMA_NAME( [t].[schema_id] ) = 'dbo' AND [t].[is_ms_shipped] = 0
ORDER BY [t].[name], [c].[column_id]" )
                .GroupBy( r => ( string ) r["Table"], StringComparer.Ordinal );

            foreach ( var table in tables )
            {
                var columns = table.Where( c => !( bool ) c["IsComputed"] ).Select( c => (Name: ( string ) c["Column"], Type: ( string ) c["Type"]) ).ToList();
                var live = $"[{database}].[dbo].[{table.Key}]";
                var before = $"[{snapshot}].[dbo].[{table.Key}]";
                var isEmpty = Convert.ToInt64( _sql.Scalar( "master", $"SELECT ( SELECT COUNT_BIG(*) FROM {live} ) + ( SELECT COUNT_BIG(*) FROM {before} )" ) ) == 0;
                if ( isEmpty )
                {
                    continue;
                }

                if ( columns.Any( c => c.Name == "Id" ) )
                {
                    CompareRows( table.Key, live, before, columns, measurement );
                }
                else
                {
                    CompareWholeRows( table.Key, live, before, columns, measurement );
                }
            }
        }

        private void CompareWholeRows( string table, string live, string before, List<(string Name, string Type)> columns, Measurement measurement )
        {
            var list = string.Join( ", ", columns.Select( ( c, i ) => $"{Comparable( "t", c )} AS [c{i}]" ) );
            var differing = Convert.ToInt64( _sql.Scalar( "master", $@"
SELECT ( SELECT COUNT_BIG(*) FROM ( SELECT {list} FROM {live} AS [t] EXCEPT SELECT {list} FROM {before} AS [t] ) AS [x] )
    + ( SELECT COUNT_BIG(*) FROM ( SELECT {list} FROM {before} AS [t] EXCEPT SELECT {list} FROM {live} AS [t] ) AS [x] )" ) );
            if ( differing > 0 )
            {
                measurement.Changes[$"{table}: rows changed (no Id column)"] = new Change { Rows = differing };
            }
        }

        private void CompareRows( string table, string live, string before, List<(string Name, string Type)> columns, Measurement measurement )
        {
            var compared = columns.Where( c => c.Name != "Id" && !Followers.Contains( c.Name ) ).ToList();
            var baseMax = $"( SELECT ISNULL( MAX( [Id] ), 0 ) FROM [{_baseDatabase}].[dbo].[{table}] )";

            // Rows added and removed. Rock Cleanup adds rows with new Ids, so added rows are counted; removed base rows keep their Ids.
            var added = Convert.ToInt64( _sql.Scalar( "master", $"SELECT COUNT_BIG(*) FROM {live} AS [l] WHERE NOT EXISTS ( SELECT 1 FROM {before} AS [s] WHERE [s].[Id] = [l].[Id] )" ) );
            if ( added > 0 )
            {
                measurement.Changes[$"{table}: rows added"] = new Change { Rows = added };
            }

            var removed = _sql.Query( "master", $"SELECT [s].[Id] FROM {before} AS [s] WHERE NOT EXISTS ( SELECT 1 FROM {live} AS [l] WHERE [l].[Id] = [s].[Id] )" )
                .Select( r => Convert.ToInt64( r["Id"] ) ).ToList();
            if ( removed.Count > 0 )
            {
                var max = Convert.ToInt64( _sql.Scalar( "master", $"SELECT {baseMax}" ) );
                var change = new Change { Rows = removed.Count, LoadedRows = removed.Count( id => id > max ) };
                change.BaseIds.UnionWith( removed.Where( id => id <= max ) );
                measurement.Changes[$"{table}: rows removed"] = change;
            }

            // Changed columns. SQL Server cannot SUM over a subquery, so each row's differences are worked out in a derived table.
            var perRow = new StringBuilder( $"[l].[Id], CASE WHEN [l].[Id] > {baseMax} THEN 1 ELSE 0 END AS [__loaded]" );
            var any = new List<string>();
            foreach ( var (column, i) in compared.Select( ( c, i ) => (c, i) ) )
            {
                perRow.Append( $",\n    CASE WHEN EXISTS ( SELECT {Comparable( "l", column )} EXCEPT SELECT {Comparable( "s", column )} ) THEN 1 ELSE 0 END AS [d{i}]" );
                any.Add( $"[d{i}] = 1" );
            }

            if ( any.Count == 0 )
            {
                return;
            }

            var changedRows = _sql.Query( "master", $@"
SELECT * FROM ( SELECT {perRow}
    FROM {live} AS [l] INNER JOIN {before} AS [s] ON [s].[Id] = [l].[Id] ) AS [x]
WHERE {string.Join( " OR ", any )}" );
            foreach ( var row in changedRows )
            {
                var id = Convert.ToInt64( row["Id"] );
                var isLoaded = Convert.ToInt32( row["__loaded"] ) == 1;
                foreach ( var (column, i) in compared.Select( ( c, i ) => (c, i) ) )
                {
                    if ( Convert.ToInt32( row[$"d{i}"] ) == 0 )
                    {
                        continue;
                    }

                    var key = $"{table}.{column.Name}";
                    if ( !measurement.Changes.TryGetValue( key, out var change ) )
                    {
                        change = new Change();
                        measurement.Changes[key] = change;
                    }

                    change.Rows++;
                    if ( isLoaded )
                    {
                        change.LoadedRows++;
                    }
                    else
                    {
                        change.BaseIds.Add( id );
                    }
                }
            }
        }

        /// <summary>
        /// A column as bytes, so EXCEPT compares it exactly: Rock's collation would call "Smith" and "SMITH " equal.
        /// Types that cannot become bytes directly pass through text first.
        /// </summary>
        private static string Comparable( string alias, (string Name, string Type) column )
        {
            var name = $"[{alias}].[{column.Name}]";
            return column.Type.ToLowerInvariant() switch
            {
                "xml" or "ntext" => $"CAST( CAST( {name} AS NVARCHAR(MAX) ) AS VARBINARY(MAX) )",
                "text" => $"CAST( CAST( {name} AS VARCHAR(MAX) ) AS VARBINARY(MAX) )",
                "sql_variant" => $"CAST( CAST( {name} AS NVARCHAR(4000) ) AS VARBINARY(MAX) )",
                "geography" or "geometry" or "hierarchyid" or "image" or "nvarchar" or "varchar" or "nchar" or "char" => $"CAST( {name} AS VARBINARY(MAX) )",
                _ => name
            };
        }

        private Dictionary<string, string> ReadAccepted()
        {
            if ( !File.Exists( _acceptedPath ) )
            {
                return new Dictionary<string, string>( StringComparer.Ordinal );
            }

            using var document = JsonDocument.Parse( File.ReadAllText( _acceptedPath ) );
            return document.RootElement.GetProperty( "accepted" ).EnumerateArray()
                .ToDictionary( a => a.GetProperty( "change" ).GetString(), a => a.GetProperty( "reason" ).GetString(), StringComparer.Ordinal );
        }
    }
}
