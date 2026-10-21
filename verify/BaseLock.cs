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
        10/21/26 - CLAUDE

        Several verify runs can share one SQL Server, each from its own worktree. Their own databases carry
        a run tag, so they never meet, but they all copy from one trusted base. A run holds a shared lock on
        the base for as long as it runs, and takes an exclusive lock only to rebuild a stale base. So a
        rebuild waits for every running verification, and no run copies a half-built base.

        The lock is SQL Server's own application lock, held by an open session, so it is released even if
        the process dies.

        Reason: Parallel verify runs on one server stop dropping each other's databases.
    */

    /// <summary>
    /// SQL Server's application lock on the trusted base: shared while a run uses it, exclusive to rebuild it.
    /// </summary>
    internal sealed class BaseLock : IDisposable
    {
        private readonly SqlConnection _connection;
        private readonly string _resource;
        private string _mode;

        public BaseLock( SqlServer sql, string baseDatabase )
        {
            _resource = baseDatabase;
            _connection = new SqlConnection( sql.ConnectionString( "master" ) );
            _connection.Open();
        }

        /// <summary>
        /// Takes the lock in "Shared" or "Exclusive" mode, releasing any lock this run held first.
        /// Waits as long as it takes, and says so when another run holds it.
        /// </summary>
        public void Take( string mode )
        {
            Release();
            if ( !TryTake( mode, 0 ) )
            {
                Console.WriteLine( $"  Waiting for another verify run to finish with {_resource} ({mode.ToLowerInvariant()} lock)..." );
                if ( !TryTake( mode, -1 ) )
                {
                    throw new InvalidOperationException( $"Could not lock {_resource}." );
                }
            }

            _mode = mode;
        }

        public void Dispose()
        {
            _connection.Dispose();
        }

        private bool TryTake( string mode, int timeoutMilliseconds )
        {
            using var command = new SqlCommand( "EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = @LockMode, @LockOwner = 'Session', @LockTimeout = @Timeout", _connection ) { CommandTimeout = 0 };
            command.Parameters.AddWithValue( "@Resource", _resource );
            command.Parameters.AddWithValue( "@LockMode", mode );
            command.Parameters.AddWithValue( "@Timeout", timeoutMilliseconds );
            var result = command.Parameters.Add( "@Result", System.Data.SqlDbType.Int );
            result.Direction = System.Data.ParameterDirection.Output;
            command.ExecuteNonQuery();

            // 0 is granted at once, 1 is granted after waiting; anything below 0 is a timeout or an error.
            return ( int ) result.Value >= 0;
        }

        private void Release()
        {
            if ( _mode == null )
            {
                return;
            }

            using var command = new SqlCommand( "EXEC sp_releaseapplock @Resource = @Resource, @LockOwner = 'Session'", _connection );
            command.Parameters.AddWithValue( "@Resource", _resource );
            command.ExecuteNonQuery();
            _mode = null;
        }
    }
}
