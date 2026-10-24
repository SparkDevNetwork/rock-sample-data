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

namespace RockSampleData.LoaderCli
{
    /// <summary>
    /// Runs the calibration scripts (parts 1 to 3, part 5 for prayer, and part 6 for notes) against a database and writes their
    /// "Section, Json" rows as tab-separated lines, the same raw format normalize.js reads.
    /// </summary>
    internal static class CalibrationRunner
    {
        private static readonly string[] Scripts =
        {
            "Report_SampleDataCalibration_1_PeopleAndGiving.sql",
            "Report_SampleDataCalibration_2_AttendanceAndGroups.sql",
            "Report_SampleDataCalibration_3_RegistrationsPrayerCommsConnections.sql",
            "Report_SampleDataCalibration_5_Prayer.sql",
            "Report_SampleDataCalibration_6_PersonNotes.sql",
            "Report_SampleDataCalibration_7_Communications.sql"
        };

        /// <summary>
        /// Runs each script and writes every Section and Json pair to the output file. The scripts
        /// are built into this program from calibration/sql.
        /// </summary>
        public static void Run( string connectionString, string outputPath )
        {
            using var connection = new SqlConnection( connectionString );
            connection.Open();
            using var writer = new StreamWriter( outputPath );

            foreach ( var script in Scripts )
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                using var resource = typeof( CalibrationRunner ).Assembly.GetManifestResourceStream( $"sql/{script}" )
                    ?? throw new InvalidOperationException( $"{script} is not built into this program." );
                var sql = new StreamReader( resource ).ReadToEnd();

                // A freshly loaded database has not run Rock's post-update jobs, so it lacks the CommunicationRecipient
                // index part 7 reads by. The loaded data is small enough to read without it.
                sql = sql.Replace( "DECLARE @ReadWithoutIndex BIT = 0;", "DECLARE @ReadWithoutIndex BIT = 1;" );
                using var command = new SqlCommand( sql, connection ) { CommandTimeout = 600 };
                using var reader = command.ExecuteReader();
                var sections = 0;

                // The scripts may return helper result sets first; only the Section and Json set is kept.
                do
                {
                    if ( reader.FieldCount != 2 || reader.GetName( 0 ) != "Section" || reader.GetName( 1 ) != "Json" )
                    {
                        continue;
                    }

                    while ( reader.Read() )
                    {
                        var json = reader.IsDBNull( 1 ) ? "null" : reader.GetString( 1 );
                        writer.WriteLine( $"{reader.GetString( 0 )}\t{json.Replace( "\r", " " ).Replace( "\n", " " )}" );
                        sections++;
                    }
                }
                while ( reader.NextResult() );

                Console.WriteLine( $"  {script}: {sections} sections ({timer.ElapsedMilliseconds:N0} ms)" );
            }
        }
    }
}
