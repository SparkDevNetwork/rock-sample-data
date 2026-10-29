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

        public List<RegistrationCategoryDefinition> Categories { get; set; } = new List<RegistrationCategoryDefinition>();

        public List<RegistrationGroupTypeDefinition> GroupTypes { get; set; } = new List<RegistrationGroupTypeDefinition>();

        /// <summary>
        /// Registration attributes every template asks once per registration.
        /// </summary>
        public List<RegistrationAttributeDefinition> RegistrationAttributes { get; set; } = new List<RegistrationAttributeDefinition>();

        public List<RegistrationTemplateDefinition> Templates { get; set; } = new List<RegistrationTemplateDefinition>();

        public List<RegistrationInstanceRule> Instances { get; set; } = new List<RegistrationInstanceRule>();
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
        /// True when a registration can cost money, so the template needs a gateway.
        /// </summary>
        public bool IsPaid => Cost > 0 || SetCostOnInstance || Fees.Count > 0;
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
