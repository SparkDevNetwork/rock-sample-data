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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Loading;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Validation
{
    /// <summary>
    /// Checks the Cast's check-in stories against the loaded database: a family that names its kids kiosk checked its
    /// kids in there last time, the label went to that kiosk's printer at a reserved address, and the adult who checked
    /// the kids in checked them out (decisions 360 to 362).
    /// </summary>
    internal static class CastKioskExpectations
    {
        private const string YoungKidAreas = "'CADB2D12-7836-44BC-8EEA-3C6AB22FD5E8', 'E3C8F7D6-5CEB-43BB-802F-66C3E734049E'";

        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, DateTime anchor, List<string> failures )
        {
            if ( source.Kiosks == null )
            {
                return 0;
            }

            var mainCampus = source.Campuses.Campuses.Single( c => c.RockDefault ).Key;
            var checks = CheckStudentNights( db, source, guids, mainCampus, anchor, failures );
            foreach ( var household in source.ReadHouseholds().Where( h => h.Attendance?.KidsKiosk != null ) )
            {
                var campus = household.CampusSlot == "main" ? mainCampus : household.CampusSlot;
                var members = household.Members.Select( ( m, i ) => P( $"@M{i}", guids.Value( m.Guid ) ) ).ToArray();
                var latest = db.Query( $@"
SELECT TOP 1 [p].[NickName], [p].[LastName], [d].[Guid] AS [Kiosk], [d].[Name] AS [KioskName], [printer].[IPAddress] AS [PrinterAddress],
    [a].[EndDateTime], [a].[CheckedInByPersonAliasId], [a].[CheckedOutByPersonAliasId]
FROM [Attendance] AS [a]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [g] ON [g].[Id] = [o].[GroupId]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
LEFT JOIN [Device] AS [d] ON [d].[Id] = [a].[DeviceId]
LEFT JOIN [Device] AS [printer] ON [printer].[Id] = [d].[PrinterDeviceId]
WHERE [p].[Guid] IN ( {string.Join( ", ", members.Select( m => m.ParameterName ) )} ) AND [a].[AttendanceCodeId] IS NOT NULL AND [gt].[Guid] IN ( {YoungKidAreas} )
ORDER BY [a].[StartDateTime] DESC", members ).FirstOrDefault();
                if ( latest == null )
                {
                    continue;
                }

                checks += 3;
                var name = $"{latest["NickName"]} {latest["LastName"]}";
                if ( latest["Kiosk"] as Guid? != CheckInDevicesLoader.KioskGuid( source.Kiosks, campus, "kids", household.Attendance.KidsKiosk ) )
                {
                    failures.Add( $"{name}: expected the latest kids check-in at {campus} kids kiosk {household.Attendance.KidsKiosk}, Rock shows {latest["KioskName"] as string ?? "no kiosk"}" );
                }

                if ( !( latest["PrinterAddress"] as string ?? string.Empty ).StartsWith( source.Kiosks.Network.Substring( 0, source.Kiosks.Network.LastIndexOf( '.' ) + 1 ), StringComparison.Ordinal ) )
                {
                    failures.Add( $"{name}: expected the label to go to a printer in {source.Kiosks.Network}, Rock shows {latest["PrinterAddress"] as string ?? "no printer"}" );
                }

                if ( latest["EndDateTime"] is not DateTime || !Equals( latest["CheckedOutByPersonAliasId"], latest["CheckedInByPersonAliasId"] ) )
                {
                    failures.Add( $"{name}: expected the adult who checked them in to check them out after the service, Rock shows no such check-out" );
                }
            }

            return checks;
        }

        /// <summary>
        /// A Cast student with a Student Night habit checks in on Wednesday nights at their campus's student kiosk: since
        /// they started, or on at least half their share of the last 26 Wednesdays. A Cast student without one never comes
        /// on a Wednesday (decision 364).
        /// </summary>
        private static int CheckStudentNights( RockDatabase db, SampleDataSource source, CastPersonGuids guids, string mainCampus, DateTime anchor, List<string> failures )
        {
            var nightSlots = source.Schedules.Slots.Where( s => s.Day != "Sunday" ).Select( ( s, i ) => P( $"@Slot{i}", s.Guid ) ).ToArray();
            if ( nightSlots.Length == 0 )
            {
                return 0;
            }

            var checks = 0;
            var dates = new AnchorDates( anchor );
            foreach ( var household in source.ReadHouseholds() )
            {
                var campus = household.CampusSlot == "main" ? mainCampus : household.CampusSlot;
                var studentKiosk = CheckInDevicesLoader.KioskGuid( source.Kiosks, campus, "students", null );
                foreach ( var member in household.Members.Where( m => m.FamilyRole == "child" ) )
                {
                    var parameters = nightSlots.Select( p => P( p.ParameterName, p.Value ) ).Append( P( "@Guid", guids.Value( member.Guid ) ) ).ToArray();
                    var nights = db.Query( $@"
SELECT [o].[OccurrenceDate], [d].[Guid] AS [Kiosk]
FROM [Attendance] AS [a]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Schedule] AS [s] ON [s].[Id] = [o].[ScheduleId]
LEFT JOIN [Device] AS [d] ON [d].[Id] = [a].[DeviceId]
WHERE [p].[Guid] = @Guid AND [a].[DidAttend] = 1 AND [s].[Guid] IN ( {string.Join( ", ", nightSlots.Select( p => p.ParameterName ) )} )", parameters )
                        .Select( r => (Date: ( DateTime ) r["OccurrenceDate"], Kiosk: r["Kiosk"] as Guid?) )
                        .ToList();
                    var name = $"{member.NickName ?? member.FirstName} {member.LastName}";
                    var habit = member.StudentNight;
                    checks++;
                    if ( habit == null )
                    {
                        if ( nights.Count > 0 )
                        {
                            failures.Add( $"{name}: expected no Student Night check-ins, Rock shows {nights.Count}" );
                        }

                        continue;
                    }

                    if ( db.Scalar( "SELECT 1 FROM [Person] WHERE [Guid] = @Guid", P( "@Guid", guids.Value( member.Guid ) ) ) == null )
                    {
                        continue;
                    }

                    checks += 2;
                    var started = dates.Resolve( habit.Started );
                    var since = started ?? anchor.Date.AddDays( -182 );
                    var counted = nights.Count( n => n.Date >= since );
                    var expected = started.HasValue ? 1 : ( int ) Math.Floor( 26 * habit.PercentAttended / 200.0 );
                    if ( counted < expected || ( started.HasValue && nights.Any( n => n.Date < started.Value ) ) )
                    {
                        failures.Add( started.HasValue
                            ? $"{name}: expected Student Night check-ins only since {started:yyyy-MM-dd}, and at least one, Rock shows {counted} since and {nights.Count - counted} before"
                            : $"{name}: expected at least {expected} Student Night check-ins in the last 26 weeks ({habit.PercentAttended}% of Wednesdays), Rock shows {counted}" );
                    }

                    if ( nights.Any( n => n.Kiosk != studentKiosk ) )
                    {
                        failures.Add( $"{name}: expected every Student Night check-in at {campus}'s student kiosk, Rock shows {nights.Count( n => n.Kiosk != studentKiosk )} elsewhere" );
                    }
                }
            }

            return checks;
        }
    }
}
