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

namespace RockSampleData.Loader.Model
{
    /// <summary>
    /// configuration/staff.json: the church staff, every one a Cast person, and the campus pastor of each campus
    /// (decision 198).
    /// </summary>
    internal sealed class StaffConfiguration
    {
        /// <summary>
        /// Every staff member.
        /// </summary>
        public List<StaffMember> Staff { get; set; } = new List<StaffMember>();

        /// <summary>
        /// Each campus's campus pastor: a staff key by campus key.
        /// </summary>
        public Dictionary<string, string> CampusPastors { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// One staff member.
    /// </summary>
    internal sealed class StaffMember
    {
        public string Key { get; set; }

        /// <summary>
        /// The Cast person's Guid.
        /// </summary>
        public Guid Person { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The church's title for the role, such as "Peoria Campus Pastor".
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// The department key, such as "campuses".
        /// </summary>
        public string Department { get; set; }

        /// <summary>
        /// The campus key the staff member serves, or null for every campus.
        /// </summary>
        public string Campus { get; set; }
    }
}
