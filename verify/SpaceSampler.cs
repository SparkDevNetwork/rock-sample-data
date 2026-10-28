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
    /*
        10/28/26 - CLAUDE

        The whole load is one transaction, so its log cannot be reused until it commits, and its peak
        disk use is a real cost of a large dataset. File sizes alone miss it: a file never shrinks, and the
        trusted base's files may already be larger than a small load needs. So this reads the used space
        once a second on its own connection, which only reads metadata and never touches the load's rows.
        It also counts the file growths the default trace saw, because with instant file initialization
        off, SQL Server writes zeros over every new extent before the load can use it.

        Reason: Measure a load's peak space without changing what it loads.
    */

    /// <summary>
    /// Samples one database's used and allocated space while a load runs, and keeps the peaks.
    /// Every reading is best-effort: a failed sample is skipped, never fatal.
    /// </summary>
    internal sealed class SpaceSampler : IDisposable
    {
        private const string SpaceSql = @"
SELECT [type] AS [Type],
    SUM( CAST( [size] AS BIGINT ) ) * 8 / 1024 AS [AllocatedMb],
    SUM( CAST( FILEPROPERTY( [name], 'SpaceUsed' ) AS BIGINT ) ) * 8 / 1024 AS [UsedMb]
FROM sys.database_files
GROUP BY [type]";

        private readonly SqlServer _sql;
        private readonly string _database;
        private readonly DateTime _startedAt;
        private readonly Timer _timer;
        private readonly object _gate = new object();
        private readonly Space _space = new Space();
        private bool _isStopped;

        /// <summary>
        /// Takes a first reading, then samples once a second until <see cref="Stop"/>.
        /// </summary>
        public SpaceSampler( SqlServer sql, string database )
        {
            _sql = sql;
            _database = database;
            _startedAt = ReadServerTime();
            Sample( isFirst: true );
            _timer = new Timer( _ => Sample( isFirst: false ), null, 1000, 1000 );
        }

        /// <summary>
        /// Stops sampling, takes a last reading, and returns what was seen.
        /// </summary>
        public Space Stop()
        {
            _timer.Dispose();
            lock ( _gate )
            {
                if ( !_isStopped )
                {
                    _isStopped = true;
                    ReadOnce( isFirst: false );
                    ReadSettings();
                    ReadGrowths();
                }

                return _space;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void Sample( bool isFirst )
        {
            // A slow sample never stacks up behind another one.
            if ( !Monitor.TryEnter( _gate ) )
            {
                return;
            }

            try
            {
                if ( !_isStopped )
                {
                    ReadOnce( isFirst );
                }
            }
            finally
            {
                Monitor.Exit( _gate );
            }
        }

        private void ReadOnce( bool isFirst )
        {
            try
            {
                foreach ( var row in _sql.Query( _database, SpaceSql ) )
                {
                    var isLog = Convert.ToInt32( row["Type"] ) == 1;
                    var allocated = Convert.ToInt64( row["AllocatedMb"] );
                    var used = row["UsedMb"] == null ? 0 : Convert.ToInt64( row["UsedMb"] );
                    var files = isLog ? _space.Log : _space.Data;
                    if ( isFirst )
                    {
                        files.UsedBeforeMb = used;
                        files.FileBeforeMb = allocated;
                    }

                    files.UsedPeakMb = Math.Max( files.UsedPeakMb, used );
                    files.FileAfterMb = allocated;
                }

                _space.Samples++;
            }
            catch ( SqlException )
            {
                // Intentionally ignored: sampling is best-effort, and one missed reading only lowers the precision of the peak.
            }
        }

        /// <summary>
        /// The recovery model and instant file initialization, which decide how much the load logs and what a file growth costs.
        /// </summary>
        private void ReadSettings()
        {
            try
            {
                _space.RecoveryModel = ( string ) _sql.Scalar( "master", "SELECT [recovery_model_desc] FROM sys.databases WHERE [name] = @Name", new SqlParameter( "@Name", _database ) );
                _space.IsInstantFileInitializationEnabled = ( string ) _sql.Scalar( "master", "SELECT TOP 1 [instant_file_initialization_enabled] FROM sys.dm_server_services WHERE [servicename] LIKE 'SQL Server (%'" ) == "Y";
            }
            catch ( SqlException )
            {
                // Intentionally ignored: these settings explain the numbers but are not needed to record them.
            }
        }

        /// <summary>
        /// The data and log file growths the default trace recorded for this database since sampling started.
        /// </summary>
        private void ReadGrowths()
        {
            try
            {
                var rows = _sql.Query( "master", @"
DECLARE @Path NVARCHAR(260) = ( SELECT [path] FROM sys.traces WHERE [is_default] = 1 );
IF @Path IS NOT NULL
BEGIN
    SET @Path = LEFT( @Path, LEN( @Path ) - CHARINDEX( '\', REVERSE( @Path ) ) ) + N'\log.trc';
    SELECT [t].[EventClass], COUNT(*) AS [Growths], SUM( [t].[Duration] ) / 1000 AS [Milliseconds]
    FROM sys.fn_trace_gettable( @Path, DEFAULT ) AS [t]
    WHERE [t].[EventClass] IN ( 92, 93 ) AND [t].[DatabaseName] = @Name AND [t].[StartTime] >= @Since
    GROUP BY [t].[EventClass];
END", new SqlParameter( "@Name", _database ), new SqlParameter( "@Since", _startedAt ) );
                foreach ( var row in rows )
                {
                    var files = Convert.ToInt32( row["EventClass"] ) == 93 ? _space.Log : _space.Data;
                    files.Growths = Convert.ToInt32( row["Growths"] );
                    files.GrowthMilliseconds = Convert.ToInt64( row["Milliseconds"] );
                }
            }
            catch ( SqlException )
            {
                // Intentionally ignored: the default trace may be off or unreadable, and the growth count is only a detail.
            }
        }

        private DateTime ReadServerTime()
        {
            try
            {
                return ( DateTime ) _sql.Scalar( "master", "SELECT GETDATE()" );
            }
            catch ( SqlException )
            {
                return DateTime.Now;
            }
        }

        /// <summary>
        /// One load's space, as the receipt records it. Sizes are in megabytes.
        /// </summary>
        internal sealed class Space
        {
            public Files Data { get; set; } = new Files();

            public Files Log { get; set; } = new Files();

            public string RecoveryModel { get; set; }

            public bool IsInstantFileInitializationEnabled { get; set; }

            /// <summary>
            /// How many readings were taken. About one a second, so peaks between readings can be missed by up to a second of growth.
            /// </summary>
            public int Samples { get; set; }

            public override string ToString()
            {
                return $"data {Data.UsedPeakMb:N0} MB used at peak ({Data.FileAfterMb:N0} MB file, {Data.Growths} growths in {Data.GrowthMilliseconds:N0} ms), "
                    + $"log {Log.UsedPeakMb:N0} MB used at peak ({Log.FileAfterMb:N0} MB file, {Log.Growths} growths in {Log.GrowthMilliseconds:N0} ms), "
                    + $"{RecoveryModel} recovery, instant file initialization {( IsInstantFileInitializationEnabled ? "on" : "off" )}";
            }
        }

        /// <summary>
        /// The data files or the log file of one database.
        /// </summary>
        internal sealed class Files
        {
            public long UsedBeforeMb { get; set; }

            public long UsedPeakMb { get; set; }

            public long FileBeforeMb { get; set; }

            public long FileAfterMb { get; set; }

            public int Growths { get; set; }

            public long GrowthMilliseconds { get; set; }
        }
    }
}
