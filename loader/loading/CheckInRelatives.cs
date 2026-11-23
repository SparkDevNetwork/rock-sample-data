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

using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Loading
{
    /*
        11/23/26 - CLAUDE

        Some children are checked in by an adult from another family: a grandparent, an aunt, a family friend. Rock
        shows such a child on that adult's kiosk search through a Known Relationship whose role Rock marks Can
        Check-in. The Cast's stories name their pairs, and a Cast line with bringsPercent also brings the child on
        Sundays the child's own family stayed home (decision D-checkin-config-2). In the Crowd, a few families with
        young children get one adult at their campus who may check the children in, and who does on some of their
        Sundays (decision D-checkin-config-8). Each family draws from its own seeded stream, after people load and
        before attendance plans, so no other draw moves.

        Reason: A kiosk search and the attendance it records should show the people who really bring children.
    */

    /// <summary>
    /// A Cast adult who brings children from another family on some Sundays that family stayed home.
    /// </summary>
    internal sealed record RelativeVisit( LoadedPerson Adult, LoadedHousehold AdultHousehold, LoadedHousehold ChildHousehold, IReadOnlyList<LoadedPerson> Children, int Percent );

    /// <summary>
    /// A generated adult who may check in a generated family's young children, and checks them in on some of their Sundays.
    /// </summary>
    internal sealed record RelativeCheckIn( LoadedPerson Adult, LoadedHousehold ChildHousehold, IReadOnlyList<LoadedPerson> Children, double Percent );

    /// <summary>
    /// The adults who check in children from another family, which the relationships and attendance steps follow.
    /// </summary>
    internal sealed class CheckInRelatives
    {
        public List<RelativeVisit> Visits { get; } = new List<RelativeVisit>();

        public List<RelativeCheckIn> CheckIns { get; } = new List<RelativeCheckIn>();

        /// <summary>
        /// The generated "Can check in" relationships, one for each child. The Cast's come from its own lines.
        /// </summary>
        public List<KnownRelationship> GeneratedRelationships => CheckIns
            .SelectMany( c => c.Children.Select( child => new KnownRelationship( c.Adult, "can-check-in", child ) ) )
            .ToList();

        /// <summary>
        /// Plans the Cast's visits from its lines, and the Crowd's check-in adults from the configuration, which may be null.
        /// </summary>
        public static CheckInRelatives Plan( CrowdCheckInRelationships crowd, IReadOnlyList<LoadedHousehold> households, ISet<string> castHouseholdKeys, DateTime anchor, string seed )
        {
            var relatives = new CheckInRelatives();
            var homes = households.SelectMany( h => h.Members.Where( m => m.FamilyId == h.FamilyId ).Select( m => (m.Cast.Guid, Household: h, Person: m) ) )
                .ToDictionary( x => x.Guid, x => (x.Household, x.Person) );

            foreach ( var household in households.Where( h => castHouseholdKeys.Contains( h.Cast.Key ) ) )
            {
                foreach ( var adult in household.Members.Where( m => m.FamilyId == household.FamilyId ) )
                {
                    var visits = adult.Cast.Relationships
                        .Where( r => r.BringsPercent.HasValue && homes.ContainsKey( r.With ) )
                        .GroupBy( r => homes[r.With].Household.FamilyId );
                    foreach ( var family in visits )
                    {
                        var childHousehold = homes[family.First().With].Household;
                        relatives.Visits.Add( new RelativeVisit( adult, household, childHousehold, family.Select( r => homes[r.With].Person ).ToList(), family.First().BringsPercent.Value ) );
                    }
                }
            }

            if ( crowd == null )
            {
                return relatives;
            }

            static bool Attends( LoadedHousehold h ) => h.Cast.Attendance is { PercentAttended: > 0, Ended: null };
            int Age( LoadedPerson p ) => p.BirthDate is { } birth ? anchor.Year - birth.Year - ( anchor < birth.AddYears( anchor.Year - birth.Year ) ? 1 : 0 ) : -1;
            var generated = households.Where( h => !castHouseholdKeys.Contains( h.Cast.Key ) && h.Cast.Kind != "business" && Attends( h ) ).ToList();
            var adultsByCampus = generated
                .SelectMany( h => h.Members.Where( m => m.FamilyId == h.FamilyId && m.IsAdult && m.CanAttend && Age( m ) >= crowd.AdultMinimumAge ).Select( m => (h.CampusKey, Adult: m) ) )
                .GroupBy( x => x.CampusKey )
                .ToDictionary( g => g.Key, g => g.Select( x => x.Adult ).ToList() );

            foreach ( var household in generated )
            {
                var children = household.Members
                    .Where( m => m.FamilyId == household.FamilyId && !m.IsAdult && m.CanAttend && Age( m ) is >= 0 and var age && age <= crowd.ChildMaximumAge )
                    .ToList();
                var random = new SeededRandom( $"{seed}:check-in-relative:{household.Cast.Guid}" );
                if ( children.Count == 0 || !random.Chance( crowd.CrowdChildPercent / 100 ) )
                {
                    continue;
                }

                var adults = adultsByCampus.GetValueOrDefault( household.CampusKey )?.Where( a => a.FamilyId != household.FamilyId ).ToList();
                if ( adults is { Count: > 0 } )
                {
                    relatives.CheckIns.Add( new RelativeCheckIn( random.Pick( adults ), household, children, crowd.CheckedInByPercent ) );
                }
            }

            return relatives;
        }
    }
}
