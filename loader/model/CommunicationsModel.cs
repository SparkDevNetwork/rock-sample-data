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
        These classes mirror configuration/communications.json. Property names match the JSON keys
        (case-insensitive).
    */

    /// <summary>
    /// The contents of configuration/communications.json: the church's phone numbers, senders,
    /// topics, branded template, and communication lists.
    /// </summary>
    internal sealed class CommunicationsConfiguration
    {
        public PhoneNumberConfiguration PhoneNumbers { get; set; } = new PhoneNumberConfiguration();

        public CommunicationSenders Senders { get; set; } = new CommunicationSenders();

        /// <summary>
        /// Rock's communication topic defined value Guids, by key.
        /// </summary>
        public Dictionary<string, Guid> Topics { get; set; } = new Dictionary<string, Guid>();

        public EmailTemplateDefinition Template { get; set; } = new EmailTemplateDefinition();

        public CommunicationListConfiguration Lists { get; set; } = new CommunicationListConfiguration();
    }

    /// <summary>
    /// The system phone numbers, and the campus pastors' personal numbers.
    /// </summary>
    internal sealed class PhoneNumberConfiguration
    {
        public List<SystemPhoneNumberDefinition> Numbers { get; set; } = new List<SystemPhoneNumberDefinition>();

        public CampusPastorNumbers CampusPastors { get; set; } = new CampusPastorNumbers();
    }

    /// <summary>
    /// One Rock system phone number.
    /// </summary>
    internal sealed class SystemPhoneNumberDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The number in E.164 form, such as +16235550150.
        /// </summary>
        public string Number { get; set; }

        public Guid Guid { get; set; }

        public int Order { get; set; }

        /// <summary>
        /// The Cast person whose personal number this is, or null for a shared line.
        /// </summary>
        public Guid? AssignedTo { get; set; }
    }

    /// <summary>
    /// A personal number for each campus pastor, in the datasets listed.
    /// </summary>
    internal sealed class CampusPastorNumbers
    {
        public List<string> Datasets { get; set; } = new List<string>();

        /// <summary>
        /// Each campus's number and Guid, by campus key.
        /// </summary>
        public Dictionary<string, CampusPastorNumber> Numbers { get; set; } = new Dictionary<string, CampusPastorNumber>();
    }

    internal sealed class CampusPastorNumber
    {
        public string Number { get; set; }

        public Guid Guid { get; set; }
    }

    /// <summary>
    /// Who sends each kind of message, and who approves sends.
    /// </summary>
    internal sealed class CommunicationSenders
    {
        public CommunicationSender Church { get; set; }

        public CommunicationSender Pastor { get; set; }

        public CommunicationSender Students { get; set; }

        public CommunicationSender Texts { get; set; }

        /// <summary>
        /// The Cast person who approves sends that need approval.
        /// </summary>
        public Guid Approver { get; set; }
    }

    internal sealed class CommunicationSender
    {
        public string FromName { get; set; }

        public string FromEmail { get; set; }

        /// <summary>
        /// The Cast person who sends in Rock.
        /// </summary>
        public Guid Sender { get; set; }

        /// <summary>
        /// The key of the phone number texts go out on, or null.
        /// </summary>
        public string PhoneNumber { get; set; }
    }

    /// <summary>
    /// The church's branded email template.
    /// </summary>
    internal sealed class EmailTemplateDefinition
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// Rock's template category.
        /// </summary>
        public Guid CategoryGuid { get; set; }

        /// <summary>
        /// The template's HTML, as a package path.
        /// </summary>
        public string Html { get; set; }

        /// <summary>
        /// The logo PNG, as a package path.
        /// </summary>
        public string Logo { get; set; }

        public Guid LogoGuid { get; set; }

        /// <summary>
        /// The template's Lava fields and their defaults, which Rock's template editor shows.
        /// </summary>
        public Dictionary<string, string> LavaFields { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// The communication lists and the rules for who is on them.
    /// </summary>
    internal sealed class CommunicationListConfiguration
    {
        /// <summary>
        /// Rock's Public communication list category, which Rock's email preference page shows.
        /// </summary>
        public Guid PublicCategoryGuid { get; set; }

        public List<CommunicationListDefinition> Lists { get; set; } = new List<CommunicationListDefinition>();

        public ListMembershipRules Membership { get; set; } = new ListMembershipRules();
    }

    /// <summary>
    /// One communication list, or one per campus.
    /// </summary>
    internal sealed class CommunicationListDefinition
    {
        public string Key { get; set; }

        /// <summary>
        /// The list's name. {campus} becomes the campus name for a list per campus.
        /// </summary>
        public string Name { get; set; }

        public string PublicName { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// True for one list per campus.
        /// </summary>
        public bool Campus { get; set; }

        public bool IsPublic { get; set; }

        /// <summary>
        /// The membership rule: newsletter, men, women, parents-of-kids, students, volunteers, prayer-team, or small-group-leaders.
        /// </summary>
        public string Members { get; set; }

        /// <summary>
        /// The members' preference on the list: email, or sms for those who can text.
        /// </summary>
        public string Preference { get; set; } = "email";

        public Guid? Guid { get; set; }

        /// <summary>
        /// Each campus's list Guid, by campus key, for a list per campus.
        /// </summary>
        public Dictionary<string, Guid> Guids { get; set; } = new Dictionary<string, Guid>();
    }

    /// <summary>
    /// The designed rules for who is on each list (decision 133).
    /// </summary>
    internal sealed class ListMembershipRules
    {
        public double OtherAdultsPercent { get; set; }

        /// <summary>
        /// For each opt-in list, the percent of adults with an active email who join it, by connection status.
        /// </summary>
        public Dictionary<string, Dictionary<string, double>> OptInPercentByStatus { get; set; } = new Dictionary<string, Dictionary<string, double>>();

        public double OtherParentsPercent { get; set; }

        public int KidsMaxGrade { get; set; } = 6;

        public IntRange StudentGrades { get; set; } = new IntRange { Min = 7, Max = 12 };

        public IntRange JoinedYearsAgo { get; set; } = new IntRange { Min = 2, Max = 6 };

        public IntRange JoinedDaysAfterFirstVisit { get; set; } = new IntRange { Min = 7, Max = 90 };
    }
}
