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

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// The campus pastor of every campus other than the main one (configuration/notes.json). Notes and
    /// communications both use them, so both pick the same person.
    /// </summary>
    internal static class CampusPastors
    {
        /// <summary>
        /// Each campus other than the main one gets one campus pastor from its generated households:
        /// the oldest Leading adult, or the oldest Engaged adult when the campus has no Leading adult.
        /// </summary>
        public static Dictionary<string, LoadedPerson> Pick( IEnumerable<LoadedHousehold> generated, string mainCampusKey )
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
