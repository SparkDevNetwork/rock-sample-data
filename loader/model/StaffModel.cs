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
    /// (decision D-staff-1).
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

        /// <summary>
        /// The key of the Person attribute category in configuration/categories.json that holds the staff attributes,
        /// or null in a package from before staff attributes (decision D-staff-4).
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// The security roles the attributes name, by key: Rock role group Guids.
        /// </summary>
        public Dictionary<string, Guid> Roles { get; set; } = new Dictionary<string, Guid>();

        /// <summary>
        /// The staff person attributes, in order.
        /// </summary>
        public List<StaffAttribute> Attributes { get; set; } = new List<StaffAttribute>();

        /// <summary>
        /// The Attribute Values block that shows the category on the person profile's Extended Attributes page.
        /// </summary>
        public StaffBlock Block { get; set; }

        /// <summary>
        /// The org chart in Rock's Organization Unit groups, or null in a package from before it (decision D-staff-5).
        /// </summary>
        public StaffOrgChart OrgChart { get; set; }
    }

    /// <summary>
    /// The org chart: Rock's Organization Unit groups, with each unit's leader and staff.
    /// </summary>
    internal sealed class StaffOrgChart
    {
        /// <summary>
        /// Rock's Organization Unit group type.
        /// </summary>
        public Guid GroupType { get; set; }

        /// <summary>
        /// The group type's Leader role.
        /// </summary>
        public Guid LeaderRole { get; set; }

        /// <summary>
        /// The group type's Staff role.
        /// </summary>
        public Guid StaffRole { get; set; }

        public List<StaffOrgUnit> Units { get; set; } = new List<StaffOrgUnit>();
    }

    /// <summary>
    /// One unit of the org chart.
    /// </summary>
    internal sealed class StaffOrgUnit
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// True for a unit Rock ships, which is already in the database. Otherwise the loader adds it.
        /// </summary>
        public bool RockShips { get; set; }

        /// <summary>
        /// For a unit the church adds: its name, description, parent unit key, and order among its siblings.
        /// </summary>
        public string Name { get; set; }

        public string Description { get; set; }

        public string Parent { get; set; }

        public int Order { get; set; }

        /// <summary>
        /// The staff key of the unit's leader.
        /// </summary>
        public string Leader { get; set; }

        /// <summary>
        /// The staff keys of the unit's staff.
        /// </summary>
        public List<string> Staff { get; set; } = new List<string>();
    }

    /// <summary>
    /// One staff person attribute.
    /// </summary>
    internal sealed class StaffAttribute
    {
        /// <summary>
        /// The attribute key, such as "StaffTitle".
        /// </summary>
        public string Key { get; set; }

        public Guid Guid { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The field type: image, text, single-select, campus, memo, person, or date.
        /// </summary>
        public string FieldType { get; set; }

        public int Order { get; set; }

        /// <summary>
        /// The field type's settings, as Rock stores them in AttributeQualifier.
        /// </summary>
        public Dictionary<string, string> Qualifiers { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// The roles allowed to view the attribute, with everyone else denied, or null when everyone may view it.
        /// </summary>
        public List<string> Viewers { get; set; }

        /// <summary>
        /// The roles allowed to edit the attribute. Everyone else is denied.
        /// </summary>
        public List<string> Editors { get; set; } = new List<string>();
    }

    /// <summary>
    /// photos/staff/manifest.json: the delivered staff photos, each a staff member's Staff Photo.
    /// </summary>
    internal sealed class StaffPhotoManifest
    {
        public string Model { get; set; }

        public List<StaffPhotoEntry> Images { get; set; } = new List<StaffPhotoEntry>();
    }

    /// <summary>
    /// One staff photo.
    /// </summary>
    internal sealed class StaffPhotoEntry
    {
        /// <summary>
        /// The JPEG under photos/, such as staff/foster-pete.jpg.
        /// </summary>
        public string File { get; set; }

        /// <summary>
        /// The Cast household key.
        /// </summary>
        public string Household { get; set; }

        /// <summary>
        /// The Cast member key.
        /// </summary>
        public string Member { get; set; }
    }

    /// <summary>
    /// The block that shows the staff attributes on a person's profile.
    /// </summary>
    internal sealed class StaffBlock
    {
        public Guid Guid { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The page's Guid: Rock's Extended Attributes page on the person profile.
        /// </summary>
        public Guid Page { get; set; }

        /// <summary>
        /// The block type's Guid: Rock's Attribute Values block.
        /// </summary>
        public Guid BlockType { get; set; }

        public string Zone { get; set; }

        public int Order { get; set; }
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

        /// <summary>
        /// The staff key of the person they report to, or null.
        /// </summary>
        public string Supervisor { get; set; }

        /// <summary>
        /// The day they joined staff.
        /// </summary>
        public RelativeDate StartDate { get; set; }

        /// <summary>
        /// A few public sentences about them, for a staff page.
        /// </summary>
        public string Bio { get; set; }

        /// <summary>
        /// Their favorite quote or Bible verse, with its reference.
        /// </summary>
        public string Quote { get; set; }
    }
}
