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
    /// configuration/categories.json: every category the sample church adds to Rock (decision 185).
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

        /// <summary>
        /// For an Attribute category, the entity its attributes belong to, such as Rock.Model.Person, as Rock
        /// qualifies it by EntityTypeId.
        /// </summary>
        public string QualifierEntityType { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public string IconCssClass { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// True for a category Rock ships. The loader finds it by Guid and changes only its name and description,
        /// so Rock's code, which finds it by Guid, still does.
        /// </summary>
        public bool RockShips { get; set; }

        /// <summary>
        /// The category's own security rules, by action. Items in it inherit them (Rock's parent authority).
        /// </summary>
        public List<CategorySecurityRule> Security { get; set; } = new List<CategorySecurityRule>();
    }

    /// <summary>
    /// One action's rules on a category, in the order Rock's security editor saves them: each allowed role, then
    /// "deny All Users" when the action is locked to those roles.
    /// </summary>
    internal sealed class CategorySecurityRule
    {
        /// <summary>
        /// Rock's action, such as View or Edit.
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// The Rock security roles allowed, by Rock's role name, such as "RSR - Staff Workers", which
        /// configuration/security-roles.json maps to a Guid.
        /// </summary>
        public List<string> Allow { get; set; } = new List<string>();

        /// <summary>
        /// Adds Rock's "deny All Users" after the allowed roles. Without it, anyone else falls through to the parent,
        /// and in the end to Rock's global default, which lets everyone view.
        /// </summary>
        public bool DenyAllUsers { get; set; }
    }

    /// <summary>
    /// configuration/group-syncs.json: the data views that feed groups, and the syncs that tie them together
    /// (decisions 186 to 188).
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
    /// One data view and its filter tree.
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

        /// <summary>
        /// The Rock entity the view returns, such as Rock.Model.Group. Null is a person.
        /// </summary>
        public string EntityType { get; set; }

        /// <summary>
        /// How Rock persists the view, or null to run it live every time.
        /// </summary>
        public DataViewPersistence Persisted { get; set; }

        /// <summary>
        /// Rock's post-filter transformation component, by class name, or null for none.
        /// </summary>
        public string Transform { get; set; }

        /// <summary>
        /// Rules on this view alone, only where it must be narrower than its category, such as Top Givers.
        /// </summary>
        public List<CategorySecurityRule> Security { get; set; } = new List<CategorySecurityRule>();

        public DataViewFilterDefinition Filter { get; set; }
    }

    /// <summary>
    /// How often Rock's Update Persisted DataViews job refreshes a view: on a named schedule, or every so many minutes.
    /// </summary>
    internal sealed class DataViewPersistence
    {
        /// <summary>
        /// A named schedule's key in configuration/reporting.json.
        /// </summary>
        public string Schedule { get; set; }

        public int? IntervalMinutes { get; set; }
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
        /// For Rock's InRegistrationInstanceFilter in registrations.json: the instance it names, in place of a selection,
        /// since Rock's selection holds Ids only a load knows.
        /// </summary>
        public RegistrationFilterRule Registration { get; set; }

        /// <summary>
        /// For a filter that points at another data view, such as Rock's Existing Data View filter or a related data view
        /// filter: the other view's key, in place of a selection, since Rock's selection holds its Id or Guid.
        /// </summary>
        public string DataView { get; set; }

        /// <summary>
        /// For Rock's StepsTakenFilter in configuration/reporting.json: the steps it matches, in place of a selection.
        /// </summary>
        public StepsFilterRule Steps { get; set; }

        /// <summary>
        /// Rock's FilterExpressionType for this node: Filter (0), GroupAll (1), GroupAny (2), or GroupAllFalse (3), which is "none".
        /// </summary>
        public int ExpressionType => Component != null ? 0 : All != null ? 1 : Any != null ? 2 : None != null ? 3
            : throw new InvalidDataException( "A data view has a filter with no component and no all, any, or none." );

        /// <summary>
        /// A group's children, or none for a leaf.
        /// </summary>
        public List<DataViewFilterDefinition> Children => All ?? Any ?? None ?? new List<DataViewFilterDefinition>();
    }

    /// <summary>
    /// The steps a StepsTakenFilter matches: Complete steps in one program, of one step type or of every type in one
    /// Ministry by Design stage (configuration/steps.json), completed at any time or in the last so many days.
    /// </summary>
    internal sealed class StepsFilterRule
    {
        /// <summary>
        /// The program's key in configuration/steps.json.
        /// </summary>
        public string Program { get; set; }

        /// <summary>
        /// A stage, such as Belong. With no step type either, every step type in the program.
        /// </summary>
        public string Stage { get; set; }

        /// <summary>
        /// One step type's key, in place of a stage.
        /// </summary>
        public string StepType { get; set; }

        public int? CompletedInLastDays { get; set; }
    }

    /// <summary>
    /// The registration instance an InRegistrationInstanceFilter names: an instance rule and its next or last occurrence,
    /// whether it matches registrants or registrars, and the wait list.
    /// </summary>
    internal sealed class RegistrationFilterRule
    {
        /// <summary>
        /// The registrations.json instance rule key.
        /// </summary>
        public string Instance { get; set; }

        public RegistrationOccurrencePick Occurrence { get; set; }

        /// <summary>
        /// "registrant" or "registrar".
        /// </summary>
        public string Person { get; set; } = "registrant";

        /// <summary>
        /// True for only the wait list, false for only those off it, null for both.
        /// </summary>
        public bool? OnWaitList { get; set; }
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
