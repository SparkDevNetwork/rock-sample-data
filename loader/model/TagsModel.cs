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
    /// configuration/tags.json: the church's tags on people, who made them, and who carries them (decision D-tags-1).
    /// </summary>
    internal sealed class TagsConfiguration
    {
        public List<TagDefinition> Tags { get; set; } = new List<TagDefinition>();

        /// <summary>
        /// The security walkthrough for tags, which verify asks Rock's own security check.
        /// </summary>
        public List<AccessExpectation> Access { get; set; } = new List<AccessExpectation>();
    }

    /// <summary>
    /// One tag on people. An organizational tag has a category, and a personal tag has an owner.
    /// </summary>
    internal sealed class TagDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// Rock's own Staff tag, found by Guid. The loader writes only who carries it.
        /// </summary>
        public bool RockShips { get; set; }

        /// <summary>
        /// A tag category's key in configuration/categories.json, for an organizational tag.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// The staff member a personal tag belongs to, or null for an organizational tag.
        /// </summary>
        public TagPerson Owner { get; set; }

        /// <summary>
        /// Who made an organizational tag. A personal tag is made by its owner.
        /// </summary>
        public TagPerson CreatedBy { get; set; }

        public int CreatedDaysAgo { get; set; }

        /// <summary>
        /// Who tags each person. Defaults to the owner, or else the creator.
        /// </summary>
        public TagPerson TaggedBy { get; set; }

        /// <summary>
        /// Tags each person within this many days before install day, or any day since the tag was made when null.
        /// </summary>
        public int? TaggedWithinDays { get; set; }

        public string IconCssClass { get; set; }

        /// <summary>
        /// The tag's color. Rock gives a new tag #F4F5F7.
        /// </summary>
        public string BackgroundColor { get; set; }

        /// <summary>
        /// Tags every staff member in configuration/staff.json this dataset loads, on their start date.
        /// </summary>
        public bool Staff { get; set; }

        /// <summary>
        /// The Cast people a story tags.
        /// </summary>
        public List<TagPerson> Cast { get; set; } = new List<TagPerson>();

        /// <summary>
        /// Generated people who match a rule, or null when the tag holds only the Cast and staff.
        /// </summary>
        public TagDraw Draw { get; set; }

        /// <summary>
        /// The person whose own campus a draw's "owner" campus means, and who tags people by default.
        /// </summary>
        public TagPerson Maker => Owner ?? CreatedBy;
    }

    /// <summary>
    /// A Cast person, by Guid, with their name for messages.
    /// </summary>
    internal sealed class TagPerson
    {
        public Guid Person { get; set; }

        public string Name { get; set; }
    }

    /// <summary>
    /// Which generated people a tag adds. A person must match every rule given.
    /// </summary>
    internal sealed class TagDraw
    {
        /// <summary>
        /// "main" for the main campus, "owner" for the campus of the tag's maker, or null for every campus.
        /// </summary>
        public string Campus { get; set; }

        public int MinAge { get; set; } = 18;

        public int? MaxAge { get; set; }

        /// <summary>
        /// Only people who are the one member of their family.
        /// </summary>
        public bool LivesAlone { get; set; }

        /// <summary>
        /// Connection statuses, or every status when empty.
        /// </summary>
        public List<string> Statuses { get; set; } = new List<string>();

        /// <summary>
        /// Only people whose family first came within this many days before install day.
        /// </summary>
        public int? FirstCameWithinDays { get; set; }

        /// <summary>
        /// A data view's key: only people in that view on install day. A load that left the view out tags no one.
        /// </summary>
        public string DataView { get; set; }

        /// <summary>
        /// Each match's chance of being tagged, in percent.
        /// </summary>
        public double? Percent { get; set; }

        /// <summary>
        /// How many matches are tagged.
        /// </summary>
        public int? Count { get; set; }
    }
}
