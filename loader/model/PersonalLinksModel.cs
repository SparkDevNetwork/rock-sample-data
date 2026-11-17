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
    /// configuration/personal-links.json: the bookmarks behind Rock's bookmark icon, in shared sections secured by role and
    /// personal sections for staff (issue 46).
    /// </summary>
    internal sealed class PersonalLinksConfiguration
    {
        /// <summary>
        /// The Cast staff member who made the shared sections.
        /// </summary>
        public Guid CreatedBy { get; set; }

        /// <summary>
        /// The roles Rock's shared link editor allows to View, Edit, and Administrate each new shared section.
        /// </summary>
        public List<string> EditorRoles { get; set; } = new List<string>();

        public List<SharedLinkSection> SharedSections { get; set; } = new List<SharedLinkSection>();

        public List<PersonalLinkSectionDefinition> PersonalSections { get; set; } = new List<PersonalLinkSectionDefinition>();

        public CampusPastorLinkSection CampusPastorSection { get; set; }

        /// <summary>
        /// The bookmark menus verify asks Rock for.
        /// </summary>
        public List<PersonalLinksMenu> Menus { get; set; } = new List<PersonalLinksMenu>();
    }

    internal sealed class SharedLinkSection
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        public int CreatedDaysAgo { get; set; }

        /// <summary>
        /// The section's View rule, which replaces the editor's.
        /// </summary>
        public CategorySecurityRule Security { get; set; }

        public List<PersonalLinkDefinition> Links { get; set; } = new List<PersonalLinkDefinition>();
    }

    internal sealed class PersonalLinkSectionDefinition
    {
        /// <summary>
        /// The Cast staff member the section belongs to.
        /// </summary>
        public Guid Owner { get; set; }

        /// <summary>
        /// The owner's name, for messages.
        /// </summary>
        public string OwnerName { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        public int CreatedDaysAgo { get; set; }

        public List<PersonalLinkDefinition> Links { get; set; } = new List<PersonalLinkDefinition>();
    }

    /// <summary>
    /// The section each loaded campus pastor gets. A link name's {campus} is the pastor's campus.
    /// </summary>
    internal sealed class CampusPastorLinkSection
    {
        public string Name { get; set; }

        /// <summary>
        /// The section's Guid on each campus, by campus key.
        /// </summary>
        public Dictionary<string, Guid> Guids { get; set; } = new Dictionary<string, Guid>();

        public int CreatedDaysAgo { get; set; }

        public List<PersonalLinkDefinition> Links { get; set; } = new List<PersonalLinkDefinition>();
    }

    internal sealed class PersonalLinkDefinition
    {
        public string Name { get; set; }

        /// <summary>
        /// The Rock page, by Guid.
        /// </summary>
        public Guid Page { get; set; }

        /// <summary>
        /// The load part whose rows the page shows, by SampleDataPart name, or null for church setup.
        /// </summary>
        public string Part { get; set; }

        /// <summary>
        /// The page parameter the target's Id fills, such as ReportId, or null for a page with no parameter.
        /// </summary>
        public string Parameter { get; set; }

        public PersonalLinkTarget Target { get; set; }
    }

    /// <summary>
    /// The row a link's page parameter names.
    /// </summary>
    internal sealed class PersonalLinkTarget
    {
        /// <summary>
        /// report, dataView, category, stepProgram, eventCalendar, campaignFund, campus, campusList, or campusSmallGroups.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// A key in a configuration file, for the kinds that name one.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// A Rock row's Guid, for the kinds that name one.
        /// </summary>
        public Guid? Guid { get; set; }
    }

    /// <summary>
    /// One person's bookmark menu: the section names Rock lists, in order.
    /// </summary>
    internal sealed class PersonalLinksMenu
    {
        public Guid Person { get; set; }

        public string Name { get; set; }

        public List<string> Sections { get; set; } = new List<string>();
    }
}
