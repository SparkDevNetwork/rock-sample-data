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

using Microsoft.Data.SqlClient;

namespace RockSampleData.Verify
{
    /// <summary>
    /// The SQL Server the verify tool works on: copies of the trusted base, snapshots, and queries.
    /// Every database it makes starts with "RockSampleVerify", so it never touches anyone else's database.
    /// </summary>
    internal sealed class SqlServer
    {
        public const string DatabasePrefix = "RockSampleVerify";

        private readonly string _server;

        public SqlServer( string server )
        {
            _server = server;
        }

        /// <summary>
        /// A connection string to one database on this server.
        /// </summary>
        public string ConnectionString( string database )
        {
            return new SqlConnectionStringBuilder
            {
                DataSource = _server,
                InitialCatalog = database,
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true,
                CommandTimeout = 1800
            }.ConnectionString;
        }

        /// <summary>
        /// A connection string for Rock itself (EF6 and System.Data.SqlClient), which reject the newer client's
        /// spaced keywords such as "Multiple Active Result Sets".
        /// </summary>
        public string RockConnectionString( string database )
        {
            return $"Data Source={_server};Initial Catalog={database};Integrated Security=True;MultipleActiveResultSets=True;TrustServerCertificate=True";
        }

        /// <summary>
        /// Runs a batch and returns every row of its first result set.
        /// </summary>
        public List<Dictionary<string, object>> Query( string database, string sql, params SqlParameter[] parameters )
        {
            using var connection = new SqlConnection( ConnectionString( database ) );
            connection.Open();
            using var command = new SqlCommand( sql, connection ) { CommandTimeout = 1800 };
            command.Parameters.AddRange( parameters );
            using var reader = command.ExecuteReader();
            var rows = new List<Dictionary<string, object>>();
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
        /// Runs a batch and returns the first column of the first row.
        /// </summary>
        public object Scalar( string database, string sql, params SqlParameter[] parameters )
        {
            var row = Query( database, sql, parameters ).FirstOrDefault();
            return row?.Values.FirstOrDefault();
        }

        /// <summary>
        /// Runs a batch that returns nothing.
        /// </summary>
        public void Execute( string database, string sql )
        {
            using var connection = new SqlConnection( ConnectionString( database ) );
            connection.Open();
            using var command = new SqlCommand( sql, connection ) { CommandTimeout = 1800 };
            command.ExecuteNonQuery();
        }

        public bool Exists( string database )
        {
            return Scalar( "master", "SELECT DB_ID( @Name )", new SqlParameter( "@Name", database ) ) != null;
        }

        /// <summary>
        /// Drops one of this tool's databases or snapshots, closing any open connections first.
        /// </summary>
        public void Drop( string database )
        {
            RequireOwn( database );
            if ( !Exists( database ) )
            {
                return;
            }

            // SQL Server will not drop a database that still has a snapshot, which a stopped run can leave behind.
            var snapshots = Query( "master", "SELECT [name] FROM sys.databases WHERE [source_database_id] = DB_ID( @Name )", new SqlParameter( "@Name", database ) );
            foreach ( var snapshot in snapshots )
            {
                Drop( ( string ) snapshot["name"] );
            }

            var isSnapshot = Scalar( "master", "SELECT [source_database_id] FROM sys.databases WHERE [name] = @Name", new SqlParameter( "@Name", database ) ) != null;
            var close = isSnapshot ? string.Empty : $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;";
            Execute( "master", $"{close} DROP DATABASE [{database}];" );
        }

        /// <summary>
        /// Makes a full, independent copy of a database by backing it up and restoring it under a new name.
        /// </summary>
        public void Copy( string source, string target )
        {
            Drop( target );
            var backupFolder = ( string ) Scalar( "master", "SELECT CAST( SERVERPROPERTY( 'InstanceDefaultBackupPath' ) AS NVARCHAR(400) )" );
            var dataFolder = ( string ) Scalar( "master", "SELECT CAST( SERVERPROPERTY( 'InstanceDefaultDataPath' ) AS NVARCHAR(400) )" );
            // One backup file per target, overwritten each time, so nothing piles up in the server's backup folder.
            // The target carries the run's tag, so parallel runs copying the same source never share a file.
            var backup = Path.Combine( backupFolder, $"{target}.bak" );
            Execute( "master", $"BACKUP DATABASE [{source}] TO DISK = N'{backup}' WITH COPY_ONLY, INIT, COMPRESSION;" );

            var moves = Query( "master", "SELECT [name], [type] FROM sys.master_files WHERE [database_id] = DB_ID( @Name )", new SqlParameter( "@Name", source ) )
                .Select( f => $"MOVE N'{f["name"]}' TO N'{Path.Combine( dataFolder, $"{target}_{f["name"]}{( Convert.ToInt32( f["type"] ) == 1 ? ".ldf" : ".mdf" )}" )}'" );
            Execute( "master", $"RESTORE DATABASE [{target}] FROM DISK = N'{backup}' WITH {string.Join( ", ", moves )}, RECOVERY, REPLACE;" );
        }

        /// <summary>
        /// Creates a read-only, copy-on-write snapshot of a database, for comparing it before and after a change.
        /// </summary>
        public void Snapshot( string source, string snapshot )
        {
            Drop( snapshot );
            var dataFolder = ( string ) Scalar( "master", "SELECT CAST( SERVERPROPERTY( 'InstanceDefaultDataPath' ) AS NVARCHAR(400) )" );
            var files = Query( "master", "SELECT [name] FROM sys.master_files WHERE [database_id] = DB_ID( @Name ) AND [type] = 0", new SqlParameter( "@Name", source ) )
                .Select( f => $"( NAME = N'{f["name"]}', FILENAME = N'{Path.Combine( dataFolder, $"{snapshot}_{f["name"]}.ss" )}' )" );
            Execute( "master", $"CREATE DATABASE [{snapshot}] ON {string.Join( ", ", files )} AS SNAPSHOT OF [{source}];" );
        }

        private static void RequireOwn( string database )
        {
            if ( !database.StartsWith( DatabasePrefix, StringComparison.Ordinal ) )
            {
                throw new InvalidOperationException( $"The verify tool only drops its own databases (named {DatabasePrefix}...), not {database}." );
            }
        }
    }
}
