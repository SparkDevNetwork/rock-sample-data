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

            if ( !_deferredRebuilds.Contains( table ) )
            {
                RebuildDisabledIndexes( table );
            }

            Timings.Add( (table, rows.Rows.Count, timer.ElapsedMilliseconds) );
        }

        /// <summary>
        /// Leaves a table's nonclustered indexes off after a large copy, so a later copy into the same table
        /// does not rebuild them twice. Call <see cref="RebuildDeferredIndexes"/> before anything reads the table.
        /// </summary>
        public void DeferIndexRebuild( string table )
        {
            _deferredRebuilds.Add( table );
        }

        /// <summary>
        /// Rebuilds the indexes of every table passed to <see cref="DeferIndexRebuild"/>.
        /// </summary>
        public void RebuildDeferredIndexes()
        {
            foreach ( var table in _deferredRebuilds )
            {
                RebuildDisabledIndexes( table );
            }

            _deferredRebuilds.Clear();
        }

        private readonly Dictionary<string, List<string>> _disabledIndexes = new Dictionary<string, List<string>>( StringComparer.OrdinalIgnoreCase );
        private readonly HashSet<string> _deferredRebuilds = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

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
            if ( !_disabledIndexes.Remove( table, out var names ) )
            {
                return;
            }

            foreach ( var name in names )
            {
                Execute( $"ALTER INDEX [{name}] ON [{table}] REBUILD" );
            }
        }

        /// <summary>
        /// When set, records every column written, Guid named, and procedure run (see <see cref="WriteRecorder"/>).
        /// </summary>
        public WriteRecorder Recorder { get; set; }

        /// <summary>
        /// Rows and milliseconds for each bulk copy, for the end-of-load timing report.
        /// </summary>
        public List<(string Table, int Rows, long Milliseconds)> Timings { get; } = new List<(string, int, long)>();

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
