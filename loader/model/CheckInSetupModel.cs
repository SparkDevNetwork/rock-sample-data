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
    /*
        11/23/26 - CLAUDE

        The check-in setup a real church sets on purpose (decisions D-checkin-config-3 to D-checkin-config-8) lives in
        checkin.json beside the rooms and groups. Every part of it is new in alpha.19, so each is null in an older
        package, and the loader then leaves Rock's check-in as that package left it.

        Reason: A newer loader must still read every published package (decision 168).
    */

    internal sealed partial class CheckInConfiguration
    {
        /// <summary>
        /// Rock's Weekly Service Check-in template.
        /// </summary>
        public NamedGuid Template { get; set; }

        /// <summary>
        /// Values for the template's own settings, by Rock attribute key, such as core_checkin_PreventDuplicateCheckin.
        /// </summary>
        public Dictionary<string, string> TemplateSettings { get; set; }

        /// <summary>
        /// Rock's next-gen labels the areas print, by key (for example "child").
        /// </summary>
        public Dictionary<string, NamedGuid> Labels { get; set; }

        /// <summary>
        /// Each room's firm limit, by check-in group key. Nobody checks a child in past it, not even a supervisor.
        /// </summary>
        public Dictionary<string, int> RoomFirmThreshold { get; set; }

        /// <summary>
        /// Rock's own test schedules and Check-in Test Area, which the Test Group carries to every campus.
        /// </summary>
        public TestCheckInDefinition TestCheckIn { get; set; }

        /// <summary>
        /// Who signs in to each campus's kiosk supervisor screen, and with what PIN.
        /// </summary>
        public List<SupervisorDefinition> Supervisors { get; set; }

        /// <summary>
        /// The serving team whose leader is a campus's supervisor when no staff member is named.
        /// </summary>
        public string SupervisorTeam { get; set; }

        /// <summary>
        /// Generated "Can check in" adults for some generated children.
        /// </summary>
        public CrowdCheckInRelationships Relationships { get; set; }
    }

    internal sealed partial class CheckInArea
    {
        /// <summary>
        /// The keys of the labels the area prints, in order, from <see cref="CheckInConfiguration.Labels"/>.
        /// </summary>
        public List<string> Labels { get; set; }
    }

    internal sealed partial class CheckInGroupDefinition
    {
        /// <summary>
        /// A second room for a large group. Rock's Balance strategy fills the two rooms evenly.
        /// </summary>
        public string SecondRoom { get; set; }
    }

    /// <summary>
    /// Rock's Check-in Test Area, its Test Group, and the two all-day test schedules.
    /// </summary>
    internal sealed class TestCheckInDefinition
    {
        public NamedGuid Area { get; set; }

        public NamedGuid Group { get; set; }

        public List<NamedGuid> Schedules { get; set; } = new List<NamedGuid>();

        /// <summary>
        /// The church's own volunteer test area, in Rock's test pattern. Null leaves volunteer check-in without one.
        /// </summary>
        public VolunteerTestAreaDefinition VolunteerArea { get; set; }
    }

    /// <summary>
    /// A check-in area under Rock's Volunteer Check-in template where anyone may check in to serve on the test schedules.
    /// </summary>
    internal sealed class VolunteerTestAreaDefinition
    {
        public string Name { get; set; }

        public Guid Guid { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The Guid of the area's one role, Member, which Rock needs as the area's default role.
        /// </summary>
        public Guid MemberRole { get; set; }

        public NamedGuid Template { get; set; }

        /// <summary>
        /// Each campus's test group name, with {campus} for the campus name.
        /// </summary>
        public string GroupNameFormat { get; set; }

        /// <summary>
        /// The keys of the labels the area prints, from <see cref="CheckInConfiguration.Labels"/>.
        /// </summary>
        public List<string> Labels { get; set; }
    }

    /// <summary>
    /// One campus's kiosk supervisor: a named person, or else the leader of the supervisor team at that campus.
    /// </summary>
    internal sealed class SupervisorDefinition
    {
        public string Campus { get; set; }

        public string Pin { get; set; }

        /// <summary>
        /// The Cast person, or null for the campus's supervisor team leader.
        /// </summary>
        public Guid? Person { get; set; }
    }

    /// <summary>
    /// How many generated children get a "Can check in" adult in another household, and how often that adult checks them in.
    /// </summary>
    internal sealed class CrowdCheckInRelationships
    {
        /// <summary>
        /// The percent of generated families with a child this young whose children get an adult who may check them in.
        /// </summary>
        public double CrowdChildPercent { get; set; }

        /// <summary>
        /// The oldest a child may be on the install day.
        /// </summary>
        public int ChildMaximumAge { get; set; }

        /// <summary>
        /// The youngest the adult may be on the install day.
        /// </summary>
        public int AdultMinimumAge { get; set; }

        /// <summary>
        /// The percent of the children's Sundays that adult checks them in.
        /// </summary>
        public double CheckedInByPercent { get; set; }
    }
}

namespace RockSampleData.Loader.Loading
{
    internal sealed partial class CampusPlan
    {
        /// <summary>
        /// The second room of each large check-in group, by group key. Its first room is in <see cref="RoomIds"/>.
        /// </summary>
        public Dictionary<string, int> SecondRoomIds { get; } = new Dictionary<string, int>();
    }
}
