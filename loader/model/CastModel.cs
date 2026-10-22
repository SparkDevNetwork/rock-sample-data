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

using System.Text.Json.Serialization;

namespace RockSampleData.Loader.Model
{
    /*
        These classes mirror cast/household.schema.json. Only the properties the
        loader uses so far are declared; the JSON reader ignores the rest.
    */

    /// <summary>
    /// One Cast household from cast/households.
    /// </summary>
    internal sealed class CastHousehold
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        public string Name { get; set; }

        public string Kind { get; set; }

        public string CampusSlot { get; set; }

        /// <summary>
        /// The household key this household is a duplicate record of. Duplicate records never join groups.
        /// </summary>
        public string DuplicateOf { get; set; }

        public CastAddress Address { get; set; }

        public CastAttendance Attendance { get; set; }

        public List<CastGiving> Giving { get; set; } = new List<CastGiving>();

        /// <summary>
        /// The household's pledges and gifts in the building campaign.
        /// </summary>
        public List<CastCampaign> Campaign { get; set; } = new List<CastCampaign>();

        /// <summary>
        /// True when a married couple gives separately: each spouse is their own giving unit.
        /// </summary>
        public bool AdultsGiveSeparately { get; set; }

        public List<CastPerson> Members { get; set; } = new List<CastPerson>();

        public List<CastSharedMember> SharedMembers { get; set; } = new List<CastSharedMember>();
    }

    /// <summary>
    /// A household's address.
    /// </summary>
    internal sealed class CastAddress
    {
        public string Type { get; set; }

        public string Street1 { get; set; }

        public string Street2 { get; set; }

        public string City { get; set; }

        public string State { get; set; }

        public string PostalCode { get; set; }

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }
    }

    /// <summary>
    /// How often a household attends on Sunday.
    /// </summary>
    internal sealed class CastAttendance
    {
        public string Persona { get; set; }

        public int PercentAttended { get; set; }

        public int? PercentPrimaryService { get; set; }

        public RelativeDate Started { get; set; }

        public RelativeDate Ended { get; set; }
    }

    /// <summary>
    /// One giver's pledge or campaign gifts in the building campaign.
    /// </summary>
    internal sealed class CastCampaign
    {
        public string Person { get; set; }

        /// <summary>
        /// The pledge total, or null for campaign gifts with no pledge.
        /// </summary>
        public decimal? Pledge { get; set; }

        /// <summary>
        /// The pledge frequency key (weekly, monthly, one-time, and so on), or null when the pledge has none.
        /// </summary>
        public string Frequency { get; set; }

        /// <summary>
        /// "quiet-phase", "commitment-sunday", or an object with weeksAfterCommitment.
        /// </summary>
        public System.Text.Json.JsonElement Pledged { get; set; }

        public string FollowThrough { get; set; }

        public List<CastCampaignGift> Gifts { get; set; } = new List<CastCampaignGift>();

        public string Expect { get; set; }

        /// <summary>
        /// Weeks after Commitment Sunday the pledge was made: 0 on Commitment Sunday, null in the quiet phase.
        /// </summary>
        [JsonIgnore]
        public int? PledgedWeeksAfterCommitment => Pledged.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Object => Pledged.GetProperty( "weeksAfterCommitment" ).GetInt32(),
            System.Text.Json.JsonValueKind.String when Pledged.GetString() == "commitment-sunday" => 0,
            _ => null
        };

        /// <summary>
        /// The pledge state a load must show: the expect value, or the one followThrough leads to.
        /// </summary>
        [JsonIgnore]
        public string ExpectedState => Expect ?? ( Pledge == null ? PledgeProgress.GiftsOnly : FollowThrough == "paid-in-full" ? PledgeProgress.Complete : FollowThrough );
    }

    /// <summary>
    /// One extra campaign gift.
    /// </summary>
    internal sealed class CastCampaignGift
    {
        public int WeeksAfterCommitment { get; set; }

        public decimal Amount { get; set; }

        public bool Stock { get; set; }
    }

    /// <summary>
    /// One giving pattern for one giver in the household.
    /// </summary>
    internal sealed class CastGiving
    {
        public string Persona { get; set; }

        /// <summary>
        /// Optional payment method (check, ach, card, or cash). Left out, the profile picks one.
        /// </summary>
        public string Currency { get; set; }

        /// <summary>
        /// Optional name signed on the check, when someone signs for the giver (a business).
        /// </summary>
        public string Signer { get; set; }

        public string Person { get; set; }

        public Dictionary<string, decimal> Funds { get; set; } = new Dictionary<string, decimal>();

        public int? PercentOfPeriodsGiven { get; set; }

        public CastGrowth Growth { get; set; }

        public int? SpecialGiftPercent { get; set; }

        public RelativeDate Started { get; set; }

        public RelativeDate Ended { get; set; }

        /// <summary>
        /// Optional: what Rock should show for this giver after a load (see <see cref="CastGivingExpect"/>).
        /// </summary>
        public CastGivingExpect Expect { get; set; }
    }

    /// <summary>
    /// What Rock should show for a Cast giver after a load. The builder checks these after every load.
    /// </summary>
    internal sealed class CastGivingExpect
    {
        /// <summary>
        /// Rock's gift frequency label: weekly, bi-weekly, monthly, quarterly, erratic, or undetermined.
        /// </summary>
        public string Frequency { get; set; }

        /// <summary>
        /// Rock's journey stage: non-giver, new, consistent, occasional, lapsed, or former.
        /// </summary>
        public string JourneyStage { get; set; }

        /// <summary>
        /// Alert type keys the giver must have at least one alert of.
        /// </summary>
        public List<string> Alerts { get; set; } = new List<string>();
    }

    /// <summary>
    /// A gift amount's growth: a percent increase every so many weeks.
    /// </summary>
    internal sealed class CastGrowth
    {
        public decimal Percent { get; set; }

        public int EveryWeeks { get; set; }
    }

    /// <summary>
    /// A person whose primary record lives in another household but who also belongs to this one.
    /// </summary>
    internal sealed class CastSharedMember
    {
        public Guid Guid { get; set; }

        public string Household { get; set; }
    }

    /// <summary>
    /// One Cast person.
    /// </summary>
    internal sealed class CastPerson
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        public string RecordType { get; set; }

        public string FirstName { get; set; }

        public string NickName { get; set; }

        public string MiddleName { get; set; }

        public string LastName { get; set; }

        public string Gender { get; set; }

        public string FamilyRole { get; set; }

        public CastBirth Birth { get; set; }

        public int? Grade { get; set; }

        public RelativeDate Graduation { get; set; }

        public string MaritalStatus { get; set; }

        public RelativeDate Anniversary { get; set; }

        public string RecordStatus { get; set; }

        public bool IsDeceased { get; set; }

        public string ConnectionStatus { get; set; }

        public string Email { get; set; }

        public bool EmailIsActive { get; set; } = true;

        public bool EmailDoNotEmail { get; set; }

        public CastPhones Phones { get; set; }

        /// <summary>
        /// This person's known relationships. Only the "business" type loads so far: the person is a
        /// contact of the business record it names.
        /// </summary>
        public List<CastRelationship> Relationships { get; set; } = new List<CastRelationship>();

        /// <summary>
        /// The security roles this person's story gives them, by the names in configuration/security-roles.json.
        /// The person joins each name's Rock role. "Prayer Team" is not a Rock role; it and "Prayer Administrators"
        /// also pick who prays for and approves prayer requests.
        /// </summary>
        public List<string> SecurityRoles { get; set; } = new List<string>();

        /// <summary>
        /// What Rock should show for this person after a load, or null. Checked after every load.
        /// </summary>
        public CastPersonExpect Expect { get; set; }

        /// <summary>
        /// The person attribute values the loader reads, or null.
        /// </summary>
        public CastPersonAttributes Attributes { get; set; }

        /// <summary>
        /// Classes a story needs, by step type key in configuration/steps.json. Steps that come from other
        /// records (groups, teams, giving, the Baptism Date) are never set here.
        /// </summary>
        public Dictionary<string, CastStepClass> Steps { get; set; } = new Dictionary<string, CastStepClass>();

        /// <summary>
        /// Requests to serve that tell this person's story.
        /// </summary>
        public List<CastConnectionRequest> ConnectionRequests { get; set; } = new List<CastConnectionRequest>();
    }

    /// <summary>
    /// One Cast request to serve. The loader adds its activity history.
    /// </summary>
    internal sealed class CastConnectionRequest
    {
        /// <summary>
        /// The team key in serving.json; its opportunity is in connections.json.
        /// </summary>
        public string Opportunity { get; set; }

        /// <summary>
        /// Active, Inactive, FutureFollowUp, or Connected.
        /// </summary>
        public string State { get; set; }

        public string Status { get; set; }

        public RelativeDate Created { get; set; }

        public RelativeDate Connected { get; set; }

        public int? FollowUpInDays { get; set; }

        /// <summary>
        /// Whether the team's leader at the person's campus is the connector.
        /// </summary>
        public bool Connector { get; set; }

        /// <summary>
        /// A background check result recorded a week after the request, or null.
        /// </summary>
        public string BackgroundCheck { get; set; }

        public string Comment { get; set; }
    }

    /// <summary>
    /// One known relationship from a Cast person to another record.
    /// </summary>
    internal sealed class CastRelationship
    {
        /// <summary>
        /// The relationship type, for example "business" (this person is a contact of that business).
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// The Guid of the other person or business.
        /// </summary>
        public Guid With { get; set; }
    }

    /// <summary>
    /// What Rock should show for a Cast person after a load.
    /// </summary>
    internal sealed class CastPersonExpect
    {
        public CastAchievementExpect Achievements { get; set; }

        public CastStepsExpect Steps { get; set; }
    }

    /// <summary>
    /// The person attribute values the loader reads. The file holds more; the JSON reader ignores the rest.
    /// </summary>
    internal sealed class CastPersonAttributes
    {
        public RelativeDate BaptismDate { get; set; }

        public RelativeDate FirstVisit { get; set; }
    }

    /// <summary>
    /// A class a Cast story needs: a set class, counted back from the current one, or any class the usual rules pick.
    /// </summary>
    internal sealed class CastStepClass
    {
        /// <summary>
        /// 0 is the class running on the install day (In Progress), 1 the one before it, and so on.
        /// </summary>
        public int? ClassesAgo { get; set; }

        /// <summary>
        /// True when the person took the class and the usual rules date it.
        /// </summary>
        public bool Taken { get; set; }
    }

    /// <summary>
    /// What a Cast person's steps must show, by step type key in configuration/steps.json.
    /// </summary>
    internal sealed class CastStepsExpect
    {
        /// <summary>
        /// Step types with at least one Complete step.
        /// </summary>
        public List<string> Complete { get; set; } = new List<string>();

        /// <summary>
        /// Step types with an In Progress step.
        /// </summary>
        public List<string> InProgress { get; set; } = new List<string>();

        /// <summary>
        /// True when the person must have a program completion, false when they must not.
        /// </summary>
        public bool? ProgramCompletion { get; set; }
    }

    /// <summary>
    /// Achievement type keys (from configuration/achievements.json) and what Rock should show for each.
    /// </summary>
    internal sealed class CastAchievementExpect
    {
        /// <summary>
        /// Types the person has at least one successful attempt of.
        /// </summary>
        public List<string> Earned { get; set; } = new List<string>();

        /// <summary>
        /// Types the person has an open attempt of: not closed and not yet successful, which check-in shows as in progress.
        /// </summary>
        public List<string> InProgress { get; set; } = new List<string>();

        /// <summary>
        /// Types the person has no successful attempt of.
        /// </summary>
        public List<string> NotEarned { get; set; } = new List<string>();
    }

    /// <summary>
    /// Age on the anchor date plus birthday.
    /// </summary>
    internal sealed class CastBirth
    {
        public int Age { get; set; }

        public int Month { get; set; }

        public int Day { get; set; }
    }

    /// <summary>
    /// A person's phone numbers (10 digits each).
    /// </summary>
    internal sealed class CastPhones
    {
        public string Home { get; set; }

        public string Mobile { get; set; }

        public string Work { get; set; }
    }

    /// <summary>
    /// A calendar date relative to the anchor: the year is the anchor year minus YearsAgo,
    /// or, when DaysAgo is set, that many days before the anchor.
    /// </summary>
    [JsonConverter( typeof( RelativeDateConverter ) )]
    internal sealed class RelativeDate
    {
        public int? DaysAgo { get; set; }

        public int YearsAgo { get; set; }

        public int Month { get; set; }

        public int Day { get; set; }

        public string Time { get; set; }
    }
}
