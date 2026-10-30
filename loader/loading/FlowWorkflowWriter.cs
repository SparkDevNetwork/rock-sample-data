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
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/30/26 - CLAUDE

        The only way into an On-Demand flow is Rock's Activate Communication Flow workflow action. So a
        flow that people enter from a form gets a workflow type the way Rock's workflow editor saves one:
        one activity with four actions (set the person to whoever is logged in, show the form, activate the
        flow, and complete the workflow), the workflow's attributes, and each action's settings. Each person
        who entered has a finished workflow they started themselves, with its activity, actions, and
        attribute values, as Rock leaves a persisted workflow.

        Rock registers the Activate Communication Flow action's entity type when the website starts, and
        its settings when the workflow editor first loads its actions. A church's database has the entity
        type before the loader runs, but a database made only by migrations does not, so the loader adds
        whatever is missing the way Rock would, and Rock updates the rows the next time it starts.

        Reason: Jon approved the Baptism Interest workflow (decision D-comm-flows-4).
    */

    /// <summary>
    /// Writes a workflow type that puts people in a Communication Flow, and each person's finished workflow.
    /// </summary>
    internal sealed class FlowWorkflowWriter
    {
        private static readonly Guid ActivateFlowEntityTypeGuid = new Guid( "4DDFE046-F05D-4857-BA9B-22391A605ED7" );
        private const string ActivateFlowClass = "Rock.Workflow.Action.ActivateCommunicationFlow";
        private const string UserEntryFormClass = "Rock.Workflow.Action.UserEntryForm";
        private const string SetToCurrentPersonClass = "Rock.Workflow.Action.SetAttributeToCurrentPerson";
        private const string CompleteWorkflowClass = "Rock.Workflow.Action.CompleteWorkflow";
        private static readonly Guid PrimaryButtonGuid = new Guid( "FDC397CD-8B4A-436E-BEA1-BCE2E6717C03" );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly string _foreignKey;
        private readonly string _seed;

        public FlowWorkflowWriter( RockDatabase db, SampleDataSource source, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// The rows of a written workflow type that each workflow points at.
        /// </summary>
        internal sealed class WorkflowTypeRows
        {
            public FlowWorkflowDefinition Definition { get; set; }

            public int TypeId { get; set; }

            public int ActivityTypeId { get; set; }

            public List<int> ActionTypeIds { get; set; } = new List<int>();

            public int PersonAttributeId { get; set; }

            public int CommentsAttributeId { get; set; }

            public int FlowAttributeId { get; set; }

            public Guid FlowGuid { get; set; }
        }

        /// <summary>
        /// Writes the workflow type, its attributes, its one activity, and its four actions with their settings.
        /// </summary>
        public WorkflowTypeRows WriteType( FlowWorkflowDefinition workflow, Guid flowGuid, int authorAliasId, DateTime created )
        {
            var workflowEntityTypeId = _db.RequireIdByGuid( "EntityType", new Guid( "3540E9A7-FE30-43A9-8B0A-A372B63DFC93" ) );
            var actionTypeEntityTypeId = _db.RequireIdByGuid( "EntityType", new Guid( "23E3273A-B137-48A3-9AFF-C8DC832DDCA6" ) );
            var activateFlowId = EnsureActivateFlowAction( actionTypeEntityTypeId, created );
            var formId = RequireEntityType( UserEntryFormClass );
            var setPersonId = RequireEntityType( SetToCurrentPersonClass );
            var completeId = RequireEntityType( CompleteWorkflowClass );
            var categoryId = _db.RequireIdByGuid( "Category", _source.Categories.Get( workflow.Category ).Guid );
            var rows = new WorkflowTypeRows { Definition = workflow, FlowGuid = flowGuid };
            Guid G( string part ) => SeededRandom.StableGuid( $"workflow:{workflow.Key}:{part}" );

            rows.TypeId = Insert( @"
INSERT INTO [WorkflowType] ( [IsSystem], [IsActive], [Name], [Description], [CategoryId], [Order], [WorkTerm], [IsPersisted], [LoggingLevel], [IconCssClass], [WorkflowIdPrefix],
    [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( 0, 1, @Name, @Description, @CategoryId, 0, @WorkTerm, 1, 0, @Icon, @Prefix, @Guid, @Created, @Created, @Author, @Author, @ForeignKey )",
                P( "@Name", workflow.Name ), P( "@Description", workflow.Description ), P( "@CategoryId", categoryId ), P( "@WorkTerm", workflow.WorkTerm ), P( "@Icon", workflow.IconCssClass ),
                P( "@Prefix", "BI" ), P( "@Guid", G( "type" ) ), P( "@Created", created ), P( "@Author", authorAliasId ) );

            var typeQualifier = rows.TypeId.ToString( System.Globalization.CultureInfo.InvariantCulture );
            rows.PersonAttributeId = InsertAttribute( FieldTypeId( "Rock.Field.Types.PersonFieldType" ), workflowEntityTypeId, "WorkflowTypeId", typeQualifier, "Person", "Person",
                "The person who filled out the form.", 0, true, null, G( "attribute:person" ), created );
            rows.CommentsAttributeId = InsertAttribute( FieldTypeId( "Rock.Field.Types.MemoFieldType" ), workflowEntityTypeId, "WorkflowTypeId", typeQualifier, "Comments", workflow.CommentsLabel,
                null, 1, false, null, G( "attribute:comments" ), created );
            rows.FlowAttributeId = InsertAttribute( FieldTypeId( "Rock.Field.Types.CommunicationFlowFieldType" ), workflowEntityTypeId, "WorkflowTypeId", typeQualifier, "CommunicationFlow", "Communication Flow",
                "The flow the workflow adds the person to.", 2, false, flowGuid.ToString(), G( "attribute:flow" ), created );

            rows.ActivityTypeId = Insert( @"
INSERT INTO [WorkflowActivityType] ( [IsActive], [WorkflowTypeId], [Name], [IsActivatedWithWorkflow], [Order], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( 1, @TypeId, 'Start', 1, 0, @Guid, @Created, @Created, @Author, @Author, @ForeignKey )",
                P( "@TypeId", rows.TypeId ), P( "@Guid", G( "activity:start" ) ), P( "@Created", created ), P( "@Author", authorAliasId ) );

            // The form shows the comments box. Rock keeps a form row for every workflow attribute, and shows only the visible ones.
            var workflowFormId = Insert( @"
INSERT INTO [WorkflowActionForm] ( [Header], [Footer], [Actions], [AllowNotes], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Header, @Footer, @Actions, 0, @Guid, @Created, @Created, @Author, @Author, @ForeignKey )",
                P( "@Header", workflow.FormHeader ), P( "@Footer", workflow.FormFooter ),
                P( "@Actions", $"Submit^{PrimaryButtonGuid.ToString().ToLowerInvariant()}^^Thank you! A pastor will be in touch soon.|" ),
                P( "@Guid", G( "form" ) ), P( "@Created", created ), P( "@Author", authorAliasId ) );
            var order = 0;
            foreach ( var (attributeId, visible, part) in new[] { (rows.PersonAttributeId, false, "person"), (rows.CommentsAttributeId, true, "comments"), (rows.FlowAttributeId, false, "flow") } )
            {
                _db.Execute( @"
INSERT INTO [WorkflowActionFormAttribute] ( [WorkflowActionFormId], [AttributeId], [Order], [IsVisible], [IsReadOnly], [IsRequired], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
VALUES ( @FormId, @AttributeId, @Order, @Visible, 0, 0, @Guid, @Created, @Created, @Author, @Author, @ForeignKey )",
                    P( "@FormId", workflowFormId ), P( "@AttributeId", attributeId ), P( "@Order", order++ ), P( "@Visible", visible ), P( "@Guid", G( $"form:{part}" ) ),
                    P( "@Created", created ), P( "@Author", authorAliasId ), P( "@ForeignKey", _foreignKey ) );
            }

            var personAttributeGuid = G( "attribute:person" ).ToString();
            var actions = new (string Name, int EntityTypeId, int? FormId, (string Key, string Value)[] Settings)[]
            {
                ("Set Person", setPersonId, null, new[] { ("PersonAttribute", personAttributeGuid) }),
                ("Interest Form", formId, workflowFormId, Array.Empty<(string, string)>()),
                ("Start the Flow", activateFlowId, null, new[] { ("CommunicationFlow", G( "attribute:flow" ).ToString()), ("Person", personAttributeGuid) }),
                ("Complete", completeId, null, new[] { ("Status", "Completed") })
            };
            for ( var i = 0; i < actions.Length; i++ )
            {
                var (name, entityTypeId, actionFormId, settings) = actions[i];
                var actionTypeId = Insert( @"
INSERT INTO [WorkflowActionType] ( [ActivityTypeId], [Name], [Order], [EntityTypeId], [IsActionCompletedOnSuccess], [IsActivityCompletedOnSuccess], [WorkflowFormId], [Guid],
    [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @ActivityTypeId, @Name, @Order, @EntityTypeId, 1, @CompletesActivity, @FormId, @Guid, @Created, @Created, @Author, @Author, @ForeignKey )",
                    P( "@ActivityTypeId", rows.ActivityTypeId ), P( "@Name", name ), P( "@Order", i ), P( "@EntityTypeId", entityTypeId ), P( "@CompletesActivity", i == actions.Length - 1 ),
                    P( "@FormId", ( object ) actionFormId ?? DBNull.Value ), P( "@Guid", G( $"action:{i}" ) ), P( "@Created", created ), P( "@Author", authorAliasId ) );
                rows.ActionTypeIds.Add( actionTypeId );

                foreach ( var (key, value) in settings )
                {
                    var attributeId = Convert.ToInt32( _db.Scalar( @"
SELECT [Id] FROM [Attribute] WHERE [EntityTypeId] = @EntityTypeId AND [EntityTypeQualifierColumn] = 'EntityTypeId' AND [EntityTypeQualifierValue] = @Component AND [Key] = @Key",
                        P( "@EntityTypeId", actionTypeEntityTypeId ), P( "@Component", entityTypeId.ToString( System.Globalization.CultureInfo.InvariantCulture ) ), P( "@Key", key ) )
                        ?? throw new InvalidOperationException( $"Rock has no '{key}' setting for the workflow action {name}." ) );
                    _db.Execute( @"
INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [IsPersistedValueDirty], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( 0, @AttributeId, @EntityId, @Value, 1, @Guid, @ForeignKey, @Created, @Created )",
                        P( "@AttributeId", attributeId ), P( "@EntityId", actionTypeId ), P( "@Value", value ), P( "@Guid", G( $"action:{i}:{key}" ) ), P( "@ForeignKey", _foreignKey ), P( "@Created", created ) );
                }
            }

            return rows;
        }

        /// <summary>
        /// Writes each person's finished workflow: they started it themselves, the form was submitted, and every action
        /// completed within seconds, as Rock leaves a persisted workflow.
        /// </summary>
        public void WriteRuns( WorkflowTypeRows rows, IEnumerable<(int PersonId, int AliasId, Guid PersonGuid, DateTime At)> entries )
        {
            var list = entries.OrderBy( e => e.At ).ThenBy( e => e.PersonId ).ToList();
            if ( list.Count == 0 )
            {
                return;
            }

            var aliasGuids = _db.Query( "SELECT [Id], [Guid] FROM [PersonAlias] WHERE [Id] IN ( SELECT [value] FROM STRING_SPLIT( @Ids, ',' ) )",
                    P( "@Ids", string.Join( ",", list.Select( e => e.AliasId ).Distinct() ) ) )
                .ToDictionary( r => ( int ) r["Id"], r => ( Guid ) r["Guid"] );
            var workflows = TableFactory.Create( "Id:int", "WorkflowTypeId:int", "Name:string", "Status:string", "IsProcessing:bool", "ActivatedDateTime:date", "LastProcessedDateTime:date",
                "CompletedDateTime:date", "InitiatorPersonAliasId:int", "WorkflowIdNumber:int", "WorkflowId:string", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date",
                "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );
            var activities = TableFactory.Create( "Id:int", "WorkflowId:int", "ActivityTypeId:int", "ActivatedDateTime:date", "LastProcessedDateTime:date", "CompletedDateTime:date", "Guid:guid",
                "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var actions = TableFactory.Create( "ActivityId:int", "ActionTypeId:int", "LastProcessedDateTime:date", "CompletedDateTime:date", "FormAction:string", "Guid:guid",
                "CreatedDateTime:date", "ModifiedDateTime:date", "ForeignKey:string" );
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            var nextWorkflowId = NextId( "Workflow" );
            var nextActivityId = NextId( "WorkflowActivity" );
            var number = 1;
            foreach ( var entry in list )
            {
                var seed = $"{_seed}:workflow:{rows.Definition.Key}:{entry.PersonGuid}:{entry.At:yyyyMMddHHmm}";
                var random = new SeededRandom( seed );
                var done = entry.At.AddSeconds( random.Between( 1, 4 ) );
                var workflowId = nextWorkflowId++;
                var activityId = nextActivityId++;
                workflows.Rows.Add( workflowId, rows.TypeId, rows.Definition.Name, "Completed", false, entry.At, done, done, entry.AliasId, number,
                    $"BI{number:00000}", SeededRandom.StableGuid( $"{seed}:workflow" ), entry.At, done, entry.AliasId, entry.AliasId, _foreignKey );
                activities.Rows.Add( activityId, workflowId, rows.ActivityTypeId, entry.At, done, done, SeededRandom.StableGuid( $"{seed}:activity" ), entry.At, done, _foreignKey );
                for ( var i = 0; i < rows.ActionTypeIds.Count; i++ )
                {
                    actions.Rows.Add( activityId, rows.ActionTypeIds[i], done, done, i == 1 ? "Submit" : ( object ) DBNull.Value, SeededRandom.StableGuid( $"{seed}:action:{i}" ), entry.At, done, _foreignKey );
                }

                values.Rows.Add( false, rows.PersonAttributeId, workflowId, aliasGuids[entry.AliasId].ToString(), true, SeededRandom.StableGuid( $"{seed}:person" ), _foreignKey, entry.At, done );
                values.Rows.Add( false, rows.FlowAttributeId, workflowId, rows.FlowGuid.ToString(), true, SeededRandom.StableGuid( $"{seed}:flow" ), _foreignKey, entry.At, done );
                if ( rows.Definition.Comments.Count > 0 && random.Chance( rows.Definition.CommentShare / 100.0 ) )
                {
                    values.Rows.Add( false, rows.CommentsAttributeId, workflowId, random.Pick( rows.Definition.Comments ), true, SeededRandom.StableGuid( $"{seed}:comments" ), _foreignKey, entry.At, done );
                }

                number++;
            }

            _db.BulkInsert( "Workflow", workflows, keepIdentity: true );
            _db.BulkInsert( "WorkflowActivity", activities, keepIdentity: true );
            _db.BulkInsert( "WorkflowAction", actions, keepIdentity: false );
            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
        }

        /// <summary>
        /// The Activate Communication Flow action's entity type and settings, added the way Rock registers them when they
        /// are missing.
        /// </summary>
        private int EnsureActivateFlowAction( int actionTypeEntityTypeId, DateTime created )
        {
            var id = _db.IdByGuid( "EntityType", ActivateFlowEntityTypeGuid );
            if ( !id.HasValue )
            {
                // Rock stores the class's assembly-qualified name, so it takes the version from an action Rock already registered.
                var formAssembly = ( string ) _db.Scalar( "SELECT [AssemblyName] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", UserEntryFormClass ) );
                id = Insert( @"
INSERT INTO [EntityType] ( [Name], [AssemblyName], [FriendlyName], [IsEntity], [IsSecured], [IsCommon], [Guid], [IsIndexingEnabled], [AttributesSupportPrePostHtml], [AttributesSupportShowOnBulk],
    [IsAchievementsEnabled], [IsMessageBusEventPublishEnabled], [IsRelatedToInteractionTrackedOnCreate] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Assembly, 'Activate Communication Flow', 0, 1, 0, @Guid, 0, 0, 0, 0, 0, 0 )",
                    P( "@Name", ActivateFlowClass ), P( "@Assembly", formAssembly.Replace( UserEntryFormClass, ActivateFlowClass ) ), P( "@Guid", ActivateFlowEntityTypeGuid ) );
            }

            var component = id.Value.ToString( System.Globalization.CultureInfo.InvariantCulture );
            var existing = _db.Query( "SELECT [Key] FROM [Attribute] WHERE [EntityTypeId] = @EntityTypeId AND [EntityTypeQualifierColumn] = 'EntityTypeId' AND [EntityTypeQualifierValue] = @Component",
                    P( "@EntityTypeId", actionTypeEntityTypeId ), P( "@Component", component ) )
                .Select( r => ( string ) r["Key"] ).ToHashSet();
            var workflowAttributeTypeId = FieldTypeId( "Rock.Field.Types.WorkflowAttributeFieldType" );
            var settings = new (string Key, string Name, string Description, int FieldTypeId, int Order, bool Required, string Default, string AttributeFieldTypes)[]
            {
                ("Active", "Active", "Should Service be used?", FieldTypeId( "Rock.Field.Types.BooleanFieldType" ), 0, false, "False", null),
                ("Order", "Order", "The order that this service should be used (priority)", FieldTypeId( "Rock.Field.Types.IntegerFieldType" ), 0, true, string.Empty, null),
                ("CommunicationFlow", "Communication Flow Attribute", "The workflow attribute that contains the communication flow to trigger.", workflowAttributeTypeId, 0, true, string.Empty, "Rock.Field.Types.CommunicationFlowFieldType"),
                ("Person", "Person", "The person to add to the communication flow.", workflowAttributeTypeId, 1, true, string.Empty, "Rock.Field.Types.PersonFieldType")
            };
            foreach ( var setting in settings.Where( s => !existing.Contains( s.Key ) ) )
            {
                var attributeId = InsertAttribute( setting.FieldTypeId, actionTypeEntityTypeId, "EntityTypeId", component, setting.Key, setting.Name, setting.Description, setting.Order, setting.Required,
                    setting.Default, SeededRandom.StableGuid( $"rock:{ActivateFlowClass}:{setting.Key}" ), created, isSystem: true );
                if ( setting.AttributeFieldTypes != null )
                {
                    _db.Execute( @"
INSERT INTO [AttributeQualifier] ( [IsSystem], [AttributeId], [Key], [Value], [Guid] )
VALUES ( 0, @AttributeId, 'attributefieldtypes', @Value, @Guid )",
                        P( "@AttributeId", attributeId ), P( "@Value", setting.AttributeFieldTypes ), P( "@Guid", SeededRandom.StableGuid( $"rock:{ActivateFlowClass}:{setting.Key}:qualifier" ) ) );
                }
            }

            return id.Value;
        }

        private int InsertAttribute( int fieldTypeId, int entityTypeId, string qualifierColumn, string qualifierValue, string key, string name, string description, int order, bool required,
            string defaultValue, Guid guid, DateTime created, bool isSystem = false )
        {
            return Insert( @"
INSERT INTO [Attribute] ( [IsSystem], [FieldTypeId], [EntityTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Key], [Name], [Description], [Order], [IsGridColumn], [DefaultValue],
    [IsMultiValue], [IsRequired], [Guid], [CreatedDateTime], [ModifiedDateTime], [IsActive], [IsPublic], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @IsSystem, @FieldTypeId, @EntityTypeId, @Column, @Value, @Key, @Name, @Description, @Order, 0, @Default, 0, @Required, @Guid, @Created, @Created, 1, 0, @ForeignKey )",
                P( "@IsSystem", isSystem ), P( "@FieldTypeId", fieldTypeId ), P( "@EntityTypeId", entityTypeId ), P( "@Column", qualifierColumn ), P( "@Value", qualifierValue ), P( "@Key", key ),
                P( "@Name", name ), P( "@Description", ( object ) description ?? DBNull.Value ), P( "@Order", order ), P( "@Default", ( object ) defaultValue ?? string.Empty ), P( "@Required", required ),
                P( "@Guid", guid ), P( "@Created", created ) );
        }

        private int RequireEntityType( string name ) => Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", name ) )
            ?? throw new InvalidOperationException( $"Rock has no entity type {name}." ) );

        private int FieldTypeId( string className ) => Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [FieldType] WHERE [Class] = @Class", P( "@Class", className ) )
            ?? throw new InvalidOperationException( $"Rock has no field type {className}." ) );

        private int Insert( string sql, params Microsoft.Data.SqlClient.SqlParameter[] parameters )
        {
            var all = parameters.Concat( sql.Contains( "@ForeignKey" ) ? new[] { P( "@ForeignKey", _foreignKey ) } : Array.Empty<Microsoft.Data.SqlClient.SqlParameter>() ).ToArray();
            return Convert.ToInt32( _db.Scalar( sql, all ) );
        }

        private int NextId( string table ) => Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );
    }
}
