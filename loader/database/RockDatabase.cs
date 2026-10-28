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

using System.Data;

using Microsoft.Data.SqlClient;

namespace RockSampleData.Loader.Database
{
    /// <summary>
    /// A thin wrapper over one SQL connection and transaction to a Rock database.
    /// Every command runs inside the same transaction, so a failed load leaves no partial data.
    /// </summary>
    internal sealed class RockDatabase : IDisposable
    {
        private readonly SqlConnection _connection;
        private SqlTransaction _transaction;

        /// <summary>
        /// Opens a connection to the Rock database.
        /// </summary>
        public RockDatabase( string connectionString )
        {
            _connection = new SqlConnection( connectionString );
            _connection.Open();
        }

        /// <summary>
        /// Starts the transaction that every later command joins.
        /// </summary>
        public void BeginTransaction()
        {
            _transaction = _connection.BeginTransaction();
        }

        /*
            10/21/26 - CLAUDE

            Two loads started at once could both find a database with no people and both load. An
            exclusive application lock owned by the transaction makes the second load fail at once,
            from any web server. An in-process lock could not do that in a web farm. The lock ends
            with the transaction. sp_getapplock runs as a stored procedure command, so the
            write recorder does not list it among the Rock procedures the load runs.

            Reason: The fresh-database guard must hold when two people press Load together.
        */

        /// <summary>
        /// Takes the exclusive sample data load lock for the open transaction. Returns false when another load holds it.
        /// </summary>
        public bool TryLockLoad()
        {
            using var command = new SqlCommand( "sp_getapplock", _connection, _transaction ) { CommandType = CommandType.StoredProcedure };
            command.Parameters.AddWithValue( "@Resource", "RockSampleData.Loader" );
            command.Parameters.AddWithValue( "@LockMode", "Exclusive" );
            command.Parameters.AddWithValue( "@LockOwner", "Transaction" );
            command.Parameters.AddWithValue( "@LockTimeout", 0 );
            var result = command.Parameters.Add( "@Result", SqlDbType.Int );
            result.Direction = ParameterDirection.ReturnValue;
            command.ExecuteNonQuery();
            return ( int ) result.Value >= 0;
        }

        /// <summary>
        /// Commits the transaction.
        /// </summary>
        public void Commit()
        {
            _transaction.Commit();
            _transaction = null;
        }

        /// <summary>
        /// Rolls back the transaction, if one is open.
        /// </summary>
        public void Rollback()
        {
            _transaction?.Rollback();
            _transaction = null;
        }

        /// <summary>
        /// Creates a SQL parameter, turning null into DBNull.
        /// </summary>
        public static SqlParameter P( string name, object value )
        {
            return new SqlParameter( name, value ?? DBNull.Value );
        }

        /// <summary>
        /// Today's date on the database server (SQL Server's GETDATE). Rock Cleanup computes ages and
        /// daily counts from this clock, so a load anchored to it agrees with Rock's own repairs, even
        /// when the server keeps a different time zone from the machine running the load.
        /// </summary>
        public DateTime ServerToday()
        {
            return ( DateTime ) Scalar( "SELECT CAST( GETDATE() AS date )" );
        }

        /// <summary>
        /// Runs a command and returns the first column of the first row.
        /// </summary>
        public object Scalar( string sql, params SqlParameter[] parameters )
        {
            using var command = CreateCommand( sql, parameters );
            var result = command.ExecuteScalar();
            return result == DBNull.Value ? null : result;
        }

        /// <summary>
        /// Runs a command and returns the number of rows affected.
        /// </summary>
        public int Execute( string sql, params SqlParameter[] parameters )
        {
            using var command = CreateCommand( sql, parameters );
            return command.ExecuteNonQuery();
        }

        /// <summary>
        /// Runs a query and returns every row as a dictionary of column name to value.
        /// </summary>
        public List<Dictionary<string, object>> Query( string sql, params SqlParameter[] parameters )
        {
            var rows = new List<Dictionary<string, object>>();
            using var command = CreateCommand( sql, parameters );
            using var reader = command.ExecuteReader();
            while ( reader.Read() )
            {
                var row = new Dictionary<string, object>( StringComparer.OrdinalIgnoreCase );
                for ( var i = 0; i < reader.FieldCount; i++ )
                {
                    row[reader.GetName( i )] = reader.IsDBNull( i ) ? null : reader.GetValue( i );
                }

                rows.Add( row );
            }

            return rows;
        }

        /// <summary>
        /// Returns the Id of the row with the given Guid, or null when there is none.
        /// The table name is never user input; it always comes from code.
        /// </summary>
        public int? IdByGuid( string table, Guid guid )
        {
            var id = Scalar( $"SELECT [Id] FROM [{table}] WHERE [Guid] = @Guid", P( "@Guid", guid ) );
            return id == null ? null : Convert.ToInt32( id );
        }

        /// <summary>
        /// Returns the Id of the row with the given Guid, or throws when it is missing.
        /// </summary>
        public int RequireIdByGuid( string table, Guid guid )
        {
            return IdByGuid( table, guid ) ?? throw new InvalidOperationException( $"{table} with Guid {guid} was not found." );
        }

        /// <summary>
        /// Bulk-copies a table of rows into a Rock table inside the transaction.
        /// </summary>
        public void BulkInsert( string table, DataTable rows, bool keepIdentity )
        {
            // Table locks are safe here: the load is one transaction on a fresh database, and they let SQL Server log less.
            var options = SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock | ( keepIdentity ? SqlBulkCopyOptions.KeepIdentity : SqlBulkCopyOptions.Default );
            using var bulk = new SqlBulkCopy( _connection, options, _transaction )
            {
                DestinationTableName = $"[{table}]",
                BatchSize = 0,
                BulkCopyTimeout = 600
            };

            foreach ( DataColumn column in rows.Columns )
            {
                bulk.ColumnMappings.Add( column.ColumnName, column.ColumnName );
            }

            Recorder?.RecordBulk( table, rows.Columns.Cast<DataColumn>().Select( c => c.ColumnName ) );

            var timer = System.Diagnostics.Stopwatch.StartNew();
            if ( rows.Rows.Count >= LargeLoadRows && !table.StartsWith( "#" ) && !_disabledIndexes.ContainsKey( table ) )
            {
                _disabledIndexes[table] = DisableNonclusteredIndexes( table );
            }

            bulk.WriteToServer( rows );
            Timings.Add( new LoadTiming( LoadTimingKind.BulkCopy, table, Log.CurrentStep, timer.ElapsedMilliseconds, rows.Rows.Count ) );

            if ( !_deferredRebuilds.ContainsKey( table ) )
            {
                RebuildDisabledIndexes( table );
            }
        }

        /*
            10/28/26 - CLAUDE

            A deferral is counted, so deferrals nest. The Communications step defers the
            email tables for the whole step, and each email writer inside it defers them too.
            A writer's rebuild then only ends its own deferral, and the indexes are rebuilt
            once, when the step ends its deferral. Before, each writer rebuilt them, and texts
            and push rebuilt them after their own copies: 3 or 4 rebuilds of tables that
            already held 1.3 to 1.9 million rows, about 118 of Full 7's 238 seconds.

            Reason: Rebuild each large table's indexes once per step, not once per writer.
        */

        /// <summary>
        /// Leaves a table's nonclustered indexes off after a large copy, so a later copy into the same table
        /// does not rebuild them again. Deferrals nest: each call needs a matching <see cref="RebuildDeferredIndexes"/>,
        /// and the indexes are rebuilt when the last one ends. Rebuild before anything reads the table.
        /// </summary>
        public void DeferIndexRebuild( string table )
        {
            _deferredRebuilds[table] = _deferredRebuilds.TryGetValue( table, out var count ) ? count + 1 : 1;
        }

        /// <summary>
        /// Ends one deferral of each table, and rebuilds the indexes of each table that no deferral holds any more.
        /// </summary>
        public void RebuildDeferredIndexes( params string[] tables )
        {
            foreach ( var table in tables )
            {
                if ( !_deferredRebuilds.TryGetValue( table, out var count ) )
                {
                    continue;
                }

                if ( count > 1 )
                {
                    _deferredRebuilds[table] = count - 1;
                    continue;
                }

                _deferredRebuilds.Remove( table );
                RebuildDisabledIndexes( table );
            }
        }

        /// <summary>
        /// Throws when any table still has a deferred rebuild or indexes this load turned off, so a load never
        /// commits a Rock database with disabled indexes.
        /// </summary>
        public void RequireIndexesRebuilt()
        {
            var tables = _deferredRebuilds.Keys.Concat( _disabledIndexes.Keys ).Distinct( StringComparer.OrdinalIgnoreCase ).ToList();
            if ( tables.Count > 0 )
            {
                throw new InvalidOperationException( $"The load ended with indexes still off on {string.Join( ", ", tables )}." );
            }
        }

        private readonly Dictionary<string, List<string>> _disabledIndexes = new Dictionary<string, List<string>>( StringComparer.OrdinalIgnoreCase );
        private readonly Dictionary<string, int> _deferredRebuilds = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );

        /*
            10/15/26 - CLAUDE

            Large tables (FinancialTransaction has 21 nonclustered indexes) load several
            times faster when their nonclustered indexes are disabled during the copy and
            rebuilt once afterward. Unique indexes are still enforced when they rebuild,
            so a duplicate still fails the load.

            Reason: Index maintenance was most of the Full 7 load time.

            10/16/26 - CLAUDE

            Only the indexes that were disabled are rebuilt. ALTER INDEX ALL also rebuilt the
            clustered index, which rewrote the whole table after every large copy. The cutoff is
            10,000 rows (it was 50,000): Person, Group, and GroupMember have many indexes and load
            20,000 to 30,000 rows in Full 7, and copying them with the indexes off is faster too.

            Reason: The clustered rebuild was most of the cost of each large copy.
        */

        /// <summary>
        /// The row count at which a bulk copy disables and rebuilds the table's nonclustered indexes.
        /// </summary>
        private const int LargeLoadRows = 10000;

        /// <summary>
        /// Disables every enabled nonclustered index of a table and returns their names.
        /// </summary>
        private List<string> DisableNonclusteredIndexes( string table )
        {
            var names = Query( "SELECT [name] FROM sys.indexes WHERE [object_id] = OBJECT_ID( @Table ) AND [type_desc] = 'NONCLUSTERED' AND [is_disabled] = 0", P( "@Table", table ) ).Select( r => ( string ) r["name"] ).ToList();
            foreach ( var name in names )
            {
                Execute( $"ALTER INDEX [{name}] ON [{table}] DISABLE" );
            }

            return names;
        }

        /// <summary>
        /// Rebuilds the indexes this load disabled on a table, if any.
        /// </summary>
        private void RebuildDisabledIndexes( string table )
        {
            if ( !_disabledIndexes.TryGetValue( table, out var names ) )
            {
                return;
            }

            _disabledIndexes.Remove( table );

            var timer = System.Diagnostics.Stopwatch.StartNew();
            foreach ( var name in names )
            {
                Execute( $"ALTER INDEX [{name}] ON [{table}] REBUILD" );
            }

            Timings.Add( new LoadTiming( LoadTimingKind.IndexRebuild, table, Log.CurrentStep, timer.ElapsedMilliseconds ) );
        }

        /// <summary>
        /// When set, records every column written, Guid named, and procedure run (see <see cref="WriteRecorder"/>).
        /// </summary>
        public WriteRecorder Recorder { get; set; }

        /// <summary>
        /// Where the load reports its progress. Reports nothing until a load sets it.
        /// </summary>
        public LoadLog Log { get; set; } = new LoadLog( null );

        /// <summary>
        /// Each bulk copy and index rebuild, with the step it ran in, for the end-of-load timing report.
        /// </summary>
        public List<LoadTiming> Timings { get; } = new List<LoadTiming>();

        /// <summary>
        /// Creates a command bound to the open transaction. Use it directly for commands that need special
        /// parameters, such as a table-valued parameter for a stored procedure.
        /// </summary>
        public SqlCommand CreateCommand( string sql, params SqlParameter[] parameters )
        {
            var command = new SqlCommand( sql, _connection, _transaction )
            {
                CommandTimeout = 600
            };
            command.Parameters.AddRange( parameters );
            Recorder?.RecordCommand( command );
            return command;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _transaction?.Dispose();
            _connection.Dispose();
        }
    }
}
