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

using RockSampleData.Loader.Database;

namespace RockSampleData.Loader.Loading
{
    /*
        11/23/26 - CLAUDE

        Rock saves one check-in session each time someone checks in at a kiosk: the kiosk and its address. Every
        attendance row from that check-in points at the session, with what was typed to find the family and the family
        it found. A family's kids share one session and one security code, so the loader opens a session with each
        code. A volunteer checking in to serve gets a session of their own.

        Reason: Rock's check-in screens and reports read the session, the kiosk, and the search (decision D-checkin-attendance-2).
    */

    /// <summary>
    /// The check-in sessions one step writes.
    /// </summary>
    internal sealed class CheckInSessions
    {
        private readonly RockDatabase _db;
        private readonly string _foreignKey;
        private readonly DataTable _rows = TableFactory.Create( "Id:int", "DeviceId:int", "ClientIpAddress:string", "Guid:guid", "ForeignKey:string" );
        private int _nextId;

        public CheckInSessions( RockDatabase db, string foreignKey )
        {
            _db = db;
            _foreignKey = foreignKey;
            _nextId = Convert.ToInt32( db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [AttendanceCheckInSession]" ) );
        }

        public int Count => _rows.Rows.Count;

        /// <summary>
        /// Opens a session at a kiosk and returns its Id, or null when there is no kiosk.
        /// </summary>
        public int? Open( Kiosk kiosk )
        {
            if ( kiosk == null )
            {
                return null;
            }

            var id = _nextId++;
            _rows.Rows.Add( id, kiosk.Id, ( object ) kiosk.Address ?? DBNull.Value, Guid.NewGuid(), _foreignKey );
            return id;
        }

        /// <summary>
        /// Writes the sessions. Attendance rows point at them, so they are written first.
        /// </summary>
        public void Write()
        {
            _db.BulkInsert( "AttendanceCheckInSession", _rows, keepIdentity: true );
        }

        /// <summary>
        /// What a person types at the kiosk to find their family: the last four digits of their mobile, of another adult's
        /// mobile in the family, or of the family's home phone. Null when the family has no phone, so a volunteer found it.
        /// </summary>
        public static string SearchValue( LoadedPerson searcher, IEnumerable<LoadedPerson> family )
        {
            var members = family.ToList();
            var phone = new[] { searcher }.Concat( members.Where( m => m.IsAdult ) ).Select( m => m.Cast.Phones?.Mobile )
                .Concat( members.Select( m => m.Cast.Phones?.Home ) )
                .FirstOrDefault( p => !string.IsNullOrEmpty( p ) );
            var digits = phone == null ? null : new string( phone.Where( char.IsDigit ).ToArray() );
            return digits == null || digits.Length < 4 ? null : digits.Substring( digits.Length - 4 );
        }
    }
}
