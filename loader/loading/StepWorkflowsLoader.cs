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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/18/26 - CLAUDE

        Rock's StepTypeDetail block saves a trigger with the step type, its program, the workflow type, the trigger type
        (Step Completed or Manual), a blank qualifier ("|"), and the workflow type's name. When a step becomes complete,
        Rock's step save hook sends LaunchStepChangeWorkflows: it activates the workflow type with the trigger's name and
        processes it right away with the step as its entity, on the message bus, so nobody is its initiator. A persisted
        workflow is saved with the step as its EntityId, and a StepWorkflow row links the step, the trigger, and the
        workflow. A manual trigger only offers the workflow on the step, and writes no StepWorkflow row.

        Each workflow type here is tasks only. Processing runs its first actions at once: set the person from the step,
        set the assignee, name the workflow, and assign the activity. Then it waits at the form, where Rock's My Workflows
        list finds it for the assignee. The loader writes the rows that processing leaves: those actions complete, the form
        action processed but not complete, and the last action not reached. A past task is the same, with the form
        submitted by the assignee and the workflow completed at that moment. The types keep their workflows forever (no
        age limit or retention), so Rock Cleanup leaves them as they are, and their forms send no email, so Process
        Workflows only reprocesses an open one and leaves it open.

        Steps are read back from the steps step's rows, so each workflow points at a real step. A baptism's task goes to
        the leader of the step's campus, which every campus pastor is (decision D-stepflows-2).

        Reason: Issue 18 (decisions D-stepflows-1 to D-stepflows-5).
    */

    /// <summary>
    /// Writes the step workflow types, their triggers, and the workflows completed steps started.
    /// </summary>
    internal sealed class StepWorkflowsLoader
    {
        private static readonly Guid StepEntityTypeGuid = new Guid( "8EADB0DC-17F4-4541-A46E-53F89E21A622" );
        private static readonly Guid PrimaryButtonGuid = new Guid( "FDC397CD-8B4A-436E-BEA1-BCE2E6717C03" );
        private const string SetFromEntityClass = "Rock.Workflow.Action.SetAttributeFromEntity";
        private const string SetToCurrentPersonClass = "Rock.Workflow.Action.SetAttributeToCurrentPerson";
        private const string SetNameClass = "Rock.Workflow.Action.SetWorkflowName";
        private const string AssignFromAttributeClass = "Rock.Workflow.Action.AssignActivityFromAttributeValue";
        private const string UserEntryFormClass = "Rock.Workflow.Action.UserEntryForm";
        private const string CompleteWorkflowClass = "Rock.Workflow.Action.CompleteWorkflow";

        /// <summary>
        /// Rock's WorkflowTriggerCondition values.
        /// </summary>
        private enum TriggerCondition
        {
            Manual = 1,
            IsComplete = 2
        }

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly WorkflowTypeWriter _types;

        public StepWorkflowsLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
            _types = new WorkflowTypeWriter( db, foreignKey );
        }

        /// <summary>
        /// The rows of one written workflow type that its workflows point at.
        /// </summary>
        private sealed class TypeRows
        {
            public StepWorkflowDefinition Definition { get; set; }

            public int TypeId { get; set; }

            public int TriggerId { get; set; }

            public int ActivityTypeId { get; set; }

            public List<int> ActionTypeIds { get; } = new List<int>();

            public int FormActionIndex { get; set; }

            public int PersonAttributeId { get; set; }

            public int AssigneeAttributeId { get; set; }

            public Dictionary<string, int> FieldAttributeIds { get; } = new Dictionary<string, int>();
        }

        /// <summary>
        /// Writes every workflow type and trigger, then each triggered type's workflows.
        /// </summary>
        /// <param name="people">Everyone loaded.</param>
        /// <param name="campusPastors">Each campus's pastor by campus key, the campus leader a baptism's task goes to.</param>
        /// <param name="campusIds">Each campus's Id by key.</param>
        public void Load( PeopleLoader people, Dictionary<string, LoadedPerson> campusPastors, Dictionary<string, int> campusIds )
        {
            var config = _source.StepWorkflows;
            if ( config == null || _source.Steps == null )
            {
                _db.Log.Write( "  Step workflows: skipped (this package has no configuration/step-workflows.json)" );
                return;
            }

            if ( !people.People.TryGetValue( config.Author, out var author ) )
            {
                throw new InvalidDataException( $"configuration/step-workflows.json: the author {config.Author} is not a loaded person." );
            }

            var built = _dates.Anchor.Date.AddMonths( -config.BuiltMonthsAgo ).AddHours( 10 ).AddMinutes( 15 );
            var byPersonId = people.Households.SelectMany( h => h.Members ).GroupBy( p => p.PersonId ).ToDictionary( g => g.Key, g => g.First() );
            var campusKeys = campusIds.ToDictionary( c => c.Value, c => c.Key );
            var summary = new List<string>();
            foreach ( var definition in config.Workflows )
            {
                var rows = WriteType( definition, author, people, built );
                if ( definition.IsManual )
                {
                    summary.Add( $"{definition.Name} (by hand)" );
                    continue;
                }

                var (open, completed) = WriteWorkflows( config, rows, built, byPersonId, people, campusPastors, campusKeys );
                summary.Add( $"{definition.Name} {open + completed:N0} ({open:N0} open)" );
            }

            _db.Log.Write( $"  Step workflows: {string.Join( ", ", summary )}" );
        }

        #region Types

        /// <summary>
        /// Writes one workflow type the way Rock's workflow editor saves it, and its trigger the way Rock's step type editor
        /// saves it.
        /// </summary>
        private TypeRows WriteType( StepWorkflowDefinition definition, LoadedPerson author, PeopleLoader people, DateTime built )
        {
            Guid G( string part ) => SeededRandom.StableGuid( $"step-workflow:{definition.Key}:{part}" );
            var rows = new TypeRows { Definition = definition };
            var categoryId = _db.RequireIdByGuid( "Category", _source.Categories.Get( definition.Category ).Guid );
            rows.TypeId = _types.InsertType( definition.Name, definition.Description, categoryId, definition.WorkTerm, definition.IconCssClass, definition.Prefix, definition.Guid, built, author.AliasId );

            // A staff assignee is the attribute's default value, which Rock saves on every workflow.
            string assigneeDefault = null;
            if ( definition.Assignee.Staff.HasValue )
            {
                assigneeDefault = ( people.People.TryGetValue( definition.Assignee.Staff.Value, out var staff ) ? staff
                    : throw new InvalidDataException( $"configuration/step-workflows.json: {definition.Key}'s assignee {definition.Assignee.Staff} is not a loaded person." ) ).AliasGuid.ToString();
            }

            var order = 0;
            rows.PersonAttributeId = _types.InsertWorkflowAttribute( rows.TypeId, "Rock.Field.Types.PersonFieldType", definition.Person.Key, definition.Person.Name, definition.Person.Description,
                order++, false, null, G( $"attribute:{definition.Person.Key}" ), built );
            rows.AssigneeAttributeId = _types.InsertWorkflowAttribute( rows.TypeId, "Rock.Field.Types.PersonFieldType", definition.Assignee.Key, definition.Assignee.Name, definition.Assignee.Description,
                order++, false, assigneeDefault, G( $"attribute:{definition.Assignee.Key}" ), built );
            foreach ( var field in definition.Fields )
            {
                rows.FieldAttributeIds[field.Key] = _types.InsertWorkflowAttribute( rows.TypeId, FieldTypeClass( field.Type ), field.Key, field.Name, null, order++, field.Required, null,
                    G( $"attribute:{field.Key}" ), built );
            }

            rows.ActivityTypeId = _types.InsertActivityType( rows.TypeId, definition.WorkTerm, G( "activity" ), built, author.AliasId );

            // The form shows the person, read only, and the fields. Rock keeps a form row for every workflow attribute.
            var formId = _types.InsertForm( definition.Form.Header, string.Empty,
                $"{definition.Form.Button}^{PrimaryButtonGuid.ToString().ToLowerInvariant()}^^{definition.Form.Response}|", G( "form" ), built, author.AliasId );
            var formOrder = 0;
            _types.InsertFormAttribute( formId, rows.PersonAttributeId, formOrder++, true, true, false, G( $"form:{definition.Person.Key}" ), built, author.AliasId );
            _types.InsertFormAttribute( formId, rows.AssigneeAttributeId, formOrder++, false, false, false, G( $"form:{definition.Assignee.Key}" ), built, author.AliasId );
            foreach ( var field in definition.Fields )
            {
                _types.InsertFormAttribute( formId, rows.FieldAttributeIds[field.Key], formOrder++, true, false, field.Required, G( $"form:{field.Key}" ), built, author.AliasId );
            }

            var personGuid = G( $"attribute:{definition.Person.Key}" ).ToString();
            var assigneeGuid = G( $"attribute:{definition.Assignee.Key}" ).ToString();
            var actions = new List<(string Name, string Class, int? FormId, (string Key, string Value)[] Settings)>
            {
                ($"Set {definition.Person.Name}", SetFromEntityClass, null, new[] { ("Attribute", personGuid), ("LavaTemplate", "{{ Entity.PersonAlias.Guid }}") })
            };
            if ( definition.Assignee.CampusLeader )
            {
                actions.Add( ($"Set {definition.Assignee.Name}", SetFromEntityClass, null, new[] { ("Attribute", assigneeGuid), ("LavaTemplate", "{{ Entity.Campus.LeaderPersonAlias.Guid }}") }) );
            }
            else if ( definition.Assignee.CurrentPerson )
            {
                actions.Add( ($"Set {definition.Assignee.Name}", SetToCurrentPersonClass, null, new[] { ("PersonAttribute", assigneeGuid) }) );
            }

            actions.Add( ("Set Name", SetNameClass, null, new[] { ("NameValue", $"{definition.Name}: {{{{ Workflow | Attribute:'{definition.Person.Key}' }}}}") }) );
            actions.Add( ($"Assign to {definition.Assignee.Name}", AssignFromAttributeClass, null, new[] { ("Attribute", assigneeGuid) }) );
            rows.FormActionIndex = actions.Count;
            actions.Add( ($"{definition.WorkTerm} Form", UserEntryFormClass, formId, Array.Empty<(string, string)>()) );
            actions.Add( ("Complete", CompleteWorkflowClass, null, new[] { ("Status", "Completed") }) );

            for ( var i = 0; i < actions.Count; i++ )
            {
                var (name, className, actionFormId, settings) = actions[i];
                var index = i;
                rows.ActionTypeIds.Add( _types.InsertActionType( rows.ActivityTypeId, name, i, _types.EntityType( className ), i == actions.Count - 1, actionFormId, G( $"action:{i}" ), built,
                    author.AliasId, settings, key => G( $"action:{index}:{key}" ) ) );
            }

            var stepType = _source.Steps.StepTypes.FirstOrDefault( t => t.Key == definition.StepType )
                ?? throw new InvalidDataException( $"configuration/step-workflows.json: {definition.Key} names step type {definition.StepType}, which configuration/steps.json does not have." );
            var stepTypeId = _db.RequireIdByGuid( "StepType", stepType.Guid );
            rows.TriggerId = _types.Insert( @"
INSERT INTO [StepWorkflowTrigger] ( [StepProgramId], [StepTypeId], [WorkflowTypeId], [TriggerType], [TypeQualifier], [WorkflowName], [IsActive],
    [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [Guid], [ForeignKey] )
OUTPUT INSERTED.[Id]
SELECT [StepProgramId], [Id], @TypeId, @TriggerType, '|', @Name, 1, @Built, @Built, @Author, @Author, @Guid, @ForeignKey
FROM [StepType]
WHERE [Id] = @StepTypeId",
                P( "@TypeId", rows.TypeId ), P( "@TriggerType", ( int ) ( definition.IsManual ? TriggerCondition.Manual : TriggerCondition.IsComplete ) ), P( "@Name", definition.Name ),
                P( "@Built", built ), P( "@Author", author.AliasId ), P( "@Guid", G( "trigger" ) ), P( "@StepTypeId", stepTypeId ) );

            return rows;
        }

        private static string FieldTypeClass( string type ) => type switch
        {
            "person" => "Rock.Field.Types.PersonFieldType",
            "boolean" => "Rock.Field.Types.BooleanFieldType",
            "memo" => "Rock.Field.Types.MemoFieldType",
            _ => throw new InvalidDataException( $"configuration/step-workflows.json: a field type must be person, boolean, or memo, not {type}." )
        };

        #endregion

        #region Workflows

        /// <summary>
        /// Writes the workflow each step of the type completed since the trigger was built started: open when the step was
        /// completed in the last open days, and completed by its assignee otherwise. Returns how many of each.
        /// </summary>
        private (int Open, int Completed) WriteWorkflows( StepWorkflowsConfiguration config, TypeRows rows, DateTime built, Dictionary<int, LoadedPerson> byPersonId, PeopleLoader people,
            Dictionary<string, LoadedPerson> campusPastors, Dictionary<int, string> campusKeys )
        {
            var definition = rows.Definition;
            var anchor = _dates.Anchor.Date;
            var openSince = anchor.AddDays( -config.OpenDays );
            var stepTypeGuid = _source.Steps.StepTypes.First( t => t.Key == definition.StepType ).Guid;
            var steps = _db.Query( @"
SELECT [s].[Id], [s].[CampusId], [s].[CompletedDateTime], [pa].[PersonId], [p].[NickName] + ' ' + [p].[LastName] + ISNULL( ' ' + [suffix].[Value], '' ) AS [FullName]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
LEFT JOIN [DefinedValue] AS [suffix] ON [suffix].[Id] = [p].[SuffixValueId]
WHERE [st].[Guid] = @StepType AND [s].[ForeignKey] = @ForeignKey AND [ss].[IsCompleteStatus] = 1
    AND [s].[CompletedDateTime] >= @Since AND [s].[CompletedDateTime] < @Tomorrow
ORDER BY [s].[CompletedDateTime], [s].[Id]",
                P( "@StepType", stepTypeGuid ), P( "@ForeignKey", _foreignKey ), P( "@Since", built ), P( "@Tomorrow", anchor.AddDays( 1 ) ) );
            var baptisms = definition.Fields.Any( f => f.History?.IfBaptizedAfter == true ) ? BaptismDates() : new Dictionary<int, DateTime>();

            var stepEntityTypeId = _db.RequireIdByGuid( "EntityType", StepEntityTypeGuid );
            var workflows = TableFactory.Create( "Id:int", "WorkflowTypeId:int", "Name:string", "Status:string", "IsProcessing:bool", "ActivatedDateTime:date", "LastProcessedDateTime:date",
                "CompletedDateTime:date", "WorkflowIdNumber:int", "WorkflowId:string", "EntityTypeId:int", "EntityId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date",
                "ModifiedByPersonAliasId:int", "ForeignKey:string" );
            var activities = TableFactory.Create( "Id:int", "WorkflowId:int", "ActivityTypeId:int", "ActivatedDateTime:date", "LastProcessedDateTime:date", "CompletedDateTime:date",
                "AssignedPersonAliasId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ModifiedByPersonAliasId:int", "ForeignKey:string" );
            var actions = TableFactory.Create( "ActivityId:int", "ActionTypeId:int", "LastProcessedDateTime:date", "CompletedDateTime:date", "FormAction:string", "Guid:guid",
                "CreatedDateTime:date", "ModifiedDateTime:date", "ModifiedByPersonAliasId:int", "ForeignKey:string" );
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string",
                "CreatedDateTime:date", "ModifiedDateTime:date" );
            var links = TableFactory.Create( "StepWorkflowTriggerId:int", "WorkflowId:int", "StepId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );

            var nextWorkflowId = _types.NextId( "Workflow" );
            var nextActivityId = _types.NextId( "WorkflowActivity" );
            var number = 0;
            var open = 0;
            foreach ( var step in steps )
            {
                var personId = ( int ) step["PersonId"];
                if ( !byPersonId.TryGetValue( personId, out var person ) )
                {
                    continue;
                }

                LoadedPerson assignee;
                if ( definition.Assignee.CampusLeader )
                {
                    if ( !( step["CampusId"] is int campusId ) || !campusKeys.TryGetValue( campusId, out var campusKey ) || !campusPastors.TryGetValue( campusKey, out assignee ) )
                    {
                        continue;
                    }
                }
                else
                {
                    assignee = people.People[definition.Assignee.Staff.Value];
                }

                var completedStep = ( DateTime ) step["CompletedDateTime"];
                var seed = $"{_seed}:step-workflow:{definition.Key}:{person.Cast.Guid}:{completedStep:yyyyMMdd}";
                var random = new SeededRandom( seed );
                var cast = _source.StepWorkflows.Cast.FirstOrDefault( c => c.Workflow == definition.Key && people.People.TryGetValue( c.Person, out var p ) && p.PersonId == personId );

                // Rock launches the workflow seconds after the step is saved complete.
                var activated = completedStep.AddSeconds( random.Between( 1, 4 ) );
                var isOpen = completedStep >= openSince;
                var done = isOpen ? ( DateTime? ) null
                    : completedStep.Date.AddDays( cast?.CompletedDaysAfter ?? random.Between( definition.CompletedDaysAfter.Min, definition.CompletedDaysAfter.Max ) )
                        .AddMinutes( random.Between( 8 * 60, 17 * 60 + 30 ) );
                var lastProcessed = done ?? activated;
                var modifiedBy = done.HasValue ? assignee.AliasId : ( object ) DBNull.Value;
                var workflowId = nextWorkflowId++;
                var activityId = nextActivityId++;
                number++;
                open += isOpen ? 1 : 0;

                var name = $"{definition.Name}: {step["FullName"]}";
                workflows.Rows.Add( workflowId, rows.TypeId, name.Length > 100 ? name.Substring( 0, 100 ) : name, isOpen ? "Active" : "Completed", false, activated, lastProcessed, Value( done ),
                    number, $"{definition.Prefix}{number:00000}", stepEntityTypeId, ( int ) step["Id"], SeededRandom.StableGuid( $"{seed}:workflow" ), activated, lastProcessed, modifiedBy, _foreignKey );
                activities.Rows.Add( activityId, workflowId, rows.ActivityTypeId, activated, lastProcessed, Value( done ), assignee.AliasId, SeededRandom.StableGuid( $"{seed}:activity" ),
                    activated, lastProcessed, modifiedBy, _foreignKey );
                for ( var i = 0; i < rows.ActionTypeIds.Count; i++ )
                {
                    var isBeforeForm = i < rows.FormActionIndex;
                    var processed = isBeforeForm ? activated : i == rows.FormActionIndex ? lastProcessed : done;
                    var completedAction = isBeforeForm ? activated : done;
                    actions.Rows.Add( activityId, rows.ActionTypeIds[i], Value( processed ), Value( completedAction ), i == rows.FormActionIndex && done.HasValue ? definition.Form.Button : ( object ) DBNull.Value,
                        SeededRandom.StableGuid( $"{seed}:action:{i}" ), activated, processed ?? activated, isBeforeForm ? DBNull.Value : modifiedBy, _foreignKey );
                }

                void AddValue( int attributeId, string value, DateTime at, string part ) => values.Rows.Add( false, attributeId, workflowId, value, true, SeededRandom.StableGuid( $"{seed}:value:{part}" ),
                    _foreignKey, at, at );
                AddValue( rows.PersonAttributeId, person.AliasGuid.ToString(), activated, definition.Person.Key );
                AddValue( rows.AssigneeAttributeId, assignee.AliasGuid.ToString(), activated, definition.Assignee.Key );
                if ( done.HasValue )
                {
                    foreach ( var field in definition.Fields )
                    {
                        var value = cast != null ? CastValue( cast, field, people ) : HistoryValue( field, random, assignee, baptisms.TryGetValue( personId, out var baptized ) && baptized > completedStep );
                        if ( !string.IsNullOrEmpty( value ) )
                        {
                            AddValue( rows.FieldAttributeIds[field.Key], value, done.Value, field.Key );
                        }
                    }
                }

                links.Rows.Add( rows.TriggerId, workflowId, ( int ) step["Id"], SeededRandom.StableGuid( $"{seed}:link" ), activated, activated, _foreignKey );
            }

            _db.BulkInsert( "Workflow", workflows, keepIdentity: true );
            _db.BulkInsert( "WorkflowActivity", activities, keepIdentity: true );
            _db.BulkInsert( "WorkflowAction", actions, keepIdentity: false );
            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
            _db.BulkInsert( "StepWorkflow", links, keepIdentity: false );
            return (open, number - open);
        }

        /// <summary>
        /// What a past task holds in a field, by the field's history rule.
        /// </summary>
        private static string HistoryValue( StepWorkflowField field, SeededRandom random, LoadedPerson assignee, bool isBaptizedAfter )
        {
            var history = field.History;
            if ( history == null )
            {
                return null;
            }

            if ( history.Assignee )
            {
                return assignee.AliasGuid.ToString();
            }

            if ( field.Type == "boolean" )
            {
                return history.IfBaptizedAfter && isBaptizedAfter || random.Chance( ( history.YesPercent ?? 0 ) / 100 ) ? "True" : "False";
            }

            return history.Pool.Count > 0 && random.Chance( ( history.Percent ?? 0 ) / 100 ) ? random.Pick( history.Pool ) : null;
        }

        /// <summary>
        /// A Cast task's value for a field, with a person field's person Guid as their primary alias, as Rock stores it.
        /// </summary>
        private static string CastValue( CastStepWorkflow cast, StepWorkflowField field, PeopleLoader people )
        {
            if ( !cast.Values.TryGetValue( field.Key, out var value ) )
            {
                return null;
            }

            return field.Type == "person" && Guid.TryParse( value, out var personGuid ) && people.People.TryGetValue( personGuid, out var person ) ? person.AliasGuid.ToString() : value;
        }

        /// <summary>
        /// Each person's Baptism step date, read back from the steps step's rows.
        /// </summary>
        private Dictionary<int, DateTime> BaptismDates()
        {
            var baptism = _source.Steps.StepTypes.Single( t => t.Source?.Kind == "baptism" );
            return _db.Query( @"
SELECT [pa].[PersonId], MAX( [s].[CompletedDateTime] ) AS [Baptized]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
WHERE [st].[Guid] = @Baptism AND [s].[ForeignKey] = @ForeignKey AND [s].[CompletedDateTime] IS NOT NULL
GROUP BY [pa].[PersonId]", P( "@Baptism", baptism.Guid ), P( "@ForeignKey", _foreignKey ) )
                .ToDictionary( r => ( int ) r["PersonId"], r => ( DateTime ) r["Baptized"] );
        }

        private static object Value( DateTime? value ) => value.HasValue ? value.Value : DBNull.Value;

        #endregion
    }
}
