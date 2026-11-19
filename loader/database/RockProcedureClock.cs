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
using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;

namespace RockSampleData.Loader.Database
{
    /*
        11/19/26 - CLAUDE

        Some of Rock's stored procedures read the clock themselves (GETDATE, RockGetDate, and the previous Sunday
        that comes from GETDATE), with no parameter for "now". Family Analytics and Calculate Peer Network work
        that way. The loader must run them as of a time it chooses: the job's last run before the anchor, which is
        not today when a test pins the anchor. So it reads the procedure's text as this Rock installed it, swaps
        each clock call for a parameter, and runs the body through sp_executesql. Everything else stays Rock's own
        SQL. When the text has a clock call the swap does not know, it returns null, and the caller runs Rock's
        procedure as it is, which is right whenever the anchor is today, as it always is in Rock's installer.

        The command text is "sp_executesql" with the body as a parameter, so the write recorder (WriteRecorder)
        never takes Rock's own statements for the loader's writes, or sp_executesql for a Rock procedure. The
        caller records the procedure by name.

        Reason: Rock's own procedures, run as of the time Rock would have run them.
    */

    /// <summary>
    /// Runs one of Rock's stored procedures from its installed text, with its clock pinned to a given time.
    /// </summary>
    internal sealed class RockProcedureClock
    {
        private static readonly Regex PreviousSunday = new Regex( @"(?:\[?dbo\]?\.)?\[?ufnUtility_GetPreviousSundayDate\]?\s*\(\s*\)", RegexOptions.IgnoreCase );
        private static readonly Regex RockGetDate = new Regex( @"(?:\[?dbo\]?\.)?\[?RockGetDate\]?\s*\(\s*\)", RegexOptions.IgnoreCase );
        private static readonly Regex GetDate = new Regex( @"\bGETDATE\s*\(\s*\)", RegexOptions.IgnoreCase );
        private static readonly Regex OtherClock = new Regex( @"\b(SYSDATETIME|SYSDATETIMEOFFSET|SYSUTCDATETIME|GETUTCDATE|CURRENT_TIMESTAMP|RockGetDate|GetPreviousSundayDate)\b", RegexOptions.IgnoreCase );

        private readonly RockDatabase _db;

        private RockProcedureClock( RockDatabase db, string name, string body )
        {
            _db = db;
            Name = name;
            Body = body;
        }

        /// <summary>
        /// The procedure's name, such as spCrm_FamilyAnalyticsGiving.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The procedure's body with its clock calls swapped for @PinnedNow and @PinnedPreviousSunday.
        /// </summary>
        public string Body { get; }

        /// <summary>
        /// Reads a procedure as this Rock installed it and pins its clock. Returns null when the procedure is missing or
        /// reads the clock in a way this does not know.
        /// </summary>
        public static RockProcedureClock TryRead( RockDatabase db, string name )
        {
            var definition = db.Scalar( "SELECT OBJECT_DEFINITION( OBJECT_ID( @Name ) )", RockDatabase.P( "@Name", $"[dbo].[{name}]" ) ) as string;
            if ( definition == null )
            {
                return null;
            }

            // Everything up to the AS that ends "CREATE PROCEDURE [dbo].[name]" is the header; the rest is the body.
            var header = new Regex( $@"\bCREATE\s+(?:OR\s+ALTER\s+)?PROC(?:EDURE)?\s+(?:\[?dbo\]?\.)?\[?{Regex.Escape( name )}\]?\s+AS\b", RegexOptions.IgnoreCase ).Match( definition );
            if ( !header.Success )
            {
                return null;
            }

            var body = definition.Substring( header.Index + header.Length );
            body = PreviousSunday.Replace( body, "@PinnedPreviousSunday" );
            body = RockGetDate.Replace( body, "@PinnedNow" );
            body = GetDate.Replace( body, "@PinnedNow" );

            // Comments may name a clock function; only code must not.
            var code = Regex.Replace( body, @"/\*.*?\*/|--[^\r\n]*", string.Empty, RegexOptions.Singleline );
            return OtherClock.IsMatch( code ) ? null : new RockProcedureClock( db, name, body );
        }

        /// <summary>
        /// A command that runs the procedure as of <paramref name="now"/>, inside the load's transaction. The previous
        /// Sunday is Rock's ufnUtility_GetPreviousSundayDate for that time.
        /// </summary>
        public SqlCommand Command( DateTime now )
        {
            var command = _db.CreateCommand( "sp_executesql" );
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.Add( new SqlParameter( "@stmt", SqlDbType.NVarChar, -1 ) { Value = Body } );
            command.Parameters.Add( new SqlParameter( "@params", SqlDbType.NVarChar, -1 ) { Value = "@PinnedNow DATETIME, @PinnedPreviousSunday DATE" } );
            command.Parameters.Add( new SqlParameter( "@PinnedNow", SqlDbType.DateTime ) { Value = now } );
            command.Parameters.Add( new SqlParameter( "@PinnedPreviousSunday", SqlDbType.Date ) { Value = RockSundays.PreviousSunday( now ) } );
            return command;
        }
    }

    /// <summary>
    /// Rock's Sunday dates, with its default first day of the week, Monday.
    /// </summary>
    internal static class RockSundays
    {
        /// <summary>
        /// Rock's ufnUtility_GetSundayDate: the Sunday that ends the date's week (the date itself on a Sunday).
        /// </summary>
        public static DateTime SundayDate( DateTime date )
        {
            return date.DayOfWeek == DayOfWeek.Sunday ? date.Date : date.Date.AddDays( 7 - ( int ) date.DayOfWeek );
        }

        /// <summary>
        /// Rock's ufnUtility_GetPreviousSundayDate as of <paramref name="now"/>: the Sunday a week before this week's.
        /// </summary>
        public static DateTime PreviousSunday( DateTime now ) => SundayDate( now ).AddDays( -7 );
    }
}
