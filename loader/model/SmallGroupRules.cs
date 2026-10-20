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
        These classes mirror configuration/small-groups.json. Every small group rule,
        number, and Guid comes from that file; docs/small-groups.md explains them.
    */

    /// <summary>
    /// The contents of small-groups.json.
    /// </summary>
    internal sealed class SmallGroupRules
    {
        public SmallGroupTypeRules GroupType { get; set; }

        public SmallGroupRoles Roles { get; set; }

        public SmallGroupParents Parents { get; set; }

        public SmallGroupFinderSettings GroupFinder { get; set; }

        public List<SmallGroupTopic> Topics { get; set; } = new List<SmallGroupTopic>();

        public SmallGroupAttributes Attributes { get; set; }

        public List<GuidValue> InactiveReasons { get; set; } = new List<GuidValue>();

        public SmallGroupNaming Naming { get; set; }

        public SmallGroupSize Size { get; set; }

        public SmallGroupLeaderRules Leaders { get; set; }

        public SmallGroupMemberRules Members { get; set; }

        public SmallGroupHostRules Hosts { get; set; }

        public SmallGroupScheduleRules Schedule { get; set; }

        /// <summary>
        /// Weights by meeting style value. The keys match <see cref="SmallGroupAttributes.MeetingStyle"/> values.
        /// </summary>
        public Dictionary<string, double> MeetingStyle { get; set; } = new Dictionary<string, double>();

        public int ChildCareProvidedPercent { get; set; }

        public bool ChildCareFollowsYoungKids { get; set; }

        public SmallGroupCalendar Calendar { get; set; }

        public SmallGroupAttendanceRules Attendance { get; set; }

        public SmallGroupMembershipRules Membership { get; set; }

        public SmallGroupParticipation Participation { get; set; }

        public SmallGroupTestCases TestCases { get; set; }

        public SmallGroupCastRules Cast { get; set; }

        /// <summary>
        /// The Meeting Style value for groups that meet in a home (the first configured value).
        /// </summary>
        public string InPersonStyle => Attributes.MeetingStyle.Values[0];

        /// <summary>
        /// The Meeting Style value for groups that meet online (the second configured value).
        /// </summary>
        public string OnlineStyle => Attributes.MeetingStyle.Values[1];
    }

    internal sealed class SmallGroupTypeRules
    {
        public Guid Guid { get; set; }

        public Guid SectionGuid { get; set; }

        /// <summary>
        /// "none", "hard", or "soft".
        /// </summary>
        public string CapacityRule { get; set; }

        public GuidValue Purpose { get; set; }
    }

    internal sealed class SmallGroupRoles
    {
        public SmallGroupRoleDefinition Leader { get; set; }

        public SmallGroupRoleDefinition Member { get; set; }

        public SmallGroupRoleDefinition Host { get; set; }
    }

    internal sealed class SmallGroupRoleDefinition
    {
        public Guid Guid { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }
    }

    internal sealed class SmallGroupParents
    {
        public NamedGuid Root { get; set; }

        /// <summary>
        /// The parent group Guid for each campus key.
        /// </summary>
        public Dictionary<string, Guid> Campuses { get; set; } = new Dictionary<string, Guid>();

        public string CampusNameFormat { get; set; }

        /// <summary>
        /// Rock's starter small group sections, which the sample church's parents replace.
        /// </summary>
        public List<NamedGuid> RemoveStarterSections { get; set; } = new List<NamedGuid>();
    }

    internal sealed class SmallGroupTopic
    {
        public Guid Guid { get; set; }

        public string Value { get; set; }

        public List<string> Descriptions { get; set; } = new List<string>();

        /// <summary>
        /// Who the topic suits. A group gets this topic only when its members meet every rule set here.
        /// </summary>
        public SmallGroupTopicFit Fit { get; set; }
    }

    internal sealed class SmallGroupTopicFit
    {
        public double? MenPercentMin { get; set; }

        public double? MarriedPercentMin { get; set; }

        public double? ParentsPercentMin { get; set; }

        public int? MedianAgeMax { get; set; }

        /// <summary>
        /// True when the people (and how many of them are parents of children under 18) meet every rule.
        /// </summary>
        public bool IsMetBy( List<CastPerson> people, int parents )
        {
            if ( people.Count == 0 )
            {
                return false;
            }

            double Percent( int count ) => count * 100.0 / people.Count;
            var ages = people.Where( p => p.Birth != null ).Select( p => p.Birth.Age ).OrderBy( a => a ).ToList();
            var median = ages.Count == 0 ? int.MaxValue : ages[ages.Count / 2];

            return ( MenPercentMin == null || Percent( people.Count( p => p.Gender == "male" ) ) >= MenPercentMin )
                && ( MarriedPercentMin == null || Percent( people.Count( p => string.Equals( p.MaritalStatus, "married", StringComparison.OrdinalIgnoreCase ) ) ) >= MarriedPercentMin )
                && ( ParentsPercentMin == null || Percent( parents ) >= ParentsPercentMin )
                && ( MedianAgeMax == null || median <= MedianAgeMax );
        }
    }

    internal sealed class SmallGroupAttributes
    {
        public SmallGroupAttribute ChildCareProvided { get; set; }

        public SmallGroupAttribute MeetingStyle { get; set; }

        public SmallGroupAttribute MeetingLink { get; set; }
    }

    internal sealed class SmallGroupAttribute
    {
        public Guid Guid { get; set; }

        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public List<string> Values { get; set; } = new List<string>();

        public string Format { get; set; }
    }

    /// <summary>
    /// A defined value: its Guid and its text.
    /// </summary>
    internal sealed class GuidValue
    {
        public Guid Guid { get; set; }

        public string Value { get; set; }
    }

    internal sealed class SmallGroupNaming
    {
        public string Format { get; set; }

        public string DuplicateFormat { get; set; }
    }

    internal sealed class SmallGroupSize
    {
        public int Target { get; set; }

        public int Min { get; set; }

        public int Max { get; set; }

        public int Capacity { get; set; }
    }

    /// <summary>
    /// Rock's Group Finder page block, and the setting that shows each group's photo on its card.
    /// </summary>
    internal sealed class SmallGroupFinderSettings
    {
        public Guid BlockGuid { get; set; }

        public Guid ShowImageAttributeGuid { get; set; }
    }

    internal sealed class SmallGroupLeaderRules
    {
        public string Gender { get; set; }

        public int MinimumAge { get; set; }

        public List<string> Statuses { get; set; } = new List<string>();

        /// <summary>
        /// Statuses a campus with too few possible leaders also takes leaders from, in this order.
        /// </summary>
        public List<string> FallbackStatuses { get; set; } = new List<string>();

        public string BecomeStatus { get; set; }
    }

    internal sealed class SmallGroupMemberRules
    {
        public int MinimumAge { get; set; }
    }

    internal sealed class SmallGroupHostRules
    {
        public int SpouseHostsPercent { get; set; }
    }

    internal sealed class SmallGroupScheduleRules
    {
        public string Frequency { get; set; }

        /// <summary>
        /// Weights by day of week name.
        /// </summary>
        public Dictionary<string, double> Days { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Weights by start time (HH:mm).
        /// </summary>
        public Dictionary<string, double> Times { get; set; } = new Dictionary<string, double>();
    }

    internal sealed class SmallGroupCalendar
    {
        public List<int> MeetingMonths { get; set; } = new List<int>();

        public int LastDecemberMeetingDay { get; set; }

        public int SummerMeetingPercent { get; set; }

        /// <summary>
        /// Holiday keys from <see cref="Generation.SundayCalendar"/> whose weeks are marked "did not meet".
        /// </summary>
        public List<string> SkippedWeeks { get; set; } = new List<string>();

        /// <summary>
        /// Join weights by month number, plus "other" shared by every month not listed.
        /// </summary>
        public Dictionary<string, double> JoinMonthWeights { get; set; } = new Dictionary<string, double>();
    }

    internal sealed class SmallGroupAttendanceRules
    {
        public int LeaderPercent { get; set; }

        public int HostPercent { get; set; }

        public IntRange MemberPercent { get; set; }

        public int SpouseTogetherPercent { get; set; }

        public int RecordsEveryWeekPercent { get; set; }

        public int SpottyRecordsPercent { get; set; }
    }

    internal sealed class SmallGroupMembershipRules
    {
        public IntRange StartedYearsAgo { get; set; }

        public int LeavePercentPerYear { get; set; }

        public int ArchivedPercentOfLeavers { get; set; }

        public IntRange PendingRequests { get; set; }
    }

    internal sealed class IntRange
    {
        public int Min { get; set; }

        public int Max { get; set; }
    }

    internal sealed class SmallGroupParticipation
    {
        /// <summary>
        /// Percent of eligible Cast adults in an active group, by connection status.
        /// </summary>
        public Dictionary<string, double> Cast { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// The profile path holding the share of active generated adults in a group.
        /// </summary>
        public string GeneratedProfilePath { get; set; }
    }

    internal sealed class SmallGroupTestCases
    {
        public TestCaseCounts MainCampus { get; set; }

        public TestCaseCounts EachOtherCampus { get; set; }
    }

    /// <summary>
    /// How many groups of each test case one campus gets.
    /// </summary>
    internal sealed class TestCaseCounts
    {
        public static readonly TestCaseCounts None = new TestCaseCounts();

        public int Full { get; set; }

        public int Closed { get; set; }

        public int Pending { get; set; }

        public int Inactive { get; set; }

        public int Archived { get; set; }

        /// <summary>
        /// Test cases that are active groups (full, closed, and pending).
        /// </summary>
        public int ActiveCount => Full + Closed + Pending;
    }

    internal sealed class SmallGroupCastRules
    {
        /// <summary>
        /// Legacy group key to its pin.
        /// </summary>
        public Dictionary<string, SmallGroupPin> Pins { get; set; } = new Dictionary<string, SmallGroupPin>();

        public List<string> NotInPattern { get; set; } = new List<string>();
    }

    /// <summary>
    /// A legacy group's fixed leader ("household/person" keys) or host household.
    /// </summary>
    internal sealed class SmallGroupPin
    {
        public string Leader { get; set; }

        public string HostHousehold { get; set; }
    }
}
