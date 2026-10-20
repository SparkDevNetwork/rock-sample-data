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
    /// The contents of serving.json: the serving team parents, the rooms teams serve in, and the teams.
    /// </summary>
    internal sealed class ServingConfiguration
    {
        public GuidReference GroupType { get; set; }

        public ServingRoles Roles { get; set; }

        public ServingParents Parents { get; set; }

        /// <summary>
        /// Rooms added to each campus building beside the check-in rooms.
        /// </summary>
        public List<string> Rooms { get; set; } = new List<string>();

        public Guid BackgroundCheckRequirementTypeGuid { get; set; }

        public VolunteerRules Volunteers { get; set; }

        public SchedulingRules Scheduling { get; set; }

        public List<ServingTeamDefinition> Teams { get; set; } = new List<ServingTeamDefinition>();
    }

    /// <summary>
    /// Who serves and how: the generated share, eligibility, rotations, and preferences.
    /// </summary>
    internal sealed class VolunteerRules
    {
        public string GeneratedProfilePath { get; set; }

        public List<string> Statuses { get; set; } = new List<string>();

        /// <summary>
        /// Statuses a campus draws on, in order, when it cannot give every team its minimum size.
        /// </summary>
        public List<string> FallbackStatuses { get; set; } = new List<string>();

        /// <summary>
        /// The status a volunteer drawn from the fallback statuses becomes.
        /// </summary>
        public string BecomeMemberStatus { get; set; }

        public int MinimumAge { get; set; }

        public int MinimumTeamSize { get; set; }

        public int SpouseSameTeamPercent { get; set; }

        public string BecomeLeaderStatus { get; set; }

        public NumberRange JoinedYearsAgo { get; set; }

        /// <summary>
        /// Weights by Rock schedule template name.
        /// </summary>
        public Dictionary<string, double> Rotations { get; set; } = new Dictionary<string, double>();

        public int PreferredServicePercent { get; set; }

        public int PreferredPositionPercent { get; set; }
    }

    internal sealed class NumberRange
    {
        public double Min { get; set; }

        public double Max { get; set; }
    }

    /// <summary>
    /// How serving history and the weeks ahead are generated.
    /// </summary>
    internal sealed class SchedulingRules
    {
        public int PastWeeks { get; set; }

        public int FutureWeeks { get; set; }

        /// <summary>
        /// When the family did not come: declined, noResponse, or confirmedNoShow weights.
        /// </summary>
        public Dictionary<string, double> WhenAbsent { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// The weeks ahead: confirmed, pending, or declined weights.
        /// </summary>
        public Dictionary<string, double> Future { get; set; } = new Dictionary<string, double>();

        public int BlackoutPercent { get; set; }

        public List<string> BlackoutTitles { get; set; } = new List<string>();
    }

    /// <summary>
    /// One Cast serving team roster from cast/groups/serving-*.json.
    /// </summary>
    internal sealed class CastServingTeam
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The team key in serving.json.
        /// </summary>
        public string Team { get; set; }

        public List<CastServingMember> Members { get; set; } = new List<CastServingMember>();
    }

    internal sealed class CastServingMember
    {
        public Guid Guid { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// Leader or Member.
        /// </summary>
        public string Role { get; set; }
    }

    internal sealed class GuidReference
    {
        public Guid Guid { get; set; }
    }

    internal sealed class ServingRoles
    {
        public GuidReference Leader { get; set; }

        public GuidReference Member { get; set; }
    }

    internal sealed class ServingParents
    {
        public NamedGuid Root { get; set; }

        /// <summary>
        /// Each campus's parent group Guid, by campus key.
        /// </summary>
        public Dictionary<string, Guid> Campuses { get; set; } = new Dictionary<string, Guid>();

        public string CampusNameFormat { get; set; }
    }

    /// <summary>
    /// One serving team. Every campus in the dataset gets one.
    /// </summary>
    internal sealed class ServingTeamDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The youngest a generated volunteer can be on this team, when above the general minimum.
        /// </summary>
        public int? MinimumAge { get; set; }

        /// <summary>
        /// "all" for every service at the campus, or "student" for the student service.
        /// </summary>
        public string Services { get; set; }

        public bool RequiresBackgroundCheck { get; set; }

        public List<ServingPosition> Positions { get; set; } = new List<ServingPosition>();
    }

    /// <summary>
    /// A room a team serves in, with the minimum, desired, and maximum people at each service.
    /// Names either a serving room or a check-in group key (meaning that group's room).
    /// </summary>
    internal sealed class ServingPosition
    {
        public string Room { get; set; }

        public string CheckInGroup { get; set; }

        public int[] Capacity { get; set; }
    }

    /// <summary>
    /// The contents of connections.json: the Involvement connection type and its serving opportunities.
    /// </summary>
    internal sealed class ConnectionsConfiguration
    {
        public ConnectionTypeDefinition ConnectionType { get; set; }

        public List<ConnectionRequestStatusDefinition> RequestStatuses { get; set; } = new List<ConnectionRequestStatusDefinition>();

        public List<NamedGuid> ActivityTypes { get; set; } = new List<NamedGuid>();

        public ConnectionPlacement Placement { get; set; }

        /// <summary>
        /// New option lists for Rock's opportunity attributes, by attribute key, in Rock's "1^Text, 2^Text" form.
        /// </summary>
        public Dictionary<string, string> AttributeOptions { get; set; } = new Dictionary<string, string>();

        public List<ConnectionOpportunityDefinition> Opportunities { get; set; } = new List<ConnectionOpportunityDefinition>();
    }

    internal sealed class ConnectionTypeDefinition
    {
        public Guid Guid { get; set; }

        public string Name { get; set; }

        public string IconCssClass { get; set; }

        public string Description { get; set; }

        public bool RequiresPlacementGroupToConnect { get; set; }
    }

    /// <summary>
    /// A connection request status, in board order.
    /// </summary>
    internal sealed class ConnectionRequestStatusDefinition
    {
        public Guid Guid { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public bool IsDefault { get; set; }

        public bool IsCritical { get; set; }

        public string HighlightColor { get; set; }
    }

    /// <summary>
    /// The team role and member status a connected request places someone with.
    /// </summary>
    internal sealed class ConnectionPlacement
    {
        /// <summary>
        /// "leader" or "member", a key of <see cref="ServingRoles"/>.
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// Rock's group member status name: Active, Inactive, or Pending.
        /// </summary>
        public string Status { get; set; }
    }

    /// <summary>
    /// One serving opportunity. Its Name is its team's name.
    /// </summary>
    internal sealed class ConnectionOpportunityDefinition
    {
        public Guid Guid { get; set; }

        /// <summary>
        /// The key of the team in serving.json that this opportunity places people on.
        /// </summary>
        public string Team { get; set; }

        public string PublicName { get; set; }

        public string IconCssClass { get; set; }

        /// <summary>
        /// Values for Rock's opportunity attributes (the Serve page's search filters), by attribute key.
        /// </summary>
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();

        public string Summary { get; set; }

        public string Description { get; set; }
    }
}
