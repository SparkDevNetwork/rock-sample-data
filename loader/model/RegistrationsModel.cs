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
    /// <summary>
    /// The contents of registrations.json: template categories, group types, templates, and the rules that make each
    /// year's registration instances (docs/registrations.md).
    /// </summary>
    internal sealed class RegistrationsConfiguration
    {
        /// <summary>
        /// Rock's RSR - Event Registration Administration role, given Edit and Administrate on each category.
        /// </summary>
        public Guid AdminRole { get; set; }

        /// <summary>
        /// The financial gateway paid templates use.
        /// </summary>
        public Guid Gateway { get; set; }

        /// <summary>
        /// The accounts.json key of the account every instance posts to.
        /// </summary>
        public string Account { get; set; }

        public string BatchPrefix { get; set; }

        /// <summary>
        /// The Rock template whose emails and success text every template copies.
        /// </summary>
        public Guid EmailsFrom { get; set; }

        /// <summary>
        /// Rock's Digitally Signed Documents binary file type, which holds each signed release's PDF.
        /// </summary>
        public Guid SignedDocumentFileType { get; set; }

        /// <summary>
        /// Rock's Electronic Signature Receipt system communication, which emails a signed release to its signer.
        /// </summary>
        public Guid? SignatureReceipt { get; set; }

        /// <summary>
        /// The releases signed with Rock's own electronic signature, which templates name by key.
        /// </summary>
        public List<SignatureDocumentDefinition> SignatureDocuments { get; set; } = new List<SignatureDocumentDefinition>();

        public List<RegistrationCategoryDefinition> Categories { get; set; } = new List<RegistrationCategoryDefinition>();

        public List<RegistrationGroupTypeDefinition> GroupTypes { get; set; } = new List<RegistrationGroupTypeDefinition>();

        /// <summary>
        /// Registration attributes every template asks once per registration.
        /// </summary>
        public List<RegistrationAttributeDefinition> RegistrationAttributes { get; set; } = new List<RegistrationAttributeDefinition>();

        public List<RegistrationTemplateDefinition> Templates { get; set; } = new List<RegistrationTemplateDefinition>();

        public List<RegistrationInstanceRule> Instances { get; set; } = new List<RegistrationInstanceRule>();

        /// <summary>
        /// Person data views about registrations, in the shape group-syncs.json uses. A template's eligibility may name one.
        /// </summary>
        public List<DataViewDefinition> DataViews { get; set; } = new List<DataViewDefinition>();
    }

    internal sealed class RegistrationCategoryDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string IconCssClass { get; set; }
    }

    /// <summary>
    /// A group type for linked groups or placement groups, found by its Guid first.
    /// </summary>
    internal sealed class RegistrationGroupTypeDefinition
    {
        public string Key { get; set; }

        /// <summary>
        /// A Guid Rock reserves for this type, or null for one made from the key.
        /// </summary>
        public Guid? Guid { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public string GroupTerm { get; set; }

        public string MemberTerm { get; set; }

        public string IconCssClass { get; set; }

        public List<RegistrationGroupRoleDefinition> Roles { get; set; } = new List<RegistrationGroupRoleDefinition>();
    }

    internal sealed class RegistrationGroupRoleDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public bool IsLeader { get; set; }

        /// <summary>
        /// The role a new member gets.
        /// </summary>
        public bool IsDefault { get; set; }
    }

    /// <summary>
    /// One registration template: what a registration costs, who may register, and what it asks.
    /// </summary>
    internal sealed class RegistrationTemplateDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Category { get; set; }

        public string Description { get; set; }

        public string RegistrantTerm { get; set; } = "Registrant";

        public decimal Cost { get; set; }

        /// <summary>
        /// The cost and minimum payment come from each instance instead of the template.
        /// </summary>
        public bool SetCostOnInstance { get; set; }

        public decimal? MinimumInitialPayment { get; set; }

        /// <summary>
        /// Registrants on one registration. One means a registration has a single registrant.
        /// </summary>
        public int MaxRegistrants { get; set; } = 1;

        public RegistrantsSameFamily RegistrantsSameFamily { get; set; } = RegistrantsSameFamily.Ask;

        public bool ShowCurrentFamilyMembers { get; set; }

        public RegistrarOption RegistrarOption { get; set; }

        /// <summary>
        /// The groupTypes key that linked groups must be, or null for a template with no linked group.
        /// </summary>
        public string GroupType { get; set; }

        /// <summary>
        /// The role key, in <see cref="GroupType"/>, registrants get in the linked group.
        /// </summary>
        public string GroupMemberRole { get; set; }

        public RegistrationGroupMemberStatus GroupMemberStatus { get; set; } = RegistrationGroupMemberStatus.Active;

        public bool WaitList { get; set; }

        /// <summary>
        /// The frequencies a payment plan may use. Empty means no payment plans.
        /// </summary>
        public List<PaymentPlanFrequency> PaymentPlan { get; set; } = new List<PaymentPlanFrequency>();

        /// <summary>
        /// The message shown when a registrar must pay in full or set up a plan, or null when either is optional.
        /// </summary>
        public string FullPaymentOrPlanRequired { get; set; }

        public RegistrationEligibility Eligibility { get; set; }

        public string Instructions { get; set; }

        public List<RegistrationFormDefinition> Forms { get; set; } = new List<RegistrationFormDefinition>();

        /// <summary>
        /// Registrant attributes, which form fields name by key.
        /// </summary>
        public List<RegistrationAttributeDefinition> Attributes { get; set; } = new List<RegistrationAttributeDefinition>();

        public List<RegistrationFeeDefinition> Fees { get; set; } = new List<RegistrationFeeDefinition>();

        public List<RegistrationDiscountDefinition> Discounts { get; set; } = new List<RegistrationDiscountDefinition>();

        public List<RegistrationPlacementDefinition> Placements { get; set; } = new List<RegistrationPlacementDefinition>();

        /// <summary>
        /// The signatureDocuments key of the release every registrant signs while registering, or null.
        /// </summary>
        public string SignatureDocument { get; set; }

        /// <summary>
        /// True when a registration can cost money, so the template needs a gateway.
        /// </summary>
        public bool IsPaid => Cost > 0 || SetCostOnInstance || Fees.Count > 0;
    }

    /// <summary>
    /// A release signed with Rock's own electronic signature: a SignatureDocumentTemplate with a typed signature.
    /// </summary>
    internal sealed class SignatureDocumentDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// What Rock calls the document on screen, such as "Release".
        /// </summary>
        public string DocumentTerm { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The document's Lava. It may use only the merge fields <see cref="Generation.SignatureLava"/> renders.
        /// </summary>
        public string LavaTemplate { get; set; }
    }

    /// <summary>
    /// Who may register, in the terms Rock's template screen uses. Every limit set must hold.
    /// </summary>
    internal sealed class RegistrationEligibility
    {
        /// <summary>
        /// The lowest and highest school grade, such as 7 and 12.
        /// </summary>
        public List<int> Grades { get; set; }

        /// <summary>
        /// The youngest and oldest age in years.
        /// </summary>
        public List<decimal> Ages { get; set; }

        public RegistrationGender? Gender { get; set; }

        /// <summary>
        /// The key of a dataViews entry whose people alone may register (Rock's EligibilityDataViewGuid), or null.
        /// </summary>
        public string DataView { get; set; }
    }

    internal sealed class RegistrationFormDefinition
    {
        public string Name { get; set; }

        public List<RegistrationFieldDefinition> Fields { get; set; } = new List<RegistrationFieldDefinition>();
    }

    /// <summary>
    /// One form field. Exactly one of <see cref="Person"/>, <see cref="PersonAttribute"/>, and <see cref="Attribute"/> is set.
    /// </summary>
    internal sealed class RegistrationFieldDefinition
    {
        public RegistrationPersonField? Person { get; set; }

        /// <summary>
        /// A Rock person attribute's Guid. The answer is saved to the person.
        /// </summary>
        public Guid? PersonAttribute { get; set; }

        /// <summary>
        /// The key of one of the template's registrant attributes.
        /// </summary>
        public string Attribute { get; set; }

        public bool Required { get; set; }

        /// <summary>
        /// How the loader answers a person attribute field for each registrant, or null to leave it as it is.
        /// </summary>
        public RegistrationAttributeFill Fill { get; set; }
    }

    internal sealed class RegistrationAttributeDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public RegistrationAttributeFieldType FieldType { get; set; }

        /// <summary>
        /// The choices of a single-select attribute.
        /// </summary>
        public List<string> Values { get; set; } = new List<string>();

        public bool IsGridColumn { get; set; }

        /// <summary>
        /// How the loader answers the attribute for each registrant or registration, or null to leave it blank.
        /// </summary>
        public RegistrationAttributeFill Fill { get; set; }
    }

    /// <summary>
    /// How the loader answers an attribute: a weighted choice, a friend's name, or a parent's name or phone.
    /// </summary>
    internal sealed class RegistrationAttributeFill
    {
        public RegistrationAttributeFillKind Kind { get; set; }

        /// <summary>
        /// A choice's weights, in the order of the attribute's values.
        /// </summary>
        public List<double> Weights { get; set; } = new List<double>();

        /// <summary>
        /// How often a friend is named, or a person attribute is answered, from 0 to 100.
        /// </summary>
        public double Percent { get; set; }

        /// <summary>
        /// A person attribute's choices, which it has none of here.
        /// </summary>
        public List<string> Values { get; set; } = new List<string>();

        /// <summary>
        /// The key of a value in a Cast person's attributes. Rock's form shows a person attribute's current value, so a
        /// Cast person who has one keeps it.
        /// </summary>
        public string CastKey { get; set; }
    }

    /// <summary>
    /// A fee. One item is a single-choice fee; more items make a multiple-choice fee, such as sizes.
    /// </summary>
    internal sealed class RegistrationFeeDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public bool Required { get; set; }

        /// <summary>
        /// A registrant may choose a quantity, such as one childcare fee per child.
        /// </summary>
        public bool AllowMultiple { get; set; }

        public bool DiscountApplies { get; set; } = true;

        public bool HideWhenNoneRemaining { get; set; }

        public List<RegistrationFeeItemDefinition> Items { get; set; } = new List<RegistrationFeeItemDefinition>();
    }

    internal sealed class RegistrationFeeItemDefinition
    {
        public string Name { get; set; }

        public decimal Cost { get; set; }

        public int? MaximumUsageCount { get; set; }
    }

    /// <summary>
    /// A discount: an amount or a percent off each registrant, by code or applied on its own.
    /// </summary>
    internal sealed class RegistrationDiscountDefinition
    {
        public string Key { get; set; }

        public string Code { get; set; }

        public decimal Amount { get; set; }

        /// <summary>
        /// Percent off, from 0 to 100.
        /// </summary>
        public decimal Percent { get; set; }

        public int? MaxUsage { get; set; }

        public int? MinRegistrants { get; set; }

        public bool AutoApply { get; set; }

        /// <summary>
        /// An early-bird discount ends on this month and day, the last one on or before the template's latest event.
        /// </summary>
        public MonthDay EndsBefore { get; set; }
    }

    internal sealed class MonthDay
    {
        public int Month { get; set; }

        public int Day { get; set; }
    }

    internal sealed class RegistrationPlacementDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string GroupType { get; set; }

        public string IconCssClass { get; set; }

        /// <summary>
        /// A registrant may be placed in more than one of these groups, such as two camp activities.
        /// </summary>
        public bool AllowMultiple { get; set; }
    }

    /// <summary>
    /// The rule that makes a template's instances from a calendar event: which occurrence, which dates, and which groups.
    /// </summary>
    internal sealed class RegistrationInstanceRule
    {
        public string Key { get; set; }

        public string Template { get; set; }

        /// <summary>
        /// The calendar.json event key.
        /// </summary>
        public string Event { get; set; }

        /// <summary>
        /// The occurrences this rule makes an instance for at each campus.
        /// </summary>
        public List<RegistrationOccurrencePick> Occurrences { get; set; } = new List<RegistrationOccurrencePick>();

        /// <summary>
        /// The instance name. May use {year}, {month}, and {campus}.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The name on the event page, or null for <see cref="Name"/>.
        /// </summary>
        public string PublicName { get; set; }

        /// <summary>
        /// The linkage's URL slug. May use {year}, {month}, and {campus}.
        /// </summary>
        public string Slug { get; set; }

        public RegistrationOpens Opens { get; set; }

        public int ClosesDaysBefore { get; set; }

        /// <summary>
        /// When the balance is due, in days before the event, or null for no deadline.
        /// </summary>
        public int? PaymentDaysBefore { get; set; }

        /// <summary>
        /// When Rock sends the reminder email, in days before the event, or null for none.
        /// </summary>
        public int? ReminderDaysBefore { get; set; }

        /// <summary>
        /// The registrant limit by dataset key. A dataset not listed has no limit.
        /// </summary>
        public Dictionary<string, int> MaxAttendees { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// The cost and minimum payment, for a template that sets them on the instance.
        /// </summary>
        public decimal? Cost { get; set; }

        public decimal? MinimumInitialPayment { get; set; }

        /// <summary>
        /// A calendar.json contact key.
        /// </summary>
        public string Contact { get; set; }

        public string Details { get; set; }

        public string Instructions { get; set; }

        public RegistrationGroupRule LinkedGroup { get; set; }

        public List<RegistrationPlacementGroupRule> PlacementGroups { get; set; } = new List<RegistrationPlacementGroupRule>();

        /// <summary>
        /// Who registers and how they pay, or null for an instance nobody has registered for yet.
        /// </summary>
        public RegistrantRule Registrants { get; set; }
    }

    /// <summary>
    /// Who registers for an instance, when, and how they pay. Designed targets, not calibration.
    /// </summary>
    internal sealed class RegistrantRule
    {
        /// <summary>
        /// Exactly one of <see cref="Students"/>, <see cref="Kids"/>, <see cref="Couples"/>, <see cref="Adults"/>, and
        /// <see cref="Volunteers"/> says who registers.
        /// </summary>
        public StudentRegistrantRule Students { get; set; }

        public KidRegistrantRule Kids { get; set; }

        public CoupleRegistrantRule Couples { get; set; }

        public AdultRegistrantRule Adults { get; set; }

        public VolunteerRegistrantRule Volunteers { get; set; }

        /// <summary>
        /// Which audience registers: the one rule of the five that is set.
        /// </summary>
        [JsonIgnore]
        public RegistrantAudience Audience
        {
            get
            {
                var set = new (object Rule, RegistrantAudience Audience)[] { (Students, RegistrantAudience.Students), (Kids, RegistrantAudience.Kids), (Couples, RegistrantAudience.Couples), (Adults, RegistrantAudience.Adults), (Volunteers, RegistrantAudience.Volunteers) }
                    .Where( a => a.Rule != null ).ToList();
                return set.Count == 1 ? set[0].Audience : throw new InvalidDataException( "configuration/registrations.json: a registrants rule needs exactly one of students, kids, couples, adults, and volunteers." );
            }
        }

        /// <summary>
        /// Current Student Leaders register on their own with a discount code.
        /// </summary>
        public StudentLeaderRule StudentLeaders { get; set; }

        /// <summary>
        /// Days in the window when registrations bunch up. The rest spread evenly across the window.
        /// </summary>
        public List<RegistrationBurst> Timing { get; set; } = new List<RegistrationBurst>();

        /// <summary>
        /// The share of registrations that pay each way.
        /// </summary>
        public Dictionary<RegistrationPayment, double> Payments { get; set; } = new Dictionary<RegistrationPayment, double>();

        /// <summary>
        /// The share of payment plans at each frequency.
        /// </summary>
        public Dictionary<PaymentPlanFrequency, double> PlanFrequencies { get; set; } = new Dictionary<PaymentPlanFrequency, double>();

        /// <summary>
        /// The share of registrants who choose each optional fee, by fee key.
        /// </summary>
        public Dictionary<string, double> Fees { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// The weights of a fee's items, in item order, by fee key. A fee not listed takes a size by grade, or its first item.
        /// </summary>
        public Dictionary<string, List<double>> FeeWeights { get; set; } = new Dictionary<string, List<double>>();

        /// <summary>
        /// Fees charged once per child in the family younger than this age on the event day, by fee key, such as
        /// childcare. The fee's share in <see cref="Fees"/> is the share of registrations that add it. It goes on the
        /// registration's first registrant, with a quantity of the children, and only when the family has such a child.
        /// </summary>
        public Dictionary<string, int> FeePerChild { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Discount codes some registrations use. A code is used only when no automatic discount is valid that day,
        /// since Rock shows that one already.
        /// </summary>
        public List<DiscountCodeRule> Codes { get; set; } = new List<DiscountCodeRule>();

        public RegistrantPlacementRule Placements { get; set; }

        /// <summary>
        /// Once the instance is full, the share of later registrations that join the wait list. The rest do not
        /// register. Null means all of them join.
        /// </summary>
        public double? WaitListPercent { get; set; }

        /// <summary>
        /// A factor on the audience's shares by dataset key, such as 0.2 for Full 7, whose large Crowd would otherwise
        /// fill a class many times over. A dataset not listed uses the shares as they are. Students use their steps' rates.
        /// </summary>
        public Dictionary<string, double> Scale { get; set; } = new Dictionary<string, double>();
    }

    /// <summary>
    /// Which students register: the share of a Student Path event step, or a share of the students who attend.
    /// </summary>
    internal sealed class StudentRegistrantRule
    {
        /// <summary>
        /// A steps.json event step type. Its percentOfStudents is the share who register, and an instance whose
        /// event has ended has exactly the people with that step at that event.
        /// </summary>
        public string StepType { get; set; }

        /// <summary>
        /// The share of attending students who register, when there is no step type.
        /// </summary>
        public double? Percent { get; set; }

        /// <summary>
        /// The gender an instance is for, such as camp's girls' instance.
        /// </summary>
        public RegistrationGender? Gender { get; set; }
    }

    /// <summary>
    /// Which kids register: a share of families by the family's connection status, with the template's ages. A parent
    /// registers the siblings together.
    /// </summary>
    internal sealed class KidRegistrantRule
    {
        /// <summary>
        /// The share of families that register, from 0 to 100, by the family's connection status (its most involved adult's).
        /// </summary>
        public Dictionary<string, double> Statuses { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Only families with a married couple register, such as Parents' Night Out.
        /// </summary>
        public bool MarriedParents { get; set; }

        /// <summary>
        /// The highest grade a kid may be in on the event day, or null for any, such as VBS's grade 5.
        /// </summary>
        public int? MaxGrade { get; set; }
    }

    /// <summary>
    /// Which married couples register together on one registration: a share by the couple's connection status.
    /// </summary>
    internal sealed class CoupleRegistrantRule
    {
        public Dictionary<string, double> Statuses { get; set; } = new Dictionary<string, double>();
    }

    /// <summary>
    /// Which adults register: a share by connection status, in an age range, and either on their own, with their
    /// spouse, or with friends from their small group.
    /// </summary>
    internal sealed class AdultRegistrantRule
    {
        public Dictionary<string, double> Statuses { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// The youngest and oldest age on the event day.
        /// </summary>
        public List<int> Ages { get; set; }

        /// <summary>
        /// The share of chosen married adults whose spouse registers with them on one registration, from 0 to 100.
        /// </summary>
        public double CouplePercent { get; set; }

        /// <summary>
        /// Friends from one small group who register together, or null.
        /// </summary>
        public SmallGroupFriendsRule SmallGroupFriends { get; set; }
    }

    /// <summary>
    /// Some active small groups send a few of their members together on one registration.
    /// </summary>
    internal sealed class SmallGroupFriendsRule
    {
        /// <summary>
        /// The share of small groups with at least two members who could go that send some, from 0 to 100.
        /// </summary>
        public double GroupPercent { get; set; }

        /// <summary>
        /// The fewest and most friends on one registration.
        /// </summary>
        public List<int> Size { get; set; }
    }

    /// <summary>
    /// Which volunteers register: shares of the people serving on a team and of those in a small group on the event
    /// day, and a share of the rest by connection status. Teens in a volunteer's family sometimes come along.
    /// </summary>
    internal sealed class VolunteerRegistrantRule
    {
        /// <summary>
        /// The share of serving team members who register, from 0 to 100.
        /// </summary>
        public double Servers { get; set; }

        /// <summary>
        /// The share of small group members who register, from 0 to 100.
        /// </summary>
        public double GroupMembers { get; set; }

        /// <summary>
        /// The share of other adults who register, by connection status.
        /// </summary>
        public Dictionary<string, double> Statuses { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// The share of a registering volunteer's teens who come along on the same registration, from 0 to 100.
        /// </summary>
        public double TeenPercent { get; set; }

        /// <summary>
        /// The youngest and oldest age of a teen who comes along.
        /// </summary>
        public List<int> TeenAges { get; set; }
    }

    /// <summary>
    /// A discount code a share of registrations use.
    /// </summary>
    internal sealed class DiscountCodeRule
    {
        /// <summary>
        /// The template's discount key.
        /// </summary>
        public string Discount { get; set; }

        /// <summary>
        /// The share of registrations that use it, from 0 to 100.
        /// </summary>
        public double Percent { get; set; }

        /// <summary>
        /// Only registrations with at least this many registrants use it, such as friends who register together.
        /// </summary>
        public int MinRegistrants { get; set; } = 1;

        /// <summary>
        /// How many of the code's uses stay unused, so it still works on install day.
        /// </summary>
        public int KeepUnused { get; set; }
    }

    internal sealed class StudentLeaderRule
    {
        /// <summary>
        /// The steps.json school-years step type of a current leader.
        /// </summary>
        public string StepType { get; set; }

        /// <summary>
        /// The template's discount key leaders use.
        /// </summary>
        public string Discount { get; set; }

        /// <summary>
        /// How many of the code's uses stay unused, so it still works on install day.
        /// </summary>
        public int KeepUnused { get; set; }
    }

    /// <summary>
    /// A share of the registrations in the days after or before a day of the window.
    /// </summary>
    internal sealed class RegistrationBurst
    {
        public RegistrationDay? After { get; set; }

        public RegistrationDay? Before { get; set; }

        public int Days { get; set; }

        public double Percent { get; set; }
    }

    internal sealed class RegistrantPlacementRule
    {
        /// <summary>
        /// How many groups of each placement every registrant is in once registration has closed.
        /// </summary>
        public Dictionary<string, int> Each { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Placements made while registration is still open, for the people who registered early.
        /// </summary>
        public EarlyPlacementRule Early { get; set; }

        /// <summary>
        /// Placements with one group per grade, by placement key: the grade of the first group, such as -1 for VBS's
        /// Pre-K crew. A kid with no grade yet joins the first group.
        /// </summary>
        public Dictionary<string, int> ByGrade { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Placements where everyone on one registration joins one group together, filling the groups evenly, such as
        /// a family on one Serve Day team.
        /// </summary>
        public List<string> Together { get; set; } = new List<string>();
    }

    internal sealed class EarlyPlacementRule
    {
        public List<string> Placements { get; set; } = new List<string>();

        /// <summary>
        /// People who registered within this many days of opening are placed.
        /// </summary>
        public int Days { get; set; }
    }

    /// <summary>
    /// Who registers for an instance.
    /// </summary>
    internal enum RegistrantAudience
    {
        Students,
        Kids,
        Couples,
        Adults,
        Volunteers
    }

    /// <summary>
    /// A day in a registration's window that registrations bunch around.
    /// </summary>
    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum RegistrationDay
    {
        [JsonStringEnumMemberName( "opens" )]
        Opens,

        [JsonStringEnumMemberName( "closes" )]
        Closes,

        [JsonStringEnumMemberName( "early-bird" )]
        EarlyBird,

        [JsonStringEnumMemberName( "payment-deadline" )]
        PaymentDeadline
    }

    /// <summary>
    /// How a registration pays.
    /// </summary>
    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum RegistrationPayment
    {
        /// <summary>
        /// Everything owed, when registering.
        /// </summary>
        [JsonStringEnumMemberName( "paid-in-full" )]
        PaidInFull,

        /// <summary>
        /// The minimum payment when registering, and the balance by the payment deadline.
        /// </summary>
        [JsonStringEnumMemberName( "deposit" )]
        Deposit,

        /// <summary>
        /// The minimum payment when registering, then a payment plan that ends by the payment deadline.
        /// </summary>
        [JsonStringEnumMemberName( "plan" )]
        Plan,

        /// <summary>
        /// Nothing when registering, on a template with no minimum payment. The whole balance is still due.
        /// </summary>
        [JsonStringEnumMemberName( "pay-later" )]
        PayLater
    }

    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum RegistrationAttributeFillKind
    {
        [JsonStringEnumMemberName( "choice" )]
        Choice,

        [JsonStringEnumMemberName( "friend" )]
        Friend,

        [JsonStringEnumMemberName( "contact-name" )]
        ContactName,

        [JsonStringEnumMemberName( "contact-phone" )]
        ContactPhone,

        /// <summary>
        /// Another registrant on the same registration, such as a roommate, or nothing on a registration of one.
        /// </summary>
        [JsonStringEnumMemberName( "same-registration" )]
        SameRegistration
    }

    /// <summary>
    /// When an instance opens.
    /// </summary>
    internal sealed class RegistrationOpens
    {
        public RegistrationOpenRule Kind { get; set; }

        /// <summary>
        /// Days before the event, for <see cref="RegistrationOpenRule.DaysBefore"/>.
        /// </summary>
        public int Days { get; set; }
    }

    /// <summary>
    /// A group an instance links to. Instances whose rules give the same key share one group.
    /// </summary>
    internal sealed class RegistrationGroupRule
    {
        public string Key { get; set; }

        public string Name { get; set; }
    }

    /// <summary>
    /// The placement groups of one placement: a list of names, or a numbered name ("Bus {n}") with a count per dataset.
    /// </summary>
    internal sealed class RegistrationPlacementGroupRule
    {
        public string Placement { get; set; }

        /// <summary>
        /// The key every group starts with; each group's key adds its number.
        /// </summary>
        public string Key { get; set; }

        public string Name { get; set; }

        public Dictionary<string, int> Count { get; set; } = new Dictionary<string, int>();

        public List<string> Names { get; set; }
    }

    /// <summary>
    /// Which occurrence an instance belongs to.
    /// </summary>
    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum RegistrationOccurrencePick
    {
        /// <summary>
        /// The first occurrence whose registration closes on or after the install day.
        /// </summary>
        Next,

        /// <summary>
        /// The occurrence before <see cref="Next"/>.
        /// </summary>
        Last
    }

    /// <summary>
    /// How an instance's opening day is counted.
    /// </summary>
    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum RegistrationOpenRule
    {
        /// <summary>
        /// The day after the previous occurrence's registration closed, so one is always open.
        /// </summary>
        Rolling,

        /// <summary>
        /// A number of days before the event.
        /// </summary>
        [JsonStringEnumMemberName( "days-before" )]
        DaysBefore
    }

    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum RegistrationAttributeFieldType
    {
        Text,

        [JsonStringEnumMemberName( "single-select" )]
        SingleSelect
    }

    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum PaymentPlanFrequency
    {
        Monthly,

        [JsonStringEnumMemberName( "twice-monthly" )]
        TwiceMonthly
    }

    /// <summary>
    /// Rock's RegistrantsSameFamily values.
    /// </summary>
    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum RegistrantsSameFamily
    {
        No = 0,
        Yes = 1,
        Ask = 2
    }

    /// <summary>
    /// Rock's RegistrarOption values.
    /// </summary>
    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum RegistrarOption
    {
        PromptForRegistrar = 0,
        PrefillFirstRegistrant = 1,
        UseFirstRegistrant = 2,
        UseLoggedInPerson = 3
    }

    /// <summary>
    /// Rock's GroupMemberStatus values a registrant may get.
    /// </summary>
    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum RegistrationGroupMemberStatus
    {
        Active = 1,
        Pending = 2
    }

    /// <summary>
    /// Rock's Gender values an eligibility rule may name.
    /// </summary>
    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum RegistrationGender
    {
        Male = 1,
        Female = 2
    }

    /// <summary>
    /// Rock's RegistrationPersonFieldType values. Race and ethnicity are left out (content standard 8).
    /// </summary>
    [JsonConverter( typeof( JsonStringEnumConverter ) )]
    internal enum RegistrationPersonField
    {
        FirstName = 0,
        LastName = 1,
        Campus = 2,
        Address = 3,
        Email = 4,
        Birthdate = 5,
        Gender = 6,
        MaritalStatus = 7,
        MobilePhone = 8,
        HomePhone = 9,
        WorkPhone = 10,
        Grade = 11,
        ConnectionStatus = 12,
        MiddleName = 13,
        AnniversaryDate = 14
    }
}
