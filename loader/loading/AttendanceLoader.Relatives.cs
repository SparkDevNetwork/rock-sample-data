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

using RockSampleData.Loader.Generation;

namespace RockSampleData.Loader.Loading
{
    /*
        11/23/26 - CLAUDE

        A last pass, after every family's and student's Sundays, so it never shifts their draws. A Cast adult with
        bringsPercent brings the child's family's young children on some Sundays the adult came and the family did
        not (decision D-checkin-config-2). A generated "Can check in" adult checks the children in on some of their
        family's Sundays, and comes to the service when not already there, so every young child's check-in is still
        made by an adult in a service that day (decision D-checkin-config-8). Then each large group's check-ins
        alternate between its two rooms in arrival order, the way Rock's Balance strategy fills the emptier room
        (decision D-checkin-config-5). Rows this pass adds out of date order get their first-time flag from all of the
        person's rows at the end, as the student pass's do.

        Reason: Who checked a child in, and which room they went to, should match the check-in setup.
    */

    internal sealed partial class AttendanceLoader
    {
        /// <summary>
        /// The adults who bring or check in children from another family. Null plans none.
        /// </summary>
        public CheckInRelatives Relatives { get; set; }

        /// <summary>
        /// The relatives' check-ins, then the large groups' second rooms.
        /// </summary>
        private void PlanRelativesAndSecondRooms()
        {
            if ( Relatives != null && ( Relatives.Visits.Count > 0 || Relatives.CheckIns.Count > 0 ) )
            {
                PlanRelatives();
            }

            BalanceSecondRooms();
        }

        private void PlanRelatives()
        {
            var occurrences = _occurrenceIds.ToDictionary( o => o.Value, o => o.Key );
            var adults = Relatives.Visits.Select( v => v.Adult.AliasId ).Concat( Relatives.CheckIns.Select( c => c.Adult.AliasId ) ).ToHashSet();
            var children = Relatives.CheckIns.SelectMany( c => c.YoungChildren ).Select( c => c.AliasId ).ToHashSet();

            // One pass over the planned rows: the service each of these adults was in on each Sunday, and each child's check-ins.
            var services = new Dictionary<int, SortedDictionary<DateTime, int>>();
            var checkIns = new Dictionary<int, List<DataRow>>();
            foreach ( DataRow row in _attendance.Rows )
            {
                var alias = ( int ) row["PersonAliasId"];
                var occurrence = occurrences[( int ) row["OccurrenceId"]];
                if ( occurrence.Group == _configuration.WorshipGroupIdValue && adults.Contains( alias ) )
                {
                    if ( !services.TryGetValue( alias, out var sundays ) )
                    {
                        sundays = new SortedDictionary<DateTime, int>();
                        services[alias] = sundays;
                    }

                    sundays[occurrence.Date] = occurrence.Schedule;
                }
                else if ( row["AttendanceCodeId"] != DBNull.Value && occurrence.Date.DayOfWeek == DayOfWeek.Sunday && children.Contains( alias ) )
                {
                    if ( !checkIns.TryGetValue( alias, out var rows ) )
                    {
                        rows = new List<DataRow>();
                        checkIns[alias] = rows;
                    }

                    rows.Add( row );
                }
            }

            foreach ( var visit in Relatives.Visits )
            {
                PlanVisit( visit, services.GetValueOrDefault( visit.Adult.AliasId ) );
            }

            // A family's security code is one check-in session, so whoever checks the children in checks in everyone on it,
            // an older sibling at the student service included.
            var codes = checkIns.Values.SelectMany( r => r ).Select( r => ( int ) r["AttendanceCodeId"] ).ToHashSet();
            var rowsByCode = _attendance.AsEnumerable()
                .Where( r => r["AttendanceCodeId"] != DBNull.Value && codes.Contains( ( int ) r["AttendanceCodeId"] ) )
                .GroupBy( r => ( int ) r["AttendanceCodeId"] )
                .ToDictionary( g => g.Key, g => g.ToList() );
            foreach ( var relative in Relatives.CheckIns )
            {
                PlanRelativeCheckIns( relative, relative.YoungChildren.SelectMany( c => checkIns.GetValueOrDefault( c.AliasId ) ?? new List<DataRow>() ).ToList(), rowsByCode, services, occurrences );
            }
        }

        /// <summary>
        /// A Cast adult brings the children to the adult's own service on some Sundays the adult came and the children's
        /// family did not, from when that family started coming. The adult checks them in with one code for the day.
        /// </summary>
        private void PlanVisit( RelativeVisit visit, SortedDictionary<DateTime, int> adultSundays )
        {
            if ( adultSundays == null )
            {
                return;
            }

            var plan = _configuration.CampusPlans[visit.AdultHousehold.CampusKey];
            var (started, ended) = Window( visit.ChildHousehold );
            var familySundays = FamilySundays.GetValueOrDefault( visit.ChildHousehold.FamilyId ) ?? new HashSet<DateTime>();
            foreach ( var (sunday, scheduleId) in adultSundays )
            {
                var service = plan.Services.FirstOrDefault( s => s.ScheduleId == scheduleId );
                if ( sunday < started || sunday > ended || familySundays.Contains( sunday ) || service == null )
                {
                    continue;
                }

                var random = new SeededRandom( $"{_seed}:relative-visit:{visit.Adult.Cast.Guid}:{visit.ChildHousehold.Cast.Guid}:{sunday:yyyy-MM-dd}" );
                if ( !random.Chance( visit.Percent / 100.0 * KidsFactor( sunday ) ) )
                {
                    continue;
                }

                foreach ( var child in visit.Children.Where( c => c.CanAttend ) )
                {
                    var groupKey = CheckInGroupFor( child, sunday );
                    if ( groupKey == null || IsStudentGroup( groupKey ) || !_checkedInThisSunday.Add( (child.PersonId, sunday) ) )
                    {
                        continue;
                    }

                    var start = sunday.Add( service.Start ).AddMinutes( -random.Between( 2, 25 ) );
                    if ( !_familyCodes.TryGetValue( (visit.AdultHousehold.FamilyId, sunday), out var codeId ) )
                    {
                        codeId = NewCode( random, sunday, start );
                        _familyCodes[(visit.AdultHousehold.FamilyId, sunday)] = codeId;
                    }

                    AddCheckIn( child, plan, groupKey, service, sunday, start, codeId, visit.Adult.AliasId );
                    _studentAliases.Add( child.AliasId );
                }
            }
        }

        /// <summary>
        /// A generated adult checks the children in on some of their family's Sundays, with everyone else on the family's
        /// security code that day, and is in that service when not already in one that day.
        /// </summary>
        private void PlanRelativeCheckIns( RelativeCheckIn relative, List<DataRow> rows, Dictionary<int, List<DataRow>> rowsByCode, Dictionary<int, SortedDictionary<DateTime, int>> services, Dictionary<int, (int Group, int Location, int Schedule, DateTime Date)> occurrences )
        {
            var plan = _configuration.CampusPlans[relative.ChildHousehold.CampusKey];
            var random = new SeededRandom( $"{_seed}:relative-check-in:{relative.ChildHousehold.Cast.Guid}" );
            if ( !services.TryGetValue( relative.Adult.AliasId, out var adultSundays ) )
            {
                adultSundays = new SortedDictionary<DateTime, int>();
                services[relative.Adult.AliasId] = adultSundays;
            }

            foreach ( var sunday in rows.GroupBy( r => occurrences[( int ) r["OccurrenceId"]].Date ).OrderBy( g => g.Key ) )
            {
                if ( !random.Chance( relative.Percent / 100.0 ) )
                {
                    continue;
                }

                foreach ( var row in sunday.Select( r => ( int ) r["AttendanceCodeId"] ).Distinct().SelectMany( code => rowsByCode[code] ) )
                {
                    row["CheckedInByPersonAliasId"] = relative.Adult.AliasId;
                }

                if ( adultSundays.ContainsKey( sunday.Key ) )
                {
                    continue;
                }

                var first = sunday.OrderBy( r => ( DateTime ) r["StartDateTime"] ).First();
                var scheduleId = occurrences[( int ) first["OccurrenceId"]].Schedule;
                var service = plan.Services.FirstOrDefault( s => s.ScheduleId == scheduleId ) ?? plan.Services[0];
                AddWorship( relative.Adult, plan, service, sunday.Key, ( ( DateTime ) first["StartDateTime"] ).AddMinutes( -random.Between( 0, 5 ) ) );
                adultSundays[sunday.Key] = service.ScheduleId;
                _studentAliases.Add( relative.Adult.AliasId );
            }
        }

        /// <summary>
        /// Moves every other check-in of each first-room occurrence of a large group, in arrival order, to its second room.
        /// </summary>
        private void BalanceSecondRooms()
        {
            var secondRooms = _configuration.CampusPlans.Values
                .SelectMany( p => p.SecondRoomIds.Select( s => (Group: _configuration.CheckInGroupIdsByKey[s.Key], First: p.RoomIds[s.Key], Second: s.Value) ) )
                .ToDictionary( s => (s.Group, s.First), s => s.Second );
            if ( secondRooms.Count == 0 )
            {
                return;
            }

            var split = _occurrenceIds.Where( o => secondRooms.ContainsKey( (o.Key.Group, o.Key.Location) ) ).ToDictionary( o => o.Value, o => o.Key );
            var rowsByOccurrence = _attendance.AsEnumerable().Where( r => split.ContainsKey( ( int ) r["OccurrenceId"] ) ).GroupBy( r => ( int ) r["OccurrenceId"] ).OrderBy( g => g.Key ).ToList();
            foreach ( var occurrence in rowsByOccurrence )
            {
                var key = split[occurrence.Key];
                var second = Occurrence( key.Group, secondRooms[(key.Group, key.Location)], key.Schedule, key.Date );
                var arrivals = occurrence.OrderBy( r => ( DateTime ) r["StartDateTime"] ).ThenBy( r => ( int ) r["Id"] ).ToList();
                for ( var i = 1; i < arrivals.Count; i += 2 )
                {
                    arrivals[i]["OccurrenceId"] = second;
                }
            }
        }
    }
}
