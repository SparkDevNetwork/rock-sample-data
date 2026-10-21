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
using System.Text.Json.Serialization;

using RockSampleData.Loader.Database;

namespace RockSampleData.Verify
{
    /// <summary>
    /// The contract between the loader and Rock (validation/rock-contract.json): every Rock table and column the
    /// loader writes, the full column list of those tables when the contract was recorded, the Rock-provided
    /// Guids it depends on, and the Rock procedures it runs. It is recorded from real loads, never written by hand.
    ///
    /// Because the loader bulk-copies around Rock's business logic, two things can go wrong without any error:
    /// Rock adds a column the loader should fill, or the loader starts writing something nobody reviewed.
    /// The contract turns both into a failing check that names the column.
    /// </summary>
    internal sealed class RockContract
    {
        /// <summary>
        /// Columns the loader must never write. Content standard 8: photo ethnicity exists only to make matching
        /// family photos and never goes into Rock, so neither does race or ethnicity.
        /// </summary>
        private static readonly string[] NeverWritten = { "Person.RaceValueId", "Person.EthnicityValueId" };

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        [JsonPropertyName( "$comment" )]
        public string Comment { get; set; }

        /// <summary>
        /// The Rock the contract was recorded against, for reading its diff later.
        /// </summary>
        public string RecordedWith { get; set; }

        public SortedDictionary<string, ContractTable> Tables { get; set; } = new SortedDictionary<string, ContractTable>( StringComparer.Ordinal );

        public List<string> Deletes { get; set; } = new List<string>();

        /// <summary>
        /// Rock-provided Guids the loader looks up or names, by the table that holds them.
        /// </summary>
        public SortedDictionary<string, List<string>> Guids { get; set; } = new SortedDictionary<string, List<string>>( StringComparer.Ordinal );

        public List<string> Procedures { get; set; } = new List<string>();

        internal sealed class ContractTable
        {
            /// <summary>
            /// The columns the loader writes.
            /// </summary>
            public List<string> Written { get; set; } = new List<string>();

            /// <summary>
            /// Every column Rock's table had when the contract was recorded, with its type. A Rock column not
            /// listed here is new, and someone must decide whether the loader fills it.
            /// </summary>
            public SortedDictionary<string, string> RockColumns { get; set; } = new SortedDictionary<string, string>( StringComparer.Ordinal );
        }

        public static RockContract Load( string path )
        {
            return File.Exists( path ) ? JsonSerializer.Deserialize<RockContract>( File.ReadAllText( path ), Json ) : null;
        }

        public void Save( string path )
        {
            var text = JsonSerializer.Serialize( this, Json ).Replace( "\r\n", "\n" ) + "\n";

            // A contract that differs only in line endings is unchanged. Rewriting it would mark the file modified,
            // and the receipt's unchanged-during-run check would fail for nothing. A changed contract keeps the line
            // endings the checkout already uses.
            var existing = File.Exists( path ) ? File.ReadAllText( path ) : null;
            if ( existing?.Replace( "\r\n", "\n" ) == text )
            {
                return;
            }

            File.WriteAllText( path, existing?.Contains( "\r\n" ) == true ? text.Replace( "\n", "\r\n" ) : text );
        }

        /// <summary>
        /// Builds a contract from what the loads recorded, reading column types and Guid owners from the trusted base.
        /// </summary>
        public static RockContract Record( WriteRecorder recording, SqlServer sql, string baseDatabase, RockRelease rock )
        {
            var columns = RockColumns( sql, baseDatabase );
            var contract = new RockContract
            {
                Comment = "Recorded by the verify tool (verify/README.md) from real loads of every dataset. Do not edit by hand: change the loader, then run the verify tool with --record-contract and review the diff.",
                RecordedWith = $"Rock {rock.Version} ({rock.NewestMigration})",
                Deletes = recording.Deletes.ToList(),
                Procedures = recording.Procedures.ToList(),
                Guids = RockGuids( sql, baseDatabase, recording.Guids )
            };

            foreach ( var (table, written) in recording.Columns )
            {
                contract.Tables[table] = new ContractTable
                {
                    Written = written.ToList(),
                    RockColumns = new SortedDictionary<string, string>( columns.GetValueOrDefault( table ) ?? new Dictionary<string, string>(), StringComparer.Ordinal )
                };
            }

            return contract;
        }

        /// <summary>
        /// Checks the contract against a Rock database: every written column still exists with the same type, Rock has
        /// added no column to a table the loader writes, every Guid and procedure exists, and nothing forbidden is written.
        /// </summary>
        public CheckResult CheckSchema( SqlServer sql, string database, string rockVersion )
        {
            var problems = new List<string>();
            var notes = new List<string>();
            var actual = RockColumns( sql, database );

            foreach ( var (table, contracted) in Tables )
            {
                if ( !actual.TryGetValue( table, out var rockColumns ) )
                {
                    problems.Add( $"Rock has no {table} table, which the loader writes." );
                    continue;
                }

                foreach ( var (column, type) in contracted.RockColumns )
                {
                    var isWritten = contracted.Written.Contains( column );
                    if ( !rockColumns.TryGetValue( column, out var actualType ) )
                    {
                        ( isWritten ? problems : notes ).Add( $"Rock removed {table}.{column}{( isWritten ? ", which the loader writes" : string.Empty )}." );
                    }
                    else if ( actualType != type )
                    {
                        ( isWritten ? problems : notes ).Add( $"Rock changed {table}.{column} from {type} to {actualType}{( isWritten ? ", and the loader writes it" : string.Empty )}." );
                    }
                }

                foreach ( var (column, type) in rockColumns.Where( c => !contracted.RockColumns.ContainsKey( c.Key ) ) )
                {
                    problems.Add( $"Rock added {table}.{column} ({type}). Decide whether the loader must fill it, then re-record the contract." );
                }
            }

            foreach ( var forbidden in NeverWritten )
            {
                var parts = forbidden.Split( '.' );
                if ( Tables.TryGetValue( parts[0], out var table ) && table.Written.Contains( parts[1] ) )
                {
                    problems.Add( $"The loader writes {forbidden}, which content standard 8 forbids." );
                }
            }

            var found = RockGuids( sql, database, Guids.Values.SelectMany( g => g ).Select( Guid.Parse ) );
            foreach ( var (table, guids) in Guids )
            {
                var present = found.GetValueOrDefault( table ) ?? new List<string>();
                foreach ( var guid in guids.Except( present, StringComparer.OrdinalIgnoreCase ) )
                {
                    problems.Add( $"Rock has no {table} with Guid {guid}, which the loader depends on." );
                }
            }

            var procedures = sql.Query( database, "SELECT [name] FROM sys.procedures" ).Select( r => ( string ) r["name"] ).ToHashSet( StringComparer.OrdinalIgnoreCase );
            foreach ( var procedure in Procedures.Where( p => !procedures.Contains( p ) ) )
            {
                problems.Add( $"Rock has no procedure {procedure}, which the loader runs." );
            }

            var columnCount = Tables.Values.Sum( t => t.Written.Count );
            var guidCount = Guids.Values.Sum( g => g.Count );
            var summary = $"{columnCount} columns in {Tables.Count} tables, {guidCount} Rock Guids, and {Procedures.Count} procedures checked against Rock {rockVersion}";
            return CheckResult.From( "contract-schema", summary, problems, notes );
        }

        /// <summary>
        /// Checks that the contract file still describes the loader: what these loads recorded must be in the contract,
        /// and when every dataset was loaded, the contract must hold nothing else.
        /// </summary>
        public CheckResult CheckCurrent( RockContract recorded, List<string> unparsed, bool isEveryDataset )
        {
            var problems = unparsed.Select( sql => $"The recorder could not read this write, so it is not in the contract: {OneLine( sql )}" ).ToList();

            foreach ( var (table, columns) in recorded.Tables )
            {
                var contracted = Tables.GetValueOrDefault( table )?.Written ?? new List<string>();
                problems.AddRange( columns.Written.Except( contracted ).Select( c => $"The loader now writes {table}.{c}, which the contract does not list." ) );
            }

            problems.AddRange( recorded.Deletes.Except( Deletes ).Select( t => $"The loader now deletes from {t}, which the contract does not list." ) );
            problems.AddRange( recorded.Procedures.Except( Procedures ).Select( p => $"The loader now runs {p}, which the contract does not list." ) );
            foreach ( var (table, guids) in recorded.Guids )
            {
                var contracted = Guids.GetValueOrDefault( table ) ?? new List<string>();
                problems.AddRange( guids.Except( contracted, StringComparer.OrdinalIgnoreCase ).Select( g => $"The loader now depends on {table} {g}, which the contract does not list." ) );
            }

            if ( isEveryDataset )
            {
                foreach ( var (table, columns) in Tables )
                {
                    var written = recorded.Tables.GetValueOrDefault( table )?.Written ?? new List<string>();
                    problems.AddRange( columns.Written.Except( written ).Select( c => $"The contract lists {table}.{c}, but no load wrote it." ) );
                }

                problems.AddRange( Deletes.Except( recorded.Deletes ).Select( t => $"The contract lists deletes from {t}, but no load deleted from it." ) );
                problems.AddRange( Procedures.Except( recorded.Procedures ).Select( p => $"The contract lists {p}, but no load ran it." ) );
                foreach ( var (table, guids) in Guids )
                {
                    var used = recorded.Guids.GetValueOrDefault( table ) ?? new List<string>();
                    problems.AddRange( guids.Except( used, StringComparer.OrdinalIgnoreCase ).Select( g => $"The contract lists {table} {g}, but no load used it." ) );
                }
            }

            if ( problems.Count > 0 )
            {
                problems.Add( "Review the change, then run the verify tool with --record-contract and commit validation/rock-contract.json." );
            }

            var scope = isEveryDataset ? "every dataset" : "the datasets loaded";
            return CheckResult.From( "contract-current", $"What the loader wrote for {scope} matches validation/rock-contract.json", problems );
        }

        /// <summary>
        /// Every dbo table's columns and types, as "nvarchar(100) null".
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> RockColumns( SqlServer sql, string database )
        {
            return sql.Query( database, @"
SELECT [TABLE_NAME], [COLUMN_NAME], [DATA_TYPE], [CHARACTER_MAXIMUM_LENGTH], [NUMERIC_PRECISION], [NUMERIC_SCALE], [IS_NULLABLE]
FROM [INFORMATION_SCHEMA].[COLUMNS]
WHERE [TABLE_SCHEMA] = 'dbo'" )
                .GroupBy( r => ( string ) r["TABLE_NAME"], StringComparer.Ordinal )
                .ToDictionary( g => g.Key, g => g.ToDictionary( r => ( string ) r["COLUMN_NAME"], TypeText, StringComparer.Ordinal ), StringComparer.Ordinal );
        }

        private static string TypeText( Dictionary<string, object> column )
        {
            var type = ( string ) column["DATA_TYPE"];
            var length = column["CHARACTER_MAXIMUM_LENGTH"] is int l ? ( l == -1 ? "(max)" : $"({l})" ) : string.Empty;
            var scale = type == "decimal" || type == "numeric" ? $"({column["NUMERIC_PRECISION"]},{column["NUMERIC_SCALE"]})" : string.Empty;
            var nullable = ( string ) column["IS_NULLABLE"] == "YES" ? " null" : " not null";
            return $"{type}{length}{scale}{nullable}";
        }

        /// <summary>
        /// Which of the given Guids a database holds, by table.
        /// </summary>
        private static SortedDictionary<string, List<string>> RockGuids( SqlServer sql, string database, IEnumerable<Guid> guids )
        {
            var result = new SortedDictionary<string, List<string>>( StringComparer.Ordinal );
            var list = guids.Distinct().ToList();
            if ( list.Count == 0 )
            {
                return result;
            }

            var batch = new StringBuilder( "SET NOCOUNT ON; CREATE TABLE #Wanted ( [Guid] UNIQUEIDENTIFIER PRIMARY KEY );\n" );
            foreach ( var chunk in list.Chunk( 900 ) )
            {
                batch.AppendLine( $"INSERT INTO #Wanted ( [Guid] ) VALUES {string.Join( ", ", chunk.Select( g => $"('{g}')" ) )};" );
            }

            batch.Append( @"
DECLARE @Sql NVARCHAR(MAX) = N'';
SELECT @Sql = @Sql + CASE WHEN @Sql = N'' THEN N'' ELSE N' UNION ALL ' END
    + N'SELECT ''' + [t].[name] + N''' AS [Table], [x].[Guid] FROM [dbo].' + QUOTENAME( [t].[name] ) + N' AS [x] INNER JOIN #Wanted AS [w] ON [w].[Guid] = [x].[Guid]'
FROM sys.tables AS [t]
INNER JOIN sys.columns AS [c] ON [c].[object_id] = [t].[object_id] AND [c].[name] = 'Guid' AND [c].[system_type_id] = 36
WHERE [t].[is_ms_shipped] = 0 AND SCHEMA_NAME( [t].[schema_id] ) = 'dbo';
EXEC sp_executesql @Sql;" );

            foreach ( var row in sql.Query( database, batch.ToString() ) )
            {
                var table = ( string ) row["Table"];
                if ( !result.TryGetValue( table, out var tableGuids ) )
                {
                    tableGuids = new List<string>();
                    result[table] = tableGuids;
                }

                tableGuids.Add( ( ( Guid ) row["Guid"] ).ToString().ToUpperInvariant() );
            }

            foreach ( var tableGuids in result.Values )
            {
                tableGuids.Sort( StringComparer.Ordinal );
            }

            return result;
        }

        private static string OneLine( string sql )
        {
            var text = string.Join( " ", sql.Split( new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries ) );
            return text.Length > 160 ? text.Substring( 0, 160 ) + "..." : text;
        }
    }
}
