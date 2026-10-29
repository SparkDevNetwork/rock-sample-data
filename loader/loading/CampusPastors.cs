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

using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// The campus pastor of each campus (configuration/staff.json). Notes, the calendar, and communications all use
    /// them, so all pick the same person.
    /// </summary>
    internal static class CampusPastors
    {
        /*
            10/28/26 - CLAUDE

            Campus pastors are named Cast people now (decision 198), one for every campus, the main campus
            included. A package built before that has no configuration/staff.json, and it still gets the old
            rule: each campus other than the main one takes its oldest Leading adult from its generated
            households, or its oldest Engaged adult when it has no Leading adult.

            Reason: A newer loader must still load every published package (decision 168).
        */

        /// <summary>
        /// Each campus's campus pastor, by campus key. With staff configuration, every loaded campus has its named
        /// pastor, the main campus included. Without it, each campus other than the main one takes a generated adult.
        /// </summary>
        /// <param name="people">Everyone loaded.</param>
        /// <param name="generated">The generated households, for a package without staff configuration.</param>
        /// <param name="mainCampusKey">The main campus, which has no generated campus pastor.</param>
        /// <param name="staff">The staff configuration, or null.</param>
        /// <exception cref="InvalidDataException">A loaded campus has no named campus pastor, or the pastor is not loaded.</exception>
        public static Dictionary<string, LoadedPerson> Pick( PeopleLoader people, IEnumerable<LoadedHousehold> generated, string mainCampusKey, StaffConfiguration staff )
        {
            return staff == null ? Generated( generated, mainCampusKey ) : Named( people, staff );
        }

        /// <summary>
        /// The named campus pastor of every campus that has loaded households.
        /// </summary>
        private static Dictionary<string, LoadedPerson> Named( PeopleLoader people, StaffConfiguration staff )
        {
            var members = staff.Staff.ToDictionary( s => s.Key, StringComparer.Ordinal );
            var pastors = new Dictionary<string, LoadedPerson>();
            foreach ( var campusKey in people.Households.Select( h => h.CampusKey ).Distinct().OrderBy( k => k, StringComparer.Ordinal ) )
            {
                if ( !staff.CampusPastors.TryGetValue( campusKey, out var staffKey ) || !members.TryGetValue( staffKey, out var member ) )
                {
                    throw new InvalidDataException( $"configuration/staff.json names no campus pastor for {campusKey}, a campus in this dataset." );
                }

                if ( !people.People.TryGetValue( member.Person, out var pastor ) )
                {
                    throw new InvalidDataException( $"configuration/staff.json: {campusKey}'s campus pastor {member.Name} ({member.Person}) is not loaded." );
                }

                pastors[campusKey] = pastor;
            }

            return pastors;
        }

        /// <summary>
        /// Each campus other than the main one gets one campus pastor from its generated households:
        /// the oldest Leading adult, or the oldest Engaged adult when the campus has no Leading adult.
        /// </summary>
        private static Dictionary<string, LoadedPerson> Generated( IEnumerable<LoadedHousehold> generated, string mainCampusKey )
        {
            var pastors = new Dictionary<string, LoadedPerson>();
            foreach ( var campus in generated.Where( h => h.CampusKey != mainCampusKey ).GroupBy( h => h.CampusKey ) )
            {
                var adults = campus.SelectMany( h => h.Members ).Where( p => p.IsAdult && p.CanAttend && p.BirthDate.HasValue ).ToList();
                var pastor = new[] { "Leading", "Engaged" }
                    .Select( status => adults.Where( p => p.Cast.ConnectionStatus == status ).OrderBy( p => p.BirthDate ).ThenBy( p => p.PersonId ).FirstOrDefault() )
                    .FirstOrDefault( p => p != null );
                if ( pastor != null )
                {
                    pastors[campus.Key] = pastor;
                }
            }

            return pastors;
        }
    }
}
