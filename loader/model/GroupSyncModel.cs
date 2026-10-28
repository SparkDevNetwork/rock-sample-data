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
    /// configuration/categories.json: every category the sample church adds to Rock (decision D-group-sync-2).
    /// </summary>
    internal sealed class CategoryConfiguration
    {
        public List<CategoryDefinition> Categories { get; set; } = new List<CategoryDefinition>();

        /// <summary>
        /// The category with the key.
        /// </summary>
        /// <exception cref="InvalidDataException">configuration/categories.json has no such category.</exception>
        public CategoryDefinition Get( string key )
        {
            return Categories.FirstOrDefault( c => c.Key == key )
                ?? throw new InvalidDataException( $"configuration/categories.json has no category '{key}'." );
        }
    }

    /// <summary>
    /// One category: what it holds, where it sits in its tree, and how Rock shows it.
    /// </summary>
    internal sealed class CategoryDefinition
    {
        public string Key { get; set; }

        /// <summary>
        /// The parent category's key, or null for a top-level category.
        /// </summary>
        public string Parent { get; set; }

        /// <summary>
        /// The Rock entity the category holds, such as Rock.Model.DataView.
        /// </summary>
        public string EntityType { get; set; }

        /// <summary>
        /// For a Group category, the one group type it holds, as Rock qualifies it by GroupTypeId.
        /// </summary>
        public Guid? QualifierGroupType { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public string IconCssClass { get; set; }

        public Guid Guid { get; set; }
    }

    /// <summary>
    /// configuration/group-syncs.json: the data views that feed groups, and the syncs that tie them together
    /// (decisions D-group-sync-1 to D-group-sync-4).
    /// </summary>
    internal sealed class GroupSyncConfiguration
    {
        /// <summary>
        /// Rock's "Sync Welcome To Group" system communication.
        /// </summary>
        public Guid WelcomeCommunicationGuid { get; set; }

        /// <summary>
        /// Rock's "Sync Exit From Group" system communication.
        /// </summary>
        public Guid ExitCommunicationGuid { get; set; }

        public List<DataViewDefinition> DataViews { get; set; } = new List<DataViewDefinition>();

        public List<GroupSyncDefinition> Syncs { get; set; } = new List<GroupSyncDefinition>();

        /// <summary>
        /// Cast people each sync's group must hold after install.
        /// </summary>
        public List<CastSyncMember> Cast { get; set; } = new List<CastSyncMember>();
    }

    /// <summary>
    /// One person data view and its filter tree.
    /// </summary>
    internal sealed class DataViewDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The key of its category in configuration/categories.json.
        /// </summary>
        public string Category { get; set; }

        public Guid Guid { get; set; }

        public DataViewFilterDefinition Filter { get; set; }
    }

    /// <summary>
    /// One node of a data view's filter tree: a group whose children must all hold, any hold, or none hold,
    /// or a leaf that is one Rock data filter component with the selection Rock's editor saves for it.
    /// </summary>
    internal sealed class DataViewFilterDefinition
    {
        public List<DataViewFilterDefinition> All { get; set; }

        public List<DataViewFilterDefinition> Any { get; set; }

        public List<DataViewFilterDefinition> None { get; set; }

        /// <summary>
        /// A leaf's data filter component, by its Rock class name.
        /// </summary>
        public string Component { get; set; }

        public string Selection { get; set; }

        /// <summary>
        /// Rock's FilterExpressionType for this node: Filter (0), GroupAll (1), GroupAny (2), or GroupAllFalse (3), which is "none".
        /// </summary>
        public int ExpressionType => Component != null ? 0 : All != null ? 1 : Any != null ? 2 : None != null ? 3
            : throw new InvalidDataException( "configuration/group-syncs.json has a filter with no component and no all, any, or none." );

        /// <summary>
        /// A group's children, or none for a leaf.
        /// </summary>
        public List<DataViewFilterDefinition> Children => All ?? Any ?? None ?? new List<DataViewFilterDefinition>();
    }

    /// <summary>
    /// One sync: a data view, the group it fills, and what Rock does when the job later adds or removes someone.
    /// </summary>
    internal sealed class GroupSyncDefinition
    {
        public string Key { get; set; }

        /// <summary>
        /// The key of the data view that feeds it.
        /// </summary>
        public string DataView { get; set; }

        public GroupSyncTarget Target { get; set; }

        /// <summary>
        /// Minutes between syncs, or null to sync on every run of the job.
        /// </summary>
        public int? IntervalMinutes { get; set; }

        /// <summary>
        /// Sends Rock's welcome email when the job adds someone.
        /// </summary>
        public bool Welcome { get; set; }

        /// <summary>
        /// Sends Rock's exit email when the job removes someone.
        /// </summary>
        public bool Exit { get; set; }

        /// <summary>
        /// Makes a login for a new member who has none, sent in the welcome email.
        /// </summary>
        public bool CreateLogin { get; set; }

        public Guid Guid { get; set; }
    }

    /// <summary>
    /// The group a sync fills: a communication list from configuration/communications.json, or a Rock group by Guid.
    /// </summary>
    internal sealed class GroupSyncTarget
    {
        public string List { get; set; }

        public Guid? Group { get; set; }

        /// <summary>
        /// The Rock group's name, for messages.
        /// </summary>
        public string Name { get; set; }
    }

    /// <summary>
    /// A Cast person a sync's group must hold.
    /// </summary>
    internal sealed class CastSyncMember
    {
        public string Sync { get; set; }

        public Guid Person { get; set; }

        public string Name { get; set; }

        public string Reason { get; set; }
    }
}
